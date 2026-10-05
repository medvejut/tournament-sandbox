// TEST HARNESS: a scripted second seat, for exercising the server and the Unity client locally.
//
// It logs in as its own device, enters a tournament, plays the seed's game the way an honest
// human would (reaction-time noise, missed numbers, a delay before pressing BINGO), waits in
// real time until its last move has happened, then submits through the normal public API.
//
// It is NOT an opponent for real players and must never be presented as one: matching paying
// players against undisclosed bots is exactly the practice this project's README warns about.
//
//   npm run second-player -- --tournament bronze --reaction 900 --miss 0.2 --bingo-delay 1500
//   npm run second-player -- --no-wait      # submit immediately: shows the server's timing bound
//
// Options: --base http://localhost:8080  --device second-player-1  --no-poll

import { parseArgs } from 'node:util';
import { randomUUID } from 'node:crypto';
import { generateGame, replay, RULES } from '../src/game.ts';
import { playHonestly } from '../src/simulate.ts';

const { values: args } = parseArgs({
  options: {
    base: { type: 'string', default: process.env.BASE_URL ?? 'http://localhost:8080' },
    device: { type: 'string', default: 'second-player-1' },
    tournament: { type: 'string', default: 'bronze' },
    reaction: { type: 'string', default: '900' },
    miss: { type: 'string', default: '0.2' },
    'bingo-delay': { type: 'string', default: '1500' },
    'no-wait': { type: 'boolean', default: false },
    'no-poll': { type: 'boolean', default: false },
  },
});

const reactionMs = Number(args.reaction);
const missRate = Number(args.miss);
const bingoDelayMs = Number(args['bingo-delay']);
const base = args.base.replace(/\/+$/, '');

type Json = Record<string, any>;

class HttpError extends Error {
  readonly status: number;
  readonly body: Json;
  constructor(status: number, body: Json) {
    super(`HTTP ${status} ${body?.error?.code ?? ''}`);
    this.status = status;
    this.body = body;
  }
}

async function request(method: string, path: string, body?: unknown, headers: Record<string, string> = {}): Promise<Json> {
  const res = await fetch(base + path, {
    method,
    headers: { 'content-type': 'application/json', ...headers },
    body: body === undefined ? undefined : JSON.stringify(body),
    signal: AbortSignal.timeout(10_000),
  });
  const json = (await res.json()) as Json;
  if (!res.ok) throw new HttpError(res.status, json);
  return json;
}

// The harness retries like a good client should: same request, same Idempotency-Key / same move
// log, backoff, and only for errors that might succeed next time.
async function withRetry<T>(what: string, fn: () => Promise<T>): Promise<T> {
  for (let attempt = 1; ; attempt++) {
    try {
      return await fn();
    } catch (err) {
      const retryable = !(err instanceof HttpError) || err.status >= 500;
      if (!retryable || attempt >= 6) throw err;
      const delay = Math.min(4000, 250 * 2 ** attempt) * Math.random();
      log(`${what} failed (${err instanceof Error ? err.message : String(err)}), retry ${attempt} in ${delay.toFixed(0)} ms`);
      await sleep(delay);
    }
  }
}

const t0 = Date.now();
const log = (msg: string) => console.log(`[second-player +${((Date.now() - t0) / 1000).toFixed(1)}s] ${msg}`);
const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));
// Box–Muller; reaction times are roughly normal around the mean, never under 250 ms.
const gaussian = () => Math.sqrt(-2 * Math.log(1 - Math.random())) * Math.cos(2 * Math.PI * Math.random());

async function main() {
  log(`TEST HARNESS, not a real opponent. ${base}, device ${args.device}`);
  const session = await withRetry('login', () => request('POST', '/v1/login', { deviceId: args.device }));
  const auth = { authorization: `Bearer ${session.token}` };
  log(`logged in as ${session.playerId}, balance ${session.balance}`);

  const idempotencyKey = randomUUID(); // one per intent, reused across retries
  const match = await withRetry('enter', () =>
    request('POST', '/v1/matches', { tournamentId: args.tournament }, { ...auth, 'idempotency-key': idempotencyKey }),
  );
  const startedLocal = Date.now();
  log(`entered ${args.tournament}: match ${match.matchId}, seed ${match.seed}, multiplier x${match.multiplier}, balance ${match.balance}`);

  const game = generateGame(match.seed);
  log(`card (row-major, 0 = free):\n${formatCard(game.card)}`);
  log(`first calls: ${game.calls.slice(0, 10).join(' ')} ...`);

  const moves = playHonestly(game, {
    reactionMs: () => Math.max(250, reactionMs + gaussian() * reactionMs * 0.3),
    miss: () => Math.random() < missRate,
    bingoDelayMs,
  });
  const predicted = replay(match.seed, moves);
  if (!predicted.ok) throw new Error(`harness produced an illegal log: ${predicted.reason}`);
  const lastT = moves.length ? moves[moves.length - 1].t : 0;
  log(`planned ${moves.length} moves: ${predicted.daubs} daubs, ${predicted.lines} lines, predicted score ${predicted.score}, last move at ${(lastT / 1000).toFixed(1)}s`);

  if (args['no-wait']) {
    log('--no-wait: submitting now; expect rejectedReason "moves-after-server-time"');
  } else {
    // Honest play takes real time. Submitting a 2-minute log after 1 second is exactly what the
    // server's timing bound rejects.
    const waitMs = Math.max(0, lastT - (Date.now() - startedLocal));
    log(`playing... submitting in ${(waitMs / 1000).toFixed(1)}s`);
    await sleep(waitMs);
  }

  const submitted = await withRetry('submit', () =>
    request('POST', `/v1/matches/${match.matchId}/submit`, { moves, clientScore: predicted.score }, auth),
  );
  report(submitted);

  if (args['no-poll'] || submitted.status !== 'waiting-opponent') return;
  log('waiting for the opponent (Ctrl+C to stop)...');
  for (;;) {
    await sleep(2000);
    const view = await withRetry('poll', () => request('GET', `/v1/matches/${match.matchId}`, undefined, auth));
    if (view.status !== 'waiting-opponent') {
      report(view);
      return;
    }
  }
}

function report(v: Json) {
  const parts = [`status ${v.status}`, `server score ${v.score}`];
  if (v.rejectedReason) parts.push(`REJECTED: ${v.rejectedReason}`);
  if (v.flags?.length) parts.push(`flags ${v.flags.join(',')}`);
  if (v.status === 'completed') parts.push(`opponent ${v.opponentScore}`, `result ${v.result}`, `payout ${v.payout}`);
  if (v.status === 'refunded') parts.push(`refund ${v.payout}`);
  parts.push(`balance ${v.balance}`);
  log(parts.join(' | '));
}

function formatCard(card: number[]): string {
  const rows: string[] = ['   B  I  N  G  O'];
  for (let r = 0; r < RULES.size; r++) {
    rows.push('  ' + card.slice(r * RULES.size, (r + 1) * RULES.size).map((n) => String(n).padStart(2)).join(' '));
  }
  return rows.join('\n');
}

main().catch((err) => {
  log(`FAILED: ${err instanceof HttpError ? JSON.stringify(err.body) : err instanceof Error ? err.message : String(err)}`);
  process.exitCode = 1;
});
