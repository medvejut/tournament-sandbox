// Real HTTP on an ephemeral port, with a fake clock in the service so matches can "last" 120 s
// without the test waiting for them.

import { test, before, after } from 'node:test';
import assert from 'node:assert/strict';
import type { AddressInfo } from 'node:net';
import { createApp, type App } from '../src/http.ts';
import { TournamentService } from '../src/tournament.ts';
import { generateGame, replay, RULES } from '../src/game.ts';
import { playHonestly } from '../src/simulate.ts';

const SEED = 20261004;
const clock = { t: 1_700_000_000_000 };
const service = new TournamentService({ now: () => clock.t, newSeed: () => SEED });
let app: App;
let base = '';

before(async () => {
  app = createApp(service, { log: () => {} });
  await new Promise<void>((resolve) => app.server.listen(0, '127.0.0.1', resolve));
  base = `http://127.0.0.1:${(app.server.address() as AddressInfo).port}`;
});

after(() => {
  app.server.closeAllConnections();
  app.server.close();
});

type Res = { status: number; body: any };

async function call(method: string, path: string, opts: { token?: string; body?: unknown; headers?: Record<string, string> } = {}): Promise<Res> {
  const headers: Record<string, string> = { ...opts.headers };
  if (opts.token) headers.authorization = `Bearer ${opts.token}`;
  if (opts.body !== undefined) headers['content-type'] = 'application/json';
  const res = await fetch(base + path, { method, headers, body: opts.body === undefined ? undefined : JSON.stringify(opts.body) });
  return { status: res.status, body: await res.json() };
}

async function login(deviceId: string): Promise<string> {
  const r = await call('POST', '/v1/login', { body: { deviceId } });
  assert.equal(r.status, 200);
  return r.body.token;
}

const moves = playHonestly(generateGame(SEED), { reactionMs: () => 450, miss: () => false, bingoDelayMs: 700 });
const score = (() => {
  const r = replay(SEED, moves);
  assert.ok(r.ok);
  return r.score;
})();

test('happy path: login -> tournaments -> enter -> submit -> resolve', async () => {
  const ta = await login('api-a');
  const tb = await login('api-b');

  const time = await call('GET', '/v1/time');
  assert.equal(time.body.serverTime, clock.t);
  assert.deepEqual((await call('GET', '/v1/tournaments', { token: ta })).body.tournaments.map((t: any) => t.id), ['bronze', 'gold']);

  const ea = await call('POST', '/v1/matches', { token: ta, body: { tournamentId: 'bronze' }, headers: { 'idempotency-key': 'a-1' } });
  assert.equal(ea.status, 200);
  assert.equal(ea.body.status, 'playing');
  assert.equal(ea.body.seed, SEED);
  assert.equal(ea.body.durationMs, RULES.matchDurationMs);
  const eb = await call('POST', '/v1/matches', { token: tb, body: { tournamentId: 'bronze' }, headers: { 'idempotency-key': 'b-1' } });
  assert.equal(eb.body.seed, SEED);

  clock.t += RULES.matchDurationMs;
  const sa = await call('POST', `/v1/matches/${ea.body.matchId}/submit`, { token: ta, body: { moves, clientScore: score } });
  assert.equal(sa.status, 200);
  assert.equal(sa.body.status, 'waiting-opponent');
  assert.equal(sa.body.score, score);

  const sb = await call('POST', `/v1/matches/${eb.body.matchId}/submit`, { token: tb, body: { moves: [], clientScore: 0 } });
  assert.equal(sb.body.status, 'completed');
  assert.equal(sb.body.result, 'loss');

  const ga = await call('GET', `/v1/matches/${ea.body.matchId}`, { token: ta });
  assert.equal(ga.body.result, 'win');
  assert.equal(ga.body.opponentScore, 0);
  assert.equal((await call('GET', '/v1/wallet', { token: ta })).body.balance, 1008);
});

test('errors: missing Idempotency-Key -> 400, bad token -> 401, unknown route -> 404, bad JSON -> 400', async () => {
  const t = await login('api-errors');
  const noKey = await call('POST', '/v1/matches', { token: t, body: { tournamentId: 'bronze' } });
  assert.equal(noKey.status, 400);
  assert.equal(noKey.body.error.code, 'idempotency-key-required');

  const bad = await call('GET', '/v1/wallet', { token: 'nope' });
  assert.equal(bad.status, 401);
  assert.equal(bad.body.error.code, 'unauthorized');

  assert.equal((await call('GET', '/v1/nothing')).status, 404);

  const res = await fetch(`${base}/v1/login`, { method: 'POST', body: '{not json' });
  assert.equal(res.status, 400);
  assert.equal(((await res.json()) as any).error.code, 'bad-json');
});

test('body larger than 64 KB -> 413', async () => {
  const res = await fetch(`${base}/v1/login`, { method: 'POST', body: JSON.stringify({ deviceId: 'x'.repeat(70_000) }) });
  assert.equal(res.status, 413);
});

test('fail chaos -> 503; dev routes are exempt', async () => {
  const set = await call('POST', '/v1/dev/chaos', { body: { failRate: 1 } });
  assert.equal(set.status, 200);
  try {
    const r = await call('GET', '/v1/time');
    assert.equal(r.status, 503);
    assert.equal(r.body.error.code, 'chaos-fail');
  } finally {
    assert.equal((await call('POST', '/v1/dev/chaos', { body: { failRate: 0 } })).status, 200);
  }
});

test('drop chaos on submit, then retry -> identical result and the payout is credited once', async () => {
  const ta = await login('drop-a');
  const tb = await login('drop-b');
  const enter = (token: string, key: string) =>
    call('POST', '/v1/matches', { token, body: { tournamentId: 'gold' }, headers: { 'idempotency-key': key } });

  const ma = (await enter(ta, 'drop-a-1')).body;
  const mb = (await enter(tb, 'drop-b-1')).body;
  assert.equal(ma.seed, mb.seed);
  clock.t += RULES.matchDurationMs;
  await call('POST', `/v1/matches/${ma.matchId}/submit`, { token: ta, body: { moves: [], clientScore: 0 } });

  // B's submit is handled (the room resolves, B is paid) but the response never arrives.
  app.chaos.dropRate = 1;
  const submitB = () => call('POST', `/v1/matches/${mb.matchId}/submit`, { token: tb, body: { moves, clientScore: score } });
  await assert.rejects(submitB(), 'the client sees a network error, not a response');
  app.chaos.dropRate = 0;

  const balanceAfterDrop = (await call('GET', '/v1/wallet', { token: tb })).body.balance;
  assert.equal(balanceAfterDrop, 1000 - 100 + 180, 'the dropped request did change state');

  // The client retries with the same log: it gets the stored result, and nothing is paid twice.
  const retry = await submitB();
  assert.equal(retry.status, 200);
  assert.equal(retry.body.status, 'completed');
  assert.equal(retry.body.result, 'win');
  assert.equal(retry.body.payout, 180);
  assert.equal(retry.body.balance, balanceAfterDrop);
  const retry2 = await submitB();
  assert.deepEqual(retry2.body, retry.body);

  // Same story for enter: a retried key never charges twice, even when the first response was lost.
  app.chaos.dropRate = 1;
  await assert.rejects(enter(ta, 'drop-a-2'));
  app.chaos.dropRate = 0;
  const before = (await call('GET', '/v1/wallet', { token: ta })).body.balance;
  const again = await enter(ta, 'drop-a-2');
  assert.equal(again.status, 200);
  assert.equal(again.body.balance, before);
});
