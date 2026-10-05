// Builds spec/vectors.json: the cross-language determinism contract. The Node tests and the
// Unity EditMode tests both assert against the committed file, so a divergence in the PRNG,
// shuffle, game generation or scoring shows up on whichever side drifted.

import { XorShift32 } from './prng.ts';
import { generateGame, replay, RULES, type Move } from './game.ts';
import { playHonestly } from './simulate.ts';

// Flat shapes throughout so Unity's JsonUtility can read the file without Newtonsoft.
export type ReplayExpected = {
  ok: boolean;
  score: number;
  daubs: number;
  lines: number;
  falseBingos: number;
  reason: string; // "" when ok
  moveIndex: number; // -1 when ok
};

export type Vectors = {
  version: number;
  nextUInt: { seed: number; values: number[] }[];
  next: { seed: number; n: number; values: number[] }[];
  games: { seed: number; card: number[]; calls: number[] }[];
  replays: { name: string; seed: number; moves: Move[]; expected: ReplayExpected }[];
};

export function buildVectors(): Vectors {
  const nextUInt = [0, 1, 42, 0xdeadbeef, 0xffffffff].map((seed) => {
    const rng = new XorShift32(seed);
    return { seed, values: Array.from({ length: 10 }, () => rng.nextUInt()) };
  });

  // 3 * 2^30 rejects ~25% of draws, so the rejection branch is exercised, not just the happy path.
  const next = [1, 7, 15, 75, 3 * 2 ** 30].map((n) => {
    const seed = 12345;
    const rng = new XorShift32(seed);
    return { seed, n, values: Array.from({ length: 20 }, () => rng.next(n)) };
  });

  const games = [1, 20261004, 0xffffffff].map((seed) => ({ seed, ...generateGame(seed) }));

  const replays: Vectors['replays'] = [];
  const add = (name: string, seed: number, moves: Move[]) => {
    const r = replay(seed, moves);
    const expected: ReplayExpected = r.ok
      ? { ok: true, score: r.score, daubs: r.daubs, lines: r.lines, falseBingos: r.falseBingos, reason: '', moveIndex: -1 }
      : { ok: false, score: 0, daubs: 0, lines: 0, falseBingos: 0, reason: r.reason, moveIndex: r.moveIndex };
    replays.push({ name, seed, moves, expected });
  };

  {
    const seed = 20261004;
    add('honest-steady', seed, playHonestly(generateGame(seed), { reactionMs: () => 600, miss: () => false, bingoDelayMs: 1000 }));
  }
  {
    const seed = 777;
    const style = new XorShift32(99); // deterministic "human" variance
    add(
      'honest-varied-with-misses',
      seed,
      playHonestly(generateGame(seed), {
        reactionMs: () => 250 + style.next(2500),
        miss: () => style.next(5) === 0,
        bingoDelayMs: 800,
      }),
    );
  }
  add('false-bingo-clamped-to-zero', 1, [{ kind: 'bingo', cell: -1, t: 1000 }]);
  {
    const seed = 1;
    const { card, calls } = generateGame(seed);
    const i = calls.findIndex((num, idx) => idx > 0 && card.includes(num));
    const cell = card.indexOf(calls[i]);
    const callT = i * RULES.callIntervalMs;
    add('reject-daub-before-call', seed, [{ kind: 'daub', cell, t: callT - 500 }]);
    add('reject-implausible-reaction', seed, [{ kind: 'daub', cell, t: callT + 100 }]);
  }

  return { version: 1, nextUInt, next, games, replays };
}

// Pretty-printed, but arrays of plain values stay on one line so the file is reviewable.
export function formatVectors(v: Vectors): string {
  return (
    JSON.stringify(v, null, 2).replace(/\[\s+([^[\]{}]*?)\s+\]/g, (_, inner: string) => `[${inner.split(/,\s+/).join(', ')}]`) +
    '\n'
  );
}
