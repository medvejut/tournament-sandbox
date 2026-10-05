// Deterministic PRNG shared with the Unity client (Assets/Scripts/Core/XorShift32.cs).
// Both sides must produce the same sequence for the same seed, so the algorithm is
// specified here rather than borrowed from Math.random / System.Random.
// spec/vectors.json pins the expected output; both test suites check against it.

const RANGE = 2 ** 32;

export class XorShift32 {
  #state: number;

  constructor(seed: number) {
    const s = seed >>> 0;
    this.#state = s !== 0 ? s : 0x9e3779b9;
  }

  nextUInt(): number {
    let x = this.#state;
    x = (x ^ (x << 13)) >>> 0;
    x = (x ^ (x >>> 17)) >>> 0;
    x = (x ^ (x << 5)) >>> 0;
    this.#state = x;
    return x;
  }

  // Uniform integer in [0, maxExclusive). Rejection sampling removes the modulo bias
  // that a plain `nextUInt() % n` would have.
  next(maxExclusive: number): number {
    if (!Number.isInteger(maxExclusive) || maxExclusive <= 0 || maxExclusive > RANGE) {
      throw new RangeError(`maxExclusive must be an integer in 1..2^32, got ${maxExclusive}`);
    }
    const limit = RANGE - (RANGE % maxExclusive);
    let r: number;
    do {
      r = this.nextUInt();
    } while (r >= limit);
    return r % maxExclusive;
  }
}

// Fisher–Yates. Swapping with j in [0, i] (not [0, n)) is what makes every permutation
// equally likely.
export function shuffle<T>(list: T[], rng: XorShift32): void {
  for (let i = list.length - 1; i > 0; i--) {
    const j = rng.next(i + 1);
    const tmp = list[i];
    list[i] = list[j];
    list[j] = tmp;
  }
}
