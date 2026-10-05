// Entry point: env config, sweep interval, listen.

import { createApp } from './http.ts';
import { TournamentService } from './tournament.ts';

const env = process.env;
const num = (name: string, fallback: number): number => {
  const raw = env[name];
  if (raw === undefined || raw === '') return fallback;
  const v = Number(raw);
  if (!Number.isFinite(v)) throw new Error(`${name} must be a number, got "${raw}"`);
  return v;
};

const port = num('PORT', 8080);
const service = new TournamentService({
  submitGraceMs: num('SUBMIT_GRACE_MS', 10 * 60_000),
  roomTtlMs: num('ROOM_TTL_MS', 30 * 60_000),
  clockToleranceMs: num('CLOCK_TOLERANCE_MS', 3000),
});
const { server, chaos } = createApp(service, {
  devRoutes: env.DEV_ROUTES !== '0',
  chaos: {
    failRate: num('CHAOS_FAIL_RATE', 0),
    dropRate: num('CHAOS_DROP_RATE', 0),
    delayMs: num('CHAOS_DELAY_MS', 0),
  },
});

const sweeper = setInterval(() => service.sweep(), 5000);
sweeper.unref();

server.listen(port, () => {
  console.log(`tournament-sandbox server on http://localhost:${port}`);
  console.log(`  submit grace ${service.submitGraceMs} ms, room TTL ${service.roomTtlMs} ms, clock tolerance ${service.clockToleranceMs} ms`);
  console.log(`  chaos ${JSON.stringify(chaos)}, dev routes ${env.DEV_ROUTES !== '0' ? 'on' : 'off'}`);
});

const shutdown = () => {
  clearInterval(sweeper);
  server.close(() => process.exit(0));
  server.closeAllConnections();
};
process.on('SIGINT', shutdown);
process.on('SIGTERM', shutdown);
