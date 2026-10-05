// Speed Bingo rules. The server replays a client's move log against these rules to compute
// the authoritative score; the client runs the same code (Assets/Scripts/Core/Bingo.cs)
// only to *predict* the score it displays.
//
// One seed produces both the card and the call order, so both players in a room get an
// identical game and only reaction speed and attention separate them.

import { XorShift32, shuffle } from './prng.ts';

export const RULES = {
  size: 5, // 5x5 card, row-major cell index = row * 5 + col
  freeCell: 12, // centre
  numbersPerColumn: 15, // B 1-15, I 16-30, N 31-45, G 46-60, O 61-75
  callIntervalMs: 2000, // call i happens at t = i * callIntervalMs
  matchDurationMs: 120_000, // -> 60 of 75 numbers called
  daubPoints: 100,
  maxSpeedBonus: 50, // daub right after the call: +50, decaying by 1 per speedBonusDecayMs
  speedBonusDecayMs: 100,
  minReactionMs: 200, // faster than this after a call is not human
  minMoveIntervalMs: 150, // faster than this between two taps is not human
  linePoints: 500,
  falseBingoPenalty: 200,
  maxMoves: 64,
} as const;

export type Rules = typeof RULES;

export type BingoGame = {
  card: number[]; // 25 cells, 0 = free centre
  calls: number[]; // calls[i] is called at i * callIntervalMs
};

// Flat shape on purpose: Unity's JsonUtility can't deserialize discriminated unions.
// kind "daub": tap cell `cell`; kind "bingo": claim lines (`cell` = -1).
// t: ms since the match started on the client.
export type Move = { kind: 'daub' | 'bingo'; cell: number; t: number };

export type ReplayResult =
  | { ok: true; score: number; daubs: number; lines: number; falseBingos: number }
  | { ok: false; reason: string; moveIndex: number };

export const LINES: readonly (readonly number[])[] = buildLines(RULES.size);

function buildLines(n: number): number[][] {
  const lines: number[][] = [];
  for (let r = 0; r < n; r++) lines.push(Array.from({ length: n }, (_, c) => r * n + c));
  for (let c = 0; c < n; c++) lines.push(Array.from({ length: n }, (_, r) => r * n + c));
  lines.push(Array.from({ length: n }, (_, i) => i * n + i));
  lines.push(Array.from({ length: n }, (_, i) => i * n + (n - 1 - i)));
  return lines;
}

export function callCount(rules: Rules = RULES): number {
  return Math.ceil(rules.matchDurationMs / rules.callIntervalMs);
}

// Order of rng use is part of the contract: card columns B..O first, then the call order.
export function generateGame(seed: number, rules: Rules = RULES): BingoGame {
  const rng = new XorShift32(seed);
  const n = rules.size;
  const card = new Array<number>(n * n).fill(0);

  for (let col = 0; col < n; col++) {
    const range = Array.from({ length: rules.numbersPerColumn }, (_, i) => col * rules.numbersPerColumn + i + 1);
    shuffle(range, rng);
    for (let row = 0; row < n; row++) card[row * n + col] = range[row];
  }
  card[rules.freeCell] = 0;

  const all = Array.from({ length: n * rules.numbersPerColumn }, (_, i) => i + 1);
  shuffle(all, rng);
  return { card, calls: all.slice(0, callCount(rules)) };
}

export function replay(seed: number, moves: Move[], rules: Rules = RULES): ReplayResult {
  if (moves.length > rules.maxMoves) return { ok: false, reason: 'too-many-moves', moveIndex: rules.maxMoves };

  const { card, calls } = generateGame(seed, rules);
  const calledAt = new Map<number, number>();
  calls.forEach((num, i) => calledAt.set(num, i * rules.callIntervalMs));

  const daubed = new Array<boolean>(card.length).fill(false);
  daubed[rules.freeCell] = true;
  const claimed = new Array<boolean>(LINES.length).fill(false);

  let score = 0;
  let daubs = 0;
  let lines = 0;
  let falseBingos = 0;
  let lastT = 0;

  for (let i = 0; i < moves.length; i++) {
    const { kind, cell, t } = moves[i];
    const fail = (reason: string): ReplayResult => ({ ok: false, reason, moveIndex: i });

    if (!Number.isInteger(cell) || !Number.isInteger(t)) return fail('malformed-move');
    if (t < 0 || t > rules.matchDurationMs) return fail('outside-match-time');
    if (i > 0 && t - lastT < rules.minMoveIntervalMs) return fail('implausible-speed');
    lastT = t;

    if (kind === 'daub') {
      if (cell < 0 || cell >= card.length || cell === rules.freeCell) return fail('cell-out-of-range');
      if (daubed[cell]) return fail('cell-already-daubed');
      const callT = calledAt.get(card[cell]);
      if (callT === undefined) return fail('number-never-called');
      if (t < callT) return fail('daub-before-call');
      if (t - callT < rules.minReactionMs) return fail('implausible-reaction');

      daubed[cell] = true;
      daubs++;
      score += rules.daubPoints + Math.max(0, rules.maxSpeedBonus - Math.floor((t - callT) / rules.speedBonusDecayMs));
    } else if (kind === 'bingo') {
      let newLines = 0;
      LINES.forEach((line, li) => {
        if (!claimed[li] && line.every((c) => daubed[c])) {
          claimed[li] = true;
          newLines++;
        }
      });
      if (newLines === 0) {
        falseBingos++;
        score -= rules.falseBingoPenalty;
      } else {
        lines += newLines;
        score += newLines * rules.linePoints;
      }
    } else {
      return fail('malformed-move');
    }
  }

  return { ok: true, score: Math.max(0, score), daubs, lines, falseBingos };
}
