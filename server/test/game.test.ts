import { test } from 'node:test';
import assert from 'node:assert/strict';
import { generateGame, replay, LINES, RULES, type Move } from '../src/game.ts';
import { playHonestly } from '../src/simulate.ts';

const SEED = 20261004;
const game = generateGame(SEED);

// The k-th call (k >= 1, skipping call 0) whose number is on the card.
function onCardCall(k = 1): { cell: number; t: number } {
  let seen = 0;
  for (let i = 1; i < game.calls.length; i++) {
    const cell = game.card.indexOf(game.calls[i]);
    if (cell >= 0 && ++seen === k) return { cell, t: i * RULES.callIntervalMs };
  }
  throw new Error('not enough on-card calls');
}

// A number on the card that is never called in this match.
function uncalledCell(): number {
  const cell = game.card.findIndex((n, i) => i !== RULES.freeCell && !game.calls.includes(n));
  assert.ok(cell >= 0);
  return cell;
}

const daub = (cell: number, t: number): Move => ({ kind: 'daub', cell, t });
const bingo = (t: number): Move => ({ kind: 'bingo', cell: -1, t });

function rejectReason(moves: Move[]): string {
  const r = replay(SEED, moves);
  assert.equal(r.ok, false, 'expected a reject');
  return r.ok ? '' : r.reason;
}

test('card: columns in range, no duplicates, free centre; 60 unique calls', () => {
  for (const seed of [1, 2, 3, SEED, 0xffffffff]) {
    const { card, calls } = generateGame(seed);
    assert.equal(card.length, 25);
    assert.equal(card[RULES.freeCell], 0);
    for (let i = 0; i < 25; i++) {
      if (i === RULES.freeCell) continue;
      const col = i % 5;
      assert.ok(card[i] >= col * 15 + 1 && card[i] <= col * 15 + 15, `cell ${i} = ${card[i]}`);
    }
    assert.equal(new Set(card).size, 25);
    assert.equal(calls.length, 60);
    assert.equal(new Set(calls).size, 60);
    assert.ok(calls.every((n) => n >= 1 && n <= 75));
  }
});

test('12 lines: 5 rows, 5 columns, 2 diagonals', () => {
  assert.equal(LINES.length, 12);
  assert.deepEqual(LINES[0], [0, 1, 2, 3, 4]);
  assert.deepEqual(LINES[5], [0, 5, 10, 15, 20]);
  assert.deepEqual(LINES[10], [0, 6, 12, 18, 24]);
  assert.deepEqual(LINES[11], [4, 8, 12, 16, 20]);
});

test('a full valid honest replay', () => {
  const moves = playHonestly(game, { reactionMs: () => 500, miss: () => false, bingoDelayMs: 600 });
  const r = replay(SEED, moves);
  assert.ok(r.ok, JSON.stringify(r));
  assert.ok(r.daubs > 10 && r.lines >= 1 && r.falseBingos === 0);
});

test('empty log scores 0', () => {
  assert.deepEqual(replay(SEED, []), { ok: true, score: 0, daubs: 0, lines: 0, falseBingos: 0 });
});

test('speed bonus: 200 ms -> +48, 2.5 s -> +25, 5 s -> 0, 7 s -> 0', () => {
  const { cell, t } = onCardCall();
  const cases: [number, number][] = [
    [200, 148],
    [2500, 125],
    [5000, 100],
    [7000, 100],
  ];
  for (const [reaction, score] of cases) {
    const r = replay(SEED, [daub(cell, t + reaction)]);
    assert.ok(r.ok);
    assert.equal(r.score, score, `reaction ${reaction}`);
  }
});

test('one daub completing a row and a column at once = +1000 on the next claim', () => {
  // Find a seed where every number in some row and some column gets called.
  for (let seed = 1; seed < 500; seed++) {
    const g = generateGame(seed);
    for (let row = 0; row < 5; row++) {
      for (let col = 0; col < 5; col++) {
        const cells = [...new Set([...LINES[row], ...LINES[5 + col]])].filter((c) => c !== RULES.freeCell);
        if (!cells.every((c) => g.calls.includes(g.card[c]))) continue;

        const cross = row * 5 + col;
        const at = (c: number) => g.calls.indexOf(g.card[c]) * RULES.callIntervalMs;
        const others = cells.filter((c) => c !== cross).sort((a, b) => at(a) - at(b));
        const moves: Move[] = others.map((c) => daub(c, at(c) + 300));
        moves.push(daub(cross, Math.max(moves[moves.length - 1].t, at(cross)) + 300));
        const withClaim = [...moves, bingo(moves[moves.length - 1].t + 300)];

        const before = replay(seed, moves);
        const after = replay(seed, withClaim);
        assert.ok(before.ok && after.ok, JSON.stringify(after));
        assert.equal(after.lines, 2);
        assert.equal(after.score - before.score, 1000);

        // Claiming again with no new line is a false bingo.
        const again = replay(seed, [...withClaim, bingo(withClaim[withClaim.length - 1].t + 300)]);
        assert.ok(again.ok);
        assert.equal(again.falseBingos, 1);
        assert.equal(again.score, after.score - 200);
        return;
      }
    }
  }
  assert.fail('no suitable seed found');
});

test('false bingo costs 200, and only the final score is clamped at 0', () => {
  const { cell, t } = onCardCall();
  assert.deepEqual(replay(SEED, [daub(cell, t + 200), bingo(t + 1000)]), {
    ok: true,
    score: 0, // 148 - 200, clamped
    daubs: 1,
    lines: 0,
    falseBingos: 1,
  });

  const second = onCardCall(2);
  const r = replay(SEED, [bingo(100), daub(cell, t + 300), daub(second.cell, second.t + 300)]);
  assert.ok(r.ok);
  assert.equal(r.score, 147 + 147 - 200, 'running total may go negative mid-match');
});

test('reject: malformed-move', () => {
  assert.equal(rejectReason([daub(1.5, 3000)]), 'malformed-move');
  assert.equal(rejectReason([{ kind: 'jump' as 'daub', cell: 1, t: 3000 }]), 'malformed-move');
});

test('reject: too-many-moves', () => {
  const moves = Array.from({ length: RULES.maxMoves + 1 }, (_, i) => bingo(i * 200));
  assert.equal(rejectReason(moves), 'too-many-moves');
});

test('reject: outside-match-time', () => {
  assert.equal(rejectReason([bingo(-1)]), 'outside-match-time');
  assert.equal(rejectReason([bingo(RULES.matchDurationMs + 1)]), 'outside-match-time');
});

test('reject: implausible-speed', () => {
  assert.equal(rejectReason([bingo(1000), bingo(1149)]), 'implausible-speed');
});

test('reject: cell-out-of-range (including the free centre)', () => {
  assert.equal(rejectReason([daub(25, 3000)]), 'cell-out-of-range');
  assert.equal(rejectReason([daub(-1, 3000)]), 'cell-out-of-range');
  assert.equal(rejectReason([daub(RULES.freeCell, 3000)]), 'cell-out-of-range');
});

test('reject: cell-already-daubed', () => {
  const { cell, t } = onCardCall();
  assert.equal(rejectReason([daub(cell, t + 300), daub(cell, t + 600)]), 'cell-already-daubed');
});

test('reject: number-never-called', () => {
  assert.equal(rejectReason([daub(uncalledCell(), 5000)]), 'number-never-called');
});

test('reject: daub-before-call', () => {
  const { cell, t } = onCardCall();
  assert.equal(rejectReason([daub(cell, t - 1)]), 'daub-before-call');
});

test('reject: implausible-reaction (boundary is 200 ms)', () => {
  const { cell, t } = onCardCall();
  assert.equal(rejectReason([daub(cell, t + 199)]), 'implausible-reaction');
  assert.ok(replay(SEED, [daub(cell, t + 200)]).ok);
});
