// Tiny HTTP layer: router, JSON body parsing (64 KB cap), error mapping, chaos, request log.
// All domain decisions live in TournamentService; this file only translates HTTP <-> calls.

import { createServer, type IncomingMessage, type Server, type ServerResponse } from 'node:http';
import { ApiError, type TournamentService } from './tournament.ts';

const MAX_BODY_BYTES = 64 * 1024;

export type ChaosConfig = {
  failRate: number; // 0..1: answer 503 before handling
  dropRate: number; // 0..1: handle (state changes!), then destroy the socket without a response
  delayMs: number; // random 0..delayMs before handling
};

export type AppOptions = {
  chaos?: Partial<ChaosConfig>;
  devRoutes?: boolean;
  log?: (line: string) => void;
  random?: () => number;
};

type Ctx = {
  req: IncomingMessage;
  params: Record<string, string>;
  body: unknown;
  playerId: string; // '' on routes without auth
};

type Route = {
  method: string;
  pattern: RegExp;
  keys: string[];
  auth: boolean;
  dev: boolean;
  handler: (ctx: Ctx) => unknown;
};

export type App = { server: Server; chaos: ChaosConfig };

export function createApp(service: TournamentService, opts: AppOptions = {}): App {
  const chaos: ChaosConfig = { failRate: 0, dropRate: 0, delayMs: 0, ...opts.chaos };
  const devRoutes = opts.devRoutes ?? true;
  const log = opts.log ?? ((line: string) => console.log(line));
  const random = opts.random ?? Math.random;

  const routes: Route[] = [];
  const route = (method: string, path: string, flags: { auth?: boolean; dev?: boolean }, handler: Route['handler']) => {
    const keys: string[] = [];
    const pattern = new RegExp(
      '^' +
        path.replace(/:([a-zA-Z]+)/g, (_, k: string) => {
          keys.push(k);
          return '([^/]+)';
        }) +
        '$',
    );
    routes.push({ method, pattern, keys, auth: flags.auth ?? true, dev: flags.dev ?? false, handler });
  };

  route('POST', '/v1/login', { auth: false }, (c) => service.login(field(c.body, 'deviceId')));
  route('GET', '/v1/time', { auth: false }, () => ({ serverTime: service.now() }));
  route('GET', '/v1/wallet', {}, (c) => service.wallet(c.playerId));
  // Lists are wrapped in an object: Unity's JsonUtility can't parse a top-level array.
  route('GET', '/v1/tournaments', {}, () => ({ tournaments: service.tournaments() }));
  route('POST', '/v1/matches', {}, (c) =>
    service.enter(c.playerId, field(c.body, 'tournamentId'), header(c.req, 'idempotency-key')),
  );
  route('POST', '/v1/matches/:id/submit', {}, (c) => service.submit(c.playerId, c.params.id, c.body));
  route('GET', '/v1/matches/:id', {}, (c) => service.getMatch(c.playerId, c.params.id));
  route('GET', '/v1/liveops', {}, () => service.liveOps());
  route('GET', '/v1/leaderboard', {}, () => ({ entries: service.leaderboard() }));
  route('POST', '/v1/dev/liveops', { auth: false, dev: true }, (c) => service.createLiveOpsEvent(c.body));
  route('POST', '/v1/dev/chaos', { auth: false, dev: true }, (c) => {
    const b = c.body;
    if (typeof b !== 'object' || b === null) throw new ApiError(400, 'bad-request', 'body must be a JSON object');
    const next = { ...chaos, ...(b as Partial<ChaosConfig>) };
    for (const k of ['failRate', 'dropRate'] as const) {
      if (typeof next[k] !== 'number' || next[k] < 0 || next[k] > 1) throw new ApiError(400, 'bad-request', `${k} must be in 0..1`);
    }
    if (typeof next.delayMs !== 'number' || next.delayMs < 0) throw new ApiError(400, 'bad-request', 'delayMs must be >= 0');
    Object.assign(chaos, { failRate: next.failRate, dropRate: next.dropRate, delayMs: next.delayMs });
    return { ...chaos };
  });

  const server = createServer((req, res) => {
    const started = performance.now();
    let note = '';
    handle(req, res)
      .then((n) => (note = n))
      .catch((err: unknown) => {
        note = 'unhandled';
        log(`[http] unhandled error: ${err instanceof Error ? err.stack : String(err)}`);
        if (!res.headersSent) send(res, 500, { error: { code: 'internal', message: 'internal error' } });
      })
      .finally(() => {
        const ms = (performance.now() - started).toFixed(0);
        const status = res.headersSent ? res.statusCode : '---';
        log(`${req.method} ${req.url} ${status} ${ms}ms${note ? ` (${note})` : ''}`);
      });
  });

  // Returns a note for the request log ('' when nothing unusual happened).
  async function handle(req: IncomingMessage, res: ServerResponse): Promise<string> {
    const url = new URL(req.url ?? '/', 'http://localhost');
    const path = url.pathname.replace(/\/+$/, '') || '/';

    const pathMatches = routes.filter((r) => r.pattern.test(path) && (!r.dev || devRoutes));
    const r = pathMatches.find((x) => x.method === req.method);
    if (!r) {
      if (pathMatches.length > 0) return fail(res, new ApiError(405, 'method-not-allowed'));
      return fail(res, new ApiError(404, 'not-found', `no route ${req.method} ${path}`));
    }

    const chaotic = !r.dev && path.startsWith('/v1/');
    if (chaotic && chaos.delayMs > 0) await sleep(random() * chaos.delayMs);
    if (chaotic && chaos.failRate > 0 && random() < chaos.failRate) {
      send(res, 503, { error: { code: 'chaos-fail', message: 'injected failure' } });
      return 'chaos fail';
    }

    try {
      const m = r.pattern.exec(path)!;
      const params: Record<string, string> = {};
      r.keys.forEach((k, i) => (params[k] = decodeURIComponent(m[i + 1])));
      const body = req.method === 'POST' ? await readJson(req) : undefined;
      const playerId = r.auth ? service.authenticate(bearer(req)).id : '';
      const result = r.handler({ req, params, body, playerId });

      if (chaotic && chaos.dropRate > 0 && random() < chaos.dropRate) {
        // The state change above already happened; the client just never hears about it.
        req.socket.destroy();
        return 'chaos drop: handled, response dropped';
      }
      send(res, 200, result);
      return '';
    } catch (err) {
      if (err instanceof ApiError) return fail(res, err);
      throw err;
    }
  }

  return { server, chaos };
}

function fail(res: ServerResponse, err: ApiError): string {
  send(res, err.status, { error: { code: err.code, message: err.message } });
  return err.code;
}

function send(res: ServerResponse, status: number, payload: unknown): void {
  const json = JSON.stringify(payload);
  res.writeHead(status, { 'content-type': 'application/json; charset=utf-8', 'content-length': Buffer.byteLength(json) });
  res.end(json);
}

async function readJson(req: IncomingMessage): Promise<unknown> {
  const chunks: Buffer[] = [];
  let size = 0;
  for await (const chunk of req) {
    size += (chunk as Buffer).length;
    if (size > MAX_BODY_BYTES) throw new ApiError(413, 'payload-too-large', `body exceeds ${MAX_BODY_BYTES} bytes`);
    chunks.push(chunk as Buffer);
  }
  if (size === 0) return {};
  try {
    return JSON.parse(Buffer.concat(chunks).toString('utf8'));
  } catch {
    throw new ApiError(400, 'bad-json', 'body is not valid JSON');
  }
}

function header(req: IncomingMessage, name: string): string | null {
  const v = req.headers[name];
  if (Array.isArray(v)) return v[0] ?? null;
  return v ?? null;
}

function bearer(req: IncomingMessage): string | null {
  const auth = header(req, 'authorization');
  const m = auth ? /^Bearer\s+(.+)$/i.exec(auth) : null;
  return m ? m[1].trim() : null;
}

function field(body: unknown, key: string): unknown {
  return typeof body === 'object' && body !== null ? (body as Record<string, unknown>)[key] : undefined;
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
