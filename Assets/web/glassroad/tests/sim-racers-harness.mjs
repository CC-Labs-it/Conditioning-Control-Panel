/* tests/sim-racers-harness.mjs - a headless 8-kart race on the real Threshold (RACE lane helper, not a test).
 * Runs the B1 frame loop's sim part (CONTRACT-B1.md section 5) at a fixed 60 Hz with a fixed seed. The
 * player is driven by the same AI (the ?bot=1 autopilot). Items join when `items` is passed.
 */
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { createRace } from '../sim/race.js';
import { createRacers } from '../sim/racers.js';
import { createAi } from '../sim/ai.js';
import { createContact } from '../sim/contact.js';
import { createStandings } from '../sim/standings.js';
import { createActions } from '../core/actions.js';
import { makeFeel, DT } from './sim-fake.mjs';

export { DT };
const JSON_TRACK = JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8'));
export const threshold = () => loadTrack(JSON_TRACK);

export function makeRace({ seed = 1, count = 8, feelOver = {}, track = threshold(), items = null } = {}) {
  const feel = makeFeel(feelOver);
  const log = [];
  const queue = { emit(name, payload = {}) { log.push({ name, payload, t: race.timeS }); } };
  const race = createRace(track, feel, queue);
  const racers = createRacers(track, feel, queue, { count, seed });
  const ai = createAi(track, feel, { seed });
  const contact = createContact(track, feel, queue);
  const standings = createStandings(track, feel, queue);
  const it = items ? items(track, feel, queue, { seed }) : null;
  const a = Object.assign(createActions(), { item: false, back: false });
  const ctx = { ai, items: it, standings };
  race.start();
  let ticks = 0;
  function tick() {
    ai.drive(racers.player, { racers: racers.list, items: it, standings, race }, DT, a);
    race.update(DT, a, racers.player.st);
    racers.step(DT, a, race, ctx);
    contact.update(DT, racers.list, race);
    standings.update(racers.list, race, DT);
    if (it) it.update(DT, racers.list, standings, race);
    ticks++;
  }
  /** Run until done() or maxS of sim time; returns the seconds run. */
  function run(maxS, done = () => false) {
    const n = Math.round(maxS / DT);
    for (let i = 0; i < n; i++) { tick(); if (done()) return (i + 1) * DT; }
    return n * DT;
  }
  return { feel, track, race, racers, ai, contact, standings, items: it, log, tick, run, get ticks() { return ticks; },
    events: (name) => log.filter((e) => e.name === name),
    botEvents: (name) => log.filter((e) => e.name === 'racer-event' && e.payload.name === name) };
}
