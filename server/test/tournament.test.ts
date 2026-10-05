import { test } from 'node:test';
import assert from 'node:assert/strict';
import { generateGame, replay, RULES, type Move } from '../src/game.ts';
import { ApiError, TournamentService, type ServiceOptions } from '../src/tournament.ts';
import { playHonestly } from '../src/simulate.ts';

const SEED = 20261004;

function setup(opts: ServiceOptions = {}) {
  const clock = { t: 1_700_000_000_000 };
  const service = new TournamentService({ now: () => clock.t, newSeed: () => SEED, ...opts });
  const login = (device: string) => service.login(device).playerId;
  return { service, clock, login, advance: (ms: number) => (clock.t += ms) };
}

function expectApiError(fn: () => unknown, status: number, code: string) {
  assert.throws(fn, (err: unknown) => {
    assert.ok(err instanceof ApiError, `expected ApiError, got ${String(err)}`);
    assert.equal(err.status, status);
    assert.equal(err.code, code);
    return true;
  });
}

const honest = (bingoDelayMs = 800): Move[] =>
  playHonestly(generateGame(SEED), { reactionMs: () => 500, miss: () => false, bingoDelayMs });
const honestScore = (moves: Move[]) => {
  const r = replay(SEED, moves);
  assert.ok(r.ok);
  return r.score;
};
const submitBody = (moves: Move[], clientScore = moves.length ? honestScore(moves) : 0) => ({ moves, clientScore });

test('login creates a player with 1000 coins and rotates the token', () => {
  const { service } = setup();
  const a = service.login('device-a');
  assert.equal(a.balance, 1000);
  const again = service.login('device-a');
  assert.equal(again.playerId, a.playerId);
  assert.notEqual(again.token, a.token);
  expectApiError(() => service.authenticate(a.token), 401, 'unauthorized');
  assert.equal(service.authenticate(again.token).id, a.playerId);
});

test('enter is idempotent: same key -> same match, balance debited once', () => {
  const { service, login } = setup();
  const p = login('a');
  const first = service.enter(p, 'bronze', 'key-1');
  const retry = service.enter(p, 'bronze', 'key-1');
  assert.equal(retry.matchId, first.matchId);
  assert.equal(service.wallet(p).balance, 990);
  const other = service.enter(p, 'bronze', 'key-2');
  assert.notEqual(other.matchId, first.matchId);
  assert.equal(service.wallet(p).balance, 980);
});

test('enter requires an Idempotency-Key, a known tournament, and the key cannot switch tournaments', () => {
  const { service, login } = setup();
  const p = login('a');
  expectApiError(() => service.enter(p, 'bronze', null), 400, 'idempotency-key-required');
  expectApiError(() => service.enter(p, 'platinum', 'k'), 404, 'tournament-not-found');
  service.enter(p, 'bronze', 'k2');
  expectApiError(() => service.enter(p, 'gold', 'k2'), 409, 'idempotency-key-reused');
});

test('insufficient funds -> 402, nothing debited', () => {
  const { service, login } = setup();
  const p = login('a');
  for (let i = 0; i < 10; i++) service.enter(p, 'gold', `k${i}`);
  assert.equal(service.wallet(p).balance, 0);
  expectApiError(() => service.enter(p, 'bronze', 'one-more'), 402, 'insufficient-funds');
  assert.equal(service.wallet(p).balance, 0);
});

test('two players share a room and a seed; a player never joins their own room', () => {
  let n = 100;
  const { service, login } = setup({ newSeed: () => n++ });
  const a = login('a');
  const b = login('b');
  const a1 = service.enter(a, 'bronze', 'a1');
  const a2 = service.enter(a, 'bronze', 'a2');
  assert.notEqual(a2.seed, a1.seed, 'second entry by the same player opens a new room');
  const b1 = service.enter(b, 'bronze', 'b1');
  assert.equal(b1.seed, a1.seed, 'b joins the oldest open room');
  const g1 = service.enter(b, 'gold', 'b2');
  assert.notEqual(g1.seed, a2.seed, 'rooms are per tournament');
});

test('win payout: floor(2 * fee * 0.9), loser gets nothing; views agree', () => {
  const { service, login, advance } = setup();
  const a = login('a');
  const b = login('b');
  const ma = service.enter(a, 'bronze', 'a');
  const mb = service.enter(b, 'bronze', 'b');
  advance(RULES.matchDurationMs);

  const moves = honest();
  const va = service.submit(a, ma.matchId, submitBody(moves));
  assert.equal(va.status, 'waiting-opponent');
  assert.equal(va.opponentScore, null);
  assert.equal(va.result, null);

  const vb = service.submit(b, mb.matchId, submitBody([]));
  assert.equal(vb.status, 'completed');
  assert.equal(vb.result, 'loss');
  assert.equal(vb.payout, 0);
  assert.equal(vb.balance, 990);

  const final = service.getMatch(a, ma.matchId);
  assert.equal(final.result, 'win');
  assert.equal(final.score, honestScore(moves));
  assert.equal(final.opponentScore, 0);
  assert.equal(final.payout, 18);
  assert.equal(final.balance, 1000 - 10 + 18);
  assert.deepEqual(final.flags, []);
});

test('tie refunds both fees', () => {
  const { service, login, advance } = setup();
  const a = login('a');
  const b = login('b');
  const ma = service.enter(a, 'gold', 'a');
  const mb = service.enter(b, 'gold', 'b');
  advance(RULES.matchDurationMs);
  service.submit(a, ma.matchId, submitBody(honest()));
  const vb = service.submit(b, mb.matchId, submitBody(honest()));
  assert.equal(vb.result, 'tie');
  assert.equal(vb.payout, 100);
  assert.equal(service.wallet(a).balance, 1000);
  assert.equal(service.wallet(b).balance, 1000);
});

test('the multiplier is snapshotted at entry, not at resolve', () => {
  const { service, login, advance } = setup();
  const a = login('a');
  const b = login('b');
  service.createLiveOpsEvent({ startsInSec: 0, durationSec: 60, multiplier: 2 });
  const ma = service.enter(a, 'bronze', 'a'); // during the event
  advance(61_000);
  assert.equal(service.currentMultiplier(), 1);
  const mb = service.enter(b, 'bronze', 'b'); // after it ended
  assert.equal(ma.multiplier, 2);
  assert.equal(mb.multiplier, 1);
  advance(RULES.matchDurationMs);
  service.submit(b, mb.matchId, submitBody([]));
  const va = service.submit(a, ma.matchId, submitBody(honest()));
  assert.equal(va.payout, 36);
});

test('live-ops lists only events that have not ended; future events are inactive', () => {
  const { service, advance } = setup();
  const ev = service.createLiveOpsEvent({ startsInSec: 10, durationSec: 5, multiplier: 3 });
  assert.equal(service.currentMultiplier(), 1);
  assert.equal(service.liveOps().events.length, 1);
  advance(10_000);
  assert.equal(service.currentMultiplier(), 3);
  advance(5_000);
  assert.equal(service.currentMultiplier(), 1);
  assert.equal(service.liveOps().events.length, 0);
  assert.equal(ev.endsAt - ev.startsAt, 5000);
});

test('resubmit: same moves -> identical view; different moves -> 409', () => {
  const { service, login, advance } = setup();
  const a = login('a');
  const m = service.enter(a, 'bronze', 'a');
  advance(RULES.matchDurationMs);
  const moves = honest();
  const first = service.submit(a, m.matchId, submitBody(moves));
  const again = service.submit(a, m.matchId, submitBody(moves));
  assert.deepEqual(again, first);
  expectApiError(() => service.submit(a, m.matchId, submitBody(honest(1500))), 409, 'already-submitted');
});

test('client score is a prediction: a mismatch is flagged, the server score stands', () => {
  const { service, login, advance } = setup();
  const a = login('a');
  const m = service.enter(a, 'bronze', 'a');
  advance(RULES.matchDurationMs);
  const moves = honest();
  const v = service.submit(a, m.matchId, { moves, clientScore: 999_999 });
  assert.equal(v.score, honestScore(moves));
  assert.deepEqual(v.flags, ['client-score-mismatch']);
});

test('an illegal log is consumed with score 0 and a reason (still a normal response)', () => {
  const { service, login, advance } = setup();
  const a = login('a');
  const m = service.enter(a, 'bronze', 'a');
  advance(10_000);
  const v = service.submit(a, m.matchId, { moves: [{ kind: 'daub', cell: 12, t: 5000 }], clientScore: 100 });
  assert.equal(v.score, 0);
  assert.equal(v.rejectedReason, 'cell-out-of-range');
  assert.deepEqual(v.flags, ['client-score-mismatch']);
});

test('timing bound: moves later than server-elapsed + tolerance are rejected', () => {
  const { service, login, advance } = setup({ clockToleranceMs: 3000 });
  const a = login('a');
  const moves = honest();
  const lastT = moves[moves.length - 1].t;

  const early = service.enter(a, 'bronze', 'early');
  advance(lastT - 3001);
  const v = service.submit(a, early.matchId, submitBody(moves));
  assert.equal(v.score, 0);
  assert.equal(v.rejectedReason, 'moves-after-server-time');

  const ok = service.enter(a, 'bronze', 'ok');
  advance(lastT - 3000);
  assert.equal(service.submit(a, ok.matchId, submitBody(moves)).rejectedReason, null);
});

test('shape errors are 400 and do not consume the entry', () => {
  const { service, login, advance } = setup();
  const a = login('a');
  const m = service.enter(a, 'bronze', 'a');
  advance(RULES.matchDurationMs);
  const bad: unknown[] = [
    null,
    { moves: 'nope', clientScore: 0 },
    { moves: [{ kind: 'jump', cell: 1, t: 1 }], clientScore: 0 },
    { moves: [{ kind: 'daub', cell: '1', t: 1 }], clientScore: 0 },
    { moves: [], clientScore: 1.5 },
    { moves: [], clientScore: -1 },
    { moves: Array.from({ length: 65 }, (_, i) => ({ kind: 'bingo', cell: -1, t: i * 200 })), clientScore: 0 },
  ];
  for (const body of bad) expectApiError(() => service.submit(a, m.matchId, body), 400, 'bad-request');
  assert.equal(service.getMatch(a, m.matchId).status, 'playing');
  assert.equal(service.submit(a, m.matchId, submitBody([])).status, 'waiting-opponent');
});

test("another player's match is 404", () => {
  const { service, login } = setup();
  const a = login('a');
  const b = login('b');
  const m = service.enter(a, 'bronze', 'a');
  expectApiError(() => service.getMatch(b, m.matchId), 404, 'match-not-found');
  expectApiError(() => service.submit(b, m.matchId, submitBody([])), 404, 'match-not-found');
});

test('forfeit after the deadline: score 0 + flag, submit -> 410, the opponent wins', () => {
  const { service, login, advance } = setup({ submitGraceMs: 60_000 });
  const a = login('a');
  const b = login('b');
  const ma = service.enter(a, 'bronze', 'a');
  const mb = service.enter(b, 'bronze', 'b');
  advance(RULES.matchDurationMs);
  service.submit(b, mb.matchId, submitBody(honest()));

  advance(60_000);
  assert.equal(service.getMatch(a, ma.matchId).status, 'playing', 'deadline is inclusive');
  advance(1);
  expectApiError(() => service.submit(a, ma.matchId, submitBody(honest())), 410, 'match-expired');
  const va = service.getMatch(a, ma.matchId);
  assert.equal(va.status, 'completed');
  assert.equal(va.score, 0);
  assert.deepEqual(va.flags, ['forfeit-timeout']);
  assert.equal(va.result, 'loss');
  assert.equal(service.getMatch(b, mb.matchId).result, 'win');
});

test('a single-entry room is refunded after the TTL', () => {
  const { service, login, advance } = setup({ roomTtlMs: 30 * 60_000 });
  const a = login('a');
  const m = service.enter(a, 'bronze', 'a');
  advance(RULES.matchDurationMs);
  service.submit(a, m.matchId, submitBody(honest()));
  assert.equal(service.wallet(a).balance, 990);

  advance(30 * 60_000 - RULES.matchDurationMs + 1);
  const v = service.getMatch(a, m.matchId);
  assert.equal(v.status, 'refunded');
  assert.equal(v.payout, 10);
  assert.equal(v.balance, 1000);

  // A refunded room is closed: a newcomer opens a fresh room, and the refund happens once.
  const b = login('b');
  service.sweep();
  assert.equal(service.enter(b, 'bronze', 'b').status, 'playing');
  assert.equal(service.wallet(a).balance, 1000);
});

test('leaderboard is the top 10 by balance', () => {
  const { service, login } = setup();
  const ids = Array.from({ length: 12 }, (_, i) => login(`d${i}`));
  service.enter(ids[0], 'gold', 'k');
  const board = service.leaderboard();
  assert.equal(board.length, 10);
  assert.ok(!board.some((r) => r.playerId === ids[0]));
  assert.ok(board.every((r) => r.balance === 1000));
});
