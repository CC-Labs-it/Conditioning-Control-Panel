// dev/kart-balance.mjs - the same bot on every kart body on every course: flying lap times. MEASURE ONLY.
// Run (from glassroad/): node dev/kart-balance.mjs [--track threshold] [--seeds 1,2,3] [--laps 3] [--clean] [--json out.json]
//
// The walk: the real sim (createRacers + kart.js) with the real AI as the player, alone on the road: no items, no
// rivals, no traps. A lap is clean when it has no fall; laps after the first are the flying laps, and a body's time is
// the mean of its clean flying laps over all seeds. Solo clean pace stays off unless --clean (a room never has it).
import { readFileSync, writeFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { createFeel } from '../core/feel.js';
import { createActions } from '../core/actions.js';
import { createRacers } from '../sim/racers.js';
import { createAi } from '../sim/ai.js';
import { COURSES } from '../track/catalog.js';

const DT = 1 / 60;
const argv = process.argv.slice(2);
const arg = (k, d) => { const i = argv.indexOf('--' + k); return i >= 0 ? argv[i + 1] : d; };
const SEEDS = String(arg('seeds', '7,11,23')).split(',').map(Number), LAPS = Number(arg('laps', 3)), CLEAN = argv.includes('--clean');
const only = arg('track', null), jsonOut = arg('json', null);
const raw = JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));
const feel = createFeel(raw);
const BODIES = Object.keys(raw.karts.bodies);

/** One run: the clean flying lap times and the falls. */
function drive(track, body, seed) {
  const racers = createRacers(track, feel, { emit() {} }, { count: 1, seed }), P = racers.player;
  racers.setBody(0, body);
  const ai = createAi(track, feel, { seed }), acts = createActions();
  const race = { phase: 'racing', timeS: 0, lapStartS: 0, finish() { race.phase = 'finished'; } };
  const ctx = { race, racers: [P], items: null, standings: null };
  const out = { laps: [], falls: 0 };
  let n = 0, dirty = false;
  const emit = P.events.emit;
  P.events.emit = (name, p = {}) => {
    if (name === 'drop') { out.falls++; dirty = true; }
    if (name === 'lap') {
      if (n++ > 0 && !dirty) out.laps.push(p.lapTimeS);
      dirty = false; race.lapStartS = race.timeS;
    }
    emit(name, p);
  };
  const maxTicks = 60 * 60 * 6 * LAPS;
  for (let i = 0; i < maxTicks && n < LAPS; i++) {
    ai.drive(P, ctx, DT, acts);
    racers.step(DT, acts, race, { cleanPace: CLEAN, songDrive: 0 });
    race.timeS += DT;
  }
  return out;
}

const mean = (a) => (a.length ? a.reduce((x, y) => x + y, 0) / a.length : NaN);
const tracks = COURSES.map((c) => c.id).filter((id) => !only || id === only);
const res = {};
for (const id of tracks) {
  const json = JSON.parse(readFileSync(new URL('../tracks/' + id + '.json', import.meta.url), 'utf8'));
  const track = loadTrack(json);
  res[id] = {};
  for (const body of BODIES) {
    const runs = SEEDS.map((seed) => drive(track, body, seed));
    const laps = runs.flatMap((r) => r.laps);
    res[id][body] = { lapS: mean(laps), clean: laps.length, falls: runs.reduce((x, r) => x + r.falls, 0) };
  }
  process.stderr.write(id + ' done\n');
}

const pad = (v, n) => String(v).padEnd(n);
const pct = (id, body) => (res[id][body].lapS / res[id].lovebomb.lapS - 1) * 100;
console.log(pad('body', 13) + tracks.map((id) => pad(id, 17)).join('') + 'mean %');
for (const body of BODIES) {
  const cells = tracks.map((id) => {
    const r = res[id][body];
    return pad(r.lapS.toFixed(2) + ' ' + (pct(id, body) >= 0 ? '+' : '') + pct(id, body).toFixed(2) + '% f' + r.falls, 17);
  });
  const m = mean(tracks.map((id) => pct(id, body)));
  console.log(pad(body, 13) + cells.join('') + (m >= 0 ? '+' : '') + m.toFixed(2));
}
console.log('seconds a flying lap, % against lovebomb, fN = falls in ' + SEEDS.length + ' runs of ' + LAPS + ' laps');
if (jsonOut) writeFileSync(jsonOut, JSON.stringify(res, null, 1));
