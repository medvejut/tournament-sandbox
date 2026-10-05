// TournamentService: all domain logic (wallet, rooms, matches, resolve, sweep, live-ops).
// In-memory on purpose; see README "what I'd do in production". Time comes from an injected
// `now()` so tests can drive deadlines and TTLs without sleeping.

import { createHash, randomBytes, randomInt, randomUUID } from 'node:crypto';
import { RULES, replay, type Move } from './game.ts';

export class ApiError extends Error {
  readonly status: number;
  readonly code: string;

  constructor(status: number, code: string, message?: string) {
    super(message ?? code);
    this.status = status;
    this.code = code;
  }
}

export type Tournament = { id: string; name: string; entryFee: number };

export const TOURNAMENTS: readonly Tournament[] = [
  { id: 'bronze', name: 'Bronze', entryFee: 10 },
  { id: 'gold', name: 'Gold', entryFee: 100 },
];

export const STARTING_BALANCE = 1000;
export const RAKE_PERCENT = 10;
export const MAX_CLIENT_SCORE = 1_000_000;

export type Player = { id: string; deviceId: string; token: string; balance: number };

type RoomStatus = 'open' | 'full' | 'resolved' | 'refunded';

type Room = {
  id: string;
  tournamentId: string;
  seed: number;
  createdAt: number;
  entryIds: string[];
  status: RoomStatus;
};

type Entry = {
  id: string;
  roomId: string;
  playerId: string;
  createdAt: number;
  deadline: number;
  multiplier: number;
  status: 'playing' | 'submitted';
  score: number;
  movesHash: string | null;
  clientScore: number | null;
  flags: string[];
  rejectedReason: string | null;
  forfeited: boolean;
  result: 'win' | 'loss' | 'tie' | null;
  payout: number;
};

export type LiveOpsEvent = {
  id: string;
  type: 'double_rewards';
  startsAt: number;
  endsAt: number;
  multiplier: number;
};

export type MatchStatus = 'playing' | 'waiting-opponent' | 'completed' | 'refunded';

export type MatchView = {
  matchId: string;
  tournamentId: string;
  seed: number;
  durationMs: number;
  startedAt: number;
  deadline: number;
  multiplier: number;
  status: MatchStatus;
  score: number | null;
  opponentScore: number | null;
  result: 'win' | 'loss' | 'tie' | null;
  payout: number;
  balance: number;
  rejectedReason: string | null;
  flags: string[];
};

export type SubmitBody = { moves: Move[]; clientScore: number };

export type ServiceOptions = {
  now?: () => number;
  newSeed?: () => number;
  submitGraceMs?: number;
  roomTtlMs?: number;
  clockToleranceMs?: number;
};

export class TournamentService {
  readonly now: () => number;
  readonly submitGraceMs: number;
  readonly roomTtlMs: number;
  readonly clockToleranceMs: number;
  readonly #newSeed: () => number;

  readonly #players = new Map<string, Player>();
  readonly #playerByDevice = new Map<string, string>();
  readonly #playerByToken = new Map<string, string>();
  readonly #rooms = new Map<string, Room>();
  readonly #entries = new Map<string, Entry>();
  readonly #entryByIdempotencyKey = new Map<string, { entryId: string; tournamentId: string }>();
  readonly #events: LiveOpsEvent[] = [];

  constructor(opts: ServiceOptions = {}) {
    this.now = opts.now ?? Date.now;
    this.#newSeed = opts.newSeed ?? (() => randomInt(1, 2 ** 32 - 1));
    this.submitGraceMs = opts.submitGraceMs ?? 10 * 60_000;
    this.roomTtlMs = opts.roomTtlMs ?? 30 * 60_000;
    this.clockToleranceMs = opts.clockToleranceMs ?? 3000;
  }

  // ---- players ----

  login(deviceId: unknown): { playerId: string; token: string; balance: number; serverTime: number } {
    if (typeof deviceId !== 'string' || deviceId.length === 0 || deviceId.length > 128) {
      throw new ApiError(400, 'bad-request', 'deviceId must be a non-empty string (max 128 chars)');
    }
    this.sweep();
    let player = this.#playerFromDevice(deviceId);
    if (!player) {
      player = { id: `p_${randomUUID().slice(0, 8)}`, deviceId, token: '', balance: STARTING_BALANCE };
      this.#players.set(player.id, player);
      this.#playerByDevice.set(deviceId, player.id);
    }
    // Each login rotates the token; the old one stops working.
    if (player.token) this.#playerByToken.delete(player.token);
    player.token = randomBytes(24).toString('base64url');
    this.#playerByToken.set(player.token, player.id);
    return { playerId: player.id, token: player.token, balance: player.balance, serverTime: this.now() };
  }

  authenticate(token: string | null): Player {
    const id = token ? this.#playerByToken.get(token) : undefined;
    const player = id ? this.#players.get(id) : undefined;
    if (!player) throw new ApiError(401, 'unauthorized', 'missing or invalid bearer token');
    return player;
  }

  wallet(playerId: string): { balance: number } {
    this.sweep();
    return { balance: this.#player(playerId).balance };
  }

  leaderboard(): { playerId: string; balance: number }[] {
    this.sweep();
    return [...this.#players.values()]
      .sort((a, b) => b.balance - a.balance || a.id.localeCompare(b.id))
      .slice(0, 10)
      .map((p) => ({ playerId: p.id, balance: p.balance }));
  }

  tournaments(): Tournament[] {
    return TOURNAMENTS.map((t) => ({ ...t }));
  }

  // ---- matches ----

  // Idempotent per (player, idempotency key): a retry returns the same match and never charges twice.
  enter(playerId: string, tournamentId: unknown, idempotencyKey: string | null): MatchView {
    if (!idempotencyKey || idempotencyKey.length > 128) {
      throw new ApiError(400, 'idempotency-key-required', 'Idempotency-Key header is required (max 128 chars)');
    }
    this.sweep();
    const player = this.#player(playerId);

    const keyId = `${playerId}:${idempotencyKey}`;
    const existing = this.#entryByIdempotencyKey.get(keyId);
    if (existing) {
      if (existing.tournamentId !== tournamentId) {
        throw new ApiError(409, 'idempotency-key-reused', 'this Idempotency-Key was used for another tournament');
      }
      return this.#view(this.#entry(existing.entryId));
    }

    const tournament = TOURNAMENTS.find((t) => t.id === tournamentId);
    if (!tournament) throw new ApiError(404, 'tournament-not-found', `unknown tournament ${String(tournamentId)}`);
    if (player.balance < tournament.entryFee) throw new ApiError(402, 'insufficient-funds');

    const now = this.now();
    const room = this.#findOpenRoom(tournament.id, playerId) ?? this.#createRoom(tournament.id, now);

    player.balance -= tournament.entryFee;
    const entry: Entry = {
      id: randomUUID(),
      roomId: room.id,
      playerId,
      createdAt: now,
      deadline: now + RULES.matchDurationMs + this.submitGraceMs,
      multiplier: this.currentMultiplier(now),
      status: 'playing',
      score: 0,
      movesHash: null,
      clientScore: null,
      flags: [],
      rejectedReason: null,
      forfeited: false,
      result: null,
      payout: 0,
    };
    this.#entries.set(entry.id, entry);
    room.entryIds.push(entry.id);
    if (room.entryIds.length === 2) room.status = 'full';
    this.#entryByIdempotencyKey.set(keyId, { entryId: entry.id, tournamentId: tournament.id });
    return this.#view(entry);
  }

  getMatch(playerId: string, matchId: string): MatchView {
    this.sweep();
    return this.#view(this.#ownedEntry(playerId, matchId));
  }

  // Idempotent by (matchId, hash of moves): the same log returns the stored result unchanged,
  // a different log is a 409. The server's replay is the only score that counts.
  submit(playerId: string, matchId: string, body: unknown): MatchView {
    const { moves, clientScore } = validateSubmitBody(body); // 400 before anything is consumed
    this.sweep();
    const entry = this.#ownedEntry(playerId, matchId);
    const room = this.#room(entry.roomId);
    const hash = hashMoves(moves);

    if (entry.forfeited) throw new ApiError(410, 'match-expired', 'the submit deadline has passed');
    if (entry.status === 'submitted') {
      if (entry.movesHash === hash) return this.#view(entry);
      throw new ApiError(409, 'already-submitted', 'a different move log was already submitted for this match');
    }
    if (room.status === 'refunded') throw new ApiError(410, 'match-expired', 'the room was refunded');

    entry.status = 'submitted';
    entry.movesHash = hash;
    entry.clientScore = clientScore;

    // A client can't have played longer than the server has known about the match.
    const lastT = moves.length > 0 ? moves[moves.length - 1].t : 0;
    const elapsed = this.now() - entry.createdAt;
    if (lastT > elapsed + this.clockToleranceMs) {
      entry.score = 0;
      entry.rejectedReason = 'moves-after-server-time';
    } else {
      const result = replay(room.seed, moves);
      if (result.ok) {
        entry.score = result.score;
      } else {
        entry.score = 0;
        entry.rejectedReason = result.reason;
      }
    }
    if (clientScore !== entry.score) entry.flags.push('client-score-mismatch');

    this.#tryResolve(room);
    return this.#view(entry);
  }

  // ---- live-ops ----

  currentMultiplier(now: number = this.now()): number {
    let m = 1;
    for (const e of this.#events) if (e.startsAt <= now && now < e.endsAt) m = Math.max(m, e.multiplier);
    return m;
  }

  liveOps(): { serverTime: number; events: LiveOpsEvent[] } {
    const now = this.now();
    return { serverTime: now, events: this.#events.filter((e) => e.endsAt > now).map((e) => ({ ...e })) };
  }

  createLiveOpsEvent(body: unknown): LiveOpsEvent {
    const b = asObject(body);
    const startsInSec = b.startsInSec ?? 0;
    const durationSec = b.durationSec;
    const multiplier = b.multiplier ?? 2;
    if (typeof startsInSec !== 'number' || !Number.isFinite(startsInSec) || startsInSec < 0) {
      throw new ApiError(400, 'bad-request', 'startsInSec must be a number >= 0');
    }
    if (typeof durationSec !== 'number' || !Number.isFinite(durationSec) || durationSec <= 0) {
      throw new ApiError(400, 'bad-request', 'durationSec must be a number > 0');
    }
    if (typeof multiplier !== 'number' || !Number.isFinite(multiplier) || multiplier < 1 || multiplier > 10) {
      throw new ApiError(400, 'bad-request', 'multiplier must be a number in 1..10');
    }
    const startsAt = this.now() + Math.round(startsInSec * 1000);
    const event: LiveOpsEvent = {
      id: `ev_${randomUUID().slice(0, 8)}`,
      type: 'double_rewards',
      startsAt,
      endsAt: startsAt + Math.round(durationSec * 1000),
      multiplier,
    };
    this.#events.push(event);
    return { ...event };
  }

  // ---- housekeeping ----

  // Called lazily at the start of every service call and on an interval by server.ts.
  sweep(): void {
    const now = this.now();
    for (const entry of this.#entries.values()) {
      if (entry.status === 'playing' && now > entry.deadline) {
        entry.status = 'submitted';
        entry.score = 0;
        entry.forfeited = true;
        entry.flags.push('forfeit-timeout');
        this.#tryResolve(this.#room(entry.roomId));
      }
    }
    for (const room of this.#rooms.values()) {
      if (room.status === 'open' && now - room.createdAt > this.roomTtlMs) {
        room.status = 'refunded';
        for (const id of room.entryIds) {
          const entry = this.#entry(id);
          const fee = this.#fee(room.tournamentId);
          entry.payout = fee;
          this.#player(entry.playerId).balance += fee;
        }
      }
    }
  }

  // ---- internals ----

  #tryResolve(room: Room): void {
    if (room.status !== 'full') return;
    const [a, b] = room.entryIds.map((id) => this.#entry(id));
    if (a.status !== 'submitted' || b.status !== 'submitted') return;

    room.status = 'resolved';
    const fee = this.#fee(room.tournamentId);
    if (a.score === b.score) {
      for (const e of [a, b]) {
        e.result = 'tie';
        e.payout = fee;
        this.#player(e.playerId).balance += fee;
      }
      return;
    }
    const [winner, loser] = a.score > b.score ? [a, b] : [b, a];
    winner.result = 'win';
    loser.result = 'loss';
    // Integer maths so 2 * fee * 0.9 never turns into 17.999999.
    winner.payout = Math.floor((2 * fee * (100 - RAKE_PERCENT) * winner.multiplier) / 100);
    this.#player(winner.playerId).balance += winner.payout;
  }

  #findOpenRoom(tournamentId: string, playerId: string): Room | undefined {
    for (const room of this.#rooms.values()) {
      if (room.status !== 'open' || room.tournamentId !== tournamentId) continue;
      if (this.#entry(room.entryIds[0]).playerId !== playerId) return room;
    }
    return undefined;
  }

  #createRoom(tournamentId: string, now: number): Room {
    const room: Room = { id: randomUUID(), tournamentId, seed: this.#newSeed() >>> 0, createdAt: now, entryIds: [], status: 'open' };
    this.#rooms.set(room.id, room);
    return room;
  }

  #view(entry: Entry): MatchView {
    const room = this.#room(entry.roomId);
    const player = this.#player(entry.playerId);
    let status: MatchStatus;
    if (room.status === 'refunded') status = 'refunded';
    else if (room.status === 'resolved') status = 'completed';
    else if (entry.status === 'playing') status = 'playing';
    else status = 'waiting-opponent';

    const opponentId = room.entryIds.find((id) => id !== entry.id);
    return {
      matchId: entry.id,
      tournamentId: room.tournamentId,
      seed: room.seed,
      durationMs: RULES.matchDurationMs,
      startedAt: entry.createdAt,
      deadline: entry.deadline,
      multiplier: entry.multiplier,
      status,
      score: entry.status === 'submitted' ? entry.score : null,
      opponentScore: status === 'completed' && opponentId ? this.#entry(opponentId).score : null,
      result: status === 'completed' ? entry.result : null,
      payout: entry.payout,
      balance: player.balance,
      rejectedReason: entry.rejectedReason,
      flags: [...entry.flags],
    };
  }

  #playerFromDevice(deviceId: string): Player | undefined {
    const id = this.#playerByDevice.get(deviceId);
    return id ? this.#players.get(id) : undefined;
  }

  #player(id: string): Player {
    const p = this.#players.get(id);
    if (!p) throw new ApiError(401, 'unauthorized', 'unknown player');
    return p;
  }

  #room(id: string): Room {
    const r = this.#rooms.get(id);
    if (!r) throw new Error(`invariant: room ${id} missing`);
    return r;
  }

  #entry(id: string): Entry {
    const e = this.#entries.get(id);
    if (!e) throw new Error(`invariant: entry ${id} missing`);
    return e;
  }

  #ownedEntry(playerId: string, matchId: string): Entry {
    const e = this.#entries.get(matchId);
    // Someone else's match is reported as missing, not forbidden, so ids can't be probed.
    if (!e || e.playerId !== playerId) throw new ApiError(404, 'match-not-found');
    return e;
  }

  #fee(tournamentId: string): number {
    const t = TOURNAMENTS.find((x) => x.id === tournamentId);
    if (!t) throw new Error(`invariant: tournament ${tournamentId} missing`);
    return t.entryFee;
  }
}

// Shape validation only. Whether the moves are *legal* is replay's job, and an illegal log is
// still consumed (score 0 + rejectedReason) so retries stay identical.
export function validateSubmitBody(body: unknown): SubmitBody {
  const b = asObject(body);
  const { moves, clientScore } = b;
  if (!Array.isArray(moves)) throw new ApiError(400, 'bad-request', 'moves must be an array');
  if (moves.length > RULES.maxMoves) throw new ApiError(400, 'bad-request', `at most ${RULES.maxMoves} moves`);
  const clean: Move[] = moves.map((m, i) => {
    if (typeof m !== 'object' || m === null || Array.isArray(m)) {
      throw new ApiError(400, 'bad-request', `moves[${i}] must be an object`);
    }
    const { kind, cell, t } = m as Record<string, unknown>;
    if (kind !== 'daub' && kind !== 'bingo') throw new ApiError(400, 'bad-request', `moves[${i}].kind must be daub|bingo`);
    if (typeof cell !== 'number' || !Number.isFinite(cell)) throw new ApiError(400, 'bad-request', `moves[${i}].cell must be a number`);
    if (typeof t !== 'number' || !Number.isFinite(t)) throw new ApiError(400, 'bad-request', `moves[${i}].t must be a number`);
    return { kind, cell, t };
  });
  if (!Number.isInteger(clientScore) || (clientScore as number) < 0 || (clientScore as number) > MAX_CLIENT_SCORE) {
    throw new ApiError(400, 'bad-request', 'clientScore must be a non-negative integer');
  }
  return { moves: clean, clientScore: clientScore as number };
}

export function hashMoves(moves: Move[]): string {
  const canonical = JSON.stringify(moves.map((m) => [m.kind, m.cell, m.t]));
  return createHash('sha256').update(canonical).digest('hex');
}

function asObject(body: unknown): Record<string, unknown> {
  if (typeof body !== 'object' || body === null || Array.isArray(body)) {
    throw new ApiError(400, 'bad-request', 'body must be a JSON object');
  }
  return body as Record<string, unknown>;
}
