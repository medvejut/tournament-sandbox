// Produces a legitimate move log for a game: what an honest (if imperfect) human would tap.
// Used by scripts/gen-vectors.ts (fixed parameters, so the vectors are deterministic) and by
// scripts/second-player.ts (randomized parameters). It is a test harness, not an opponent
// that real players are matched against.

import { LINES, RULES, type BingoGame, type Move, type Rules } from './game.ts';

export type PlayStyle = {
  reactionMs: () => number; // delay after a call before daubing; values < minReactionMs are raised to it
  miss: () => boolean; // true = this called number is never daubed
  bingoDelayMs: number; // delay between completing a line and pressing BINGO
};

export function playHonestly(game: BingoGame, style: PlayStyle, rules: Rules = RULES): Move[] {
  const { card, calls } = game;

  const daubs: { cell: number; t: number }[] = [];
  calls.forEach((num, i) => {
    const cell = card.indexOf(num);
    if (cell < 0 || style.miss()) return;
    const reaction = Math.max(rules.minReactionMs, Math.round(style.reactionMs()));
    daubs.push({ cell, t: i * rules.callIntervalMs + reaction });
  });
  daubs.sort((a, b) => a.t - b.t);

  const daubed = new Array<boolean>(card.length).fill(false);
  daubed[rules.freeCell] = true;
  const claimed = new Array<boolean>(LINES.length).fill(false);
  const moves: Move[] = [];
  let lastT = -Infinity;
  let pendingClaimAt: number | null = null;

  // Moves must be at least minMoveIntervalMs apart; pushing a move later never makes it illegal
  // (it is still after its call), it only costs speed bonus.
  const at = (t: number) => Math.max(t, lastT + rules.minMoveIntervalMs);
  const canEmit = (t: number) => t <= rules.matchDurationMs && moves.length < rules.maxMoves;

  const claim = (wanted: number) => {
    const t = at(wanted);
    if (!canEmit(t)) return;
    moves.push({ kind: 'bingo', cell: -1, t });
    lastT = t;
    LINES.forEach((line, li) => {
      if (!claimed[li] && line.every((c) => daubed[c])) claimed[li] = true;
    });
  };
  const hasUnclaimedLine = () => LINES.some((line, li) => !claimed[li] && line.every((c) => daubed[c]));

  for (const d of daubs) {
    if (pendingClaimAt !== null && pendingClaimAt <= d.t) {
      claim(pendingClaimAt);
      pendingClaimAt = null;
    }
    const t = at(d.t);
    if (!canEmit(t)) break;
    moves.push({ kind: 'daub', cell: d.cell, t });
    lastT = t;
    daubed[d.cell] = true;
    if (pendingClaimAt === null && hasUnclaimedLine()) pendingClaimAt = t + style.bingoDelayMs;
  }
  if (pendingClaimAt !== null) claim(pendingClaimAt);

  return moves;
}
