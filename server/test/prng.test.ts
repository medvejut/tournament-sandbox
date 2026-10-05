import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { XorShift32, shuffle } from '../src/prng.ts';
import { generateGame, replay } from '../src/game.ts';
import { buildVectors, formatVectors, type Vectors } from '../src/vectors.ts';

const vectorsPath = new URL('../../spec/vectors.json', import.meta.url);
const vectors = JSON.parse(readFileSync(vectorsPath, 'utf8')) as Vectors;

test('committed spec/vectors.json matches what the current code generates', () => {
  // If this fails, either the code drifted (fix it) or the contract changed on purpose
  // (run `npm run vectors`, commit, and update the Unity port to match).
  assert.equal(readFileSync(vectorsPath, 'utf8').replace(/\r\n/g, '\n'), formatVectors(buildVectors()));
});

test('nextUInt vectors', () => {
  for (const v of vectors.nextUInt) {
    const rng = new XorShift32(v.seed);
    assert.deepEqual(Array.from({ length: v.values.length }, () => rng.nextUInt()), v.values, `seed ${v.seed}`);
  }
});

test('next(n) vectors, including the rejection-heavy n', () => {
  for (const v of vectors.next) {
    const rng = new XorShift32(v.seed);
    assert.deepEqual(Array.from({ length: v.values.length }, () => rng.next(v.n)), v.values, `n ${v.n}`);
  }
});

test('game vectors', () => {
  for (const v of vectors.games) assert.deepEqual(generateGame(v.seed), { card: v.card, calls: v.calls }, `seed ${v.seed}`);
});

test('replay vectors', () => {
  for (const v of vectors.replays) {
    const r = replay(v.seed, v.moves);
    const e = v.expected;
    if (e.ok) assert.deepEqual(r, { ok: true, score: e.score, daubs: e.daubs, lines: e.lines, falseBingos: e.falseBingos }, v.name);
    else assert.deepEqual(r, { ok: false, reason: e.reason, moveIndex: e.moveIndex }, v.name);
  }
});

test('zero seed is remapped, not stuck at zero', () => {
  assert.equal(new XorShift32(0).nextUInt(), new XorShift32(0x9e3779b9).nextUInt());
  assert.notEqual(new XorShift32(0).nextUInt(), 0);
});

test('next() rejects bad bounds', () => {
  const rng = new XorShift32(1);
  for (const bad of [0, -1, 1.5, 2 ** 32 + 1, NaN]) assert.throws(() => rng.next(bad), RangeError);
  assert.doesNotThrow(() => rng.next(2 ** 32));
});

test('shuffle is a permutation', () => {
  const list = Array.from({ length: 50 }, (_, i) => i);
  shuffle(list, new XorShift32(5));
  assert.deepEqual([...list].sort((a, b) => a - b), Array.from({ length: 50 }, (_, i) => i));
});
