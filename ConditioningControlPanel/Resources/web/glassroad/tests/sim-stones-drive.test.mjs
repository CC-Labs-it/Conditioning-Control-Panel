/* Driving proof for the real Threshold stepping minigame: every persona and full grids
 * cross doubled islands with deliberate jumps, including boosted approaches and contact.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { createAi, PERSONAS } from '../sim/ai.js';
import { createRacers } from '../sim/racers.js';
import { step } from '../sim/kart.js';
import { updateRespawn } from '../sim/respawn.js';
import { S, RESPAWN, BOOST, setPos, setQuat, setVel } from '../sim/state.js';
import { applyBoost } from '../sim/boost.js';
import { qfromBasis } from '../core/math.js';
import { createActions } from '../core/actions.js';
import { makeFeel, DT } from './sim-fake.mjs';
import { makeRace } from './sim-racers-harness.mjs';

const JSON_TRACK = JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8'));
const TRACK = loadTrack(JSON_TRACK);
const SEEDS = [1, 20261023, 241023];

const FIELD = TRACK.fields[0];
const inField = (s) => s >= FIELD.fromS && s < FIELD.toS;
const laneErr = (x) => Math.min(...FIELD.lanes.map((lx) => Math.abs(lx - x)));

function place(track, st, s, x, v) {
  st.fill(0);
  const f = track.frameAt(s);
  setPos(st, f.p.map((p, i) => p + f.b[i] * x));
  setQuat(st, qfromBasis([0, 0, 0, 1], f.b, f.n, f.t.map((c) => -c)));
  setVel(st, f.t.map((c) => c * v));
  st[S.engineV] = v; st[S.speedPct] = v / 30; st[S.grounded] = 1;
  st[S.seg] = f.seg; st[S.latchSeg] = f.seg; st[S.s] = s; st[S.lateral] = x; st[S.hopT] = 1e3;
}

/** One bot from 70 m before the field, boosted 10 m before it, until 20 m past it or 20 s. */
function drive({ seed = 1, racer = 1, speed = 30, boost = false }) {
  const feel = makeFeel(), names = [], ev = { emit: (n, p) => names.push({ n, p }) };
  const rs = createRacers(TRACK, feel, ev, { count: 8, seed }), r = rs.list[racer];
  place(TRACK, r.st, FIELD.fromS - 70, 0, speed);
  const ai = createAi(TRACK, feel, { seed }), a = createActions(), race = { phase: 'racing', timeS: 10 };
  let boostPending = boost, entered = false, worstLane = 0, minSpeed = Infinity, drifts = 0, lane = null;
  const stones = new Set();
  for (let i = 0; i < 20 / DT; i++) {
    if (boostPending && r.st[S.s] >= FIELD.fromS - 10) { applyBoost(r.st, BOOST.PAD, 0, feel, ev, { amount: 0.24, holdS: 3 }); boostPending = false; }
    const n0 = names.length;
    const stoneId = TRACK.query(r.st[S.posX], r.st[S.posY], r.st[S.posZ], r.st[S.seg] | 0)?.stone?.id;
    ai.drive(r, { race, racers: [r] }, DT, a);
    step(r.st, a, TRACK, feel, DT, ev);
    updateRespawn(r.st, TRACK, feel, DT, ev);
    race.timeS += DT;
    const st = r.st, s = st[S.s], x = st[S.lateral];
    if (inField(s) && st[S.respawnPhase] === RESPAWN.NONE) {
      entered = true;
      if (lane === null) lane = FIELD.lanes.reduce((b, lx) => (Math.abs(lx - x) < Math.abs(b - x) ? lx : b));
      worstLane = Math.max(worstLane, laneErr(x));
      minSpeed = Math.min(minSpeed, Math.hypot(st[S.velX], st[S.velY], st[S.velZ]));
      if (stoneId) stones.add(stoneId);
      for (let k = n0; k < names.length; k++) if (names[k].n === 'drift-start') drifts++;
    }
    const bad = names.slice(n0).find((e) => e.n === 'drop' || e.n === 'fall');
    if (bad || st[S.respawnPhase] !== RESPAWN.NONE) break;
    if (s >= FIELD.toS + 20 && st[S.grounded]) break;
  }
  const bad = names.filter((e) => e.n === 'drop' || e.n === 'fall').map((e) => e.n + (e.p && e.p.cause ? ':' + e.p.cause : ''));
  return { st: r.st, bad, entered, worstLane, minSpeed, drifts, lane, stones: stones.size, persona: ai.personaOf(racer) };
}

test('the field is on the bridge, after its pad, with a stone in some lane at every s', () => {
  const pad = TRACK.boosts.find((b) => b.x === -3);
  assert.ok(FIELD.fromS - (pad.s + pad.len) >= 35, 'the enlarged approach leaves room after the pad: ' + JSON.stringify({ pad, fromS: FIELD.fromS }));
  assert.ok(FIELD.toS - FIELD.fromS >= 55 && FIELD.toS - FIELD.fromS <= 60, 'shortened stepping district');
  // the last stones end 2 m before the deck resumes (the brief), so the check stops there
  for (let s = FIELD.fromS; s < FIELD.toS - 2.5; s += 0.5) {
    assert.ok(FIELD.stones.some((st) => s >= st.s && s < st.s + st.len), 'a stone under s ' + s.toFixed(1));
  }
  const plan = createAi(TRACK, makeFeel(), { seed: 1 }).fields();
  assert.equal(plan.length, 1);
  assert.deepEqual(plan[0].lanes, [-14, 0, 14]);
  assert.equal(plan[0].centre, 0);
  assert.ok(plan[0].safe.length >= 1, 'a safe lane');
  assert.equal(FIELD.platforming, true, 'the real course requires deliberate jumps');
});

test('every persona commits to a lane, keeps speed and crosses the stones at 18 and 30 m/s', () => {
  const personas = new Set(), lanes = new Set();
  for (const speed of [18, 30]) for (let racer = 1; racer <= 7; racer++) {
    const r = drive({ racer, speed });
    const label = JSON.stringify({ racer, persona: r.persona, speed, s: +r.st[S.s].toFixed(1), x: +r.st[S.lateral].toFixed(2), lane: r.lane, worstLane: +r.worstLane.toFixed(2), minSpeed: +r.minSpeed.toFixed(1), stones: r.stones, bad: r.bad });
    assert.ok(r.entered && r.st[S.s] >= FIELD.toS + 20, 'across the field: ' + label);
    assert.deepEqual(r.bad, [], 'no drop, no fall: ' + label);
    assert.equal(r.st[S.respawnPhase], RESPAWN.NONE, 'no rescue: ' + label);
    assert.ok(r.worstLane <= 1.5, 'held the lane: ' + label);
    assert.ok(r.minSpeed >= 12, 'kept speed: ' + label);
    assert.equal(r.drifts, 0, 'no drift in the field: ' + label);
    assert.ok(r.stones >= 2, 'drove over the stones, not the void: ' + label);
    personas.add(r.persona); lanes.add(r.lane);
  }
  assert.equal(personas.size, PERSONAS.length, 'every persona drove');
  // lane variety follows the safe lanes of the layout (a lane with an entry void the deck cannot hand over is never picked)
  console.log('# stones: lanes taken by the bots ' + JSON.stringify([...lanes]));
});

// A fast entry still has to land each jump on the real islands.
test('every persona holds its lane and completes the stepping minigame with a boosted entry', () => {
  const outcomes = [];
  for (const speed of [18, 30]) for (let racer = 1; racer <= 7; racer++) {
    const r = drive({ racer, speed, boost: true });
    const label = JSON.stringify({ racer, persona: r.persona, speed, s: +r.st[S.s].toFixed(1), lane: r.lane, worstLane: +r.worstLane.toFixed(2), minSpeed: +r.minSpeed.toFixed(1), stones: r.stones, bad: r.bad });
    assert.ok(r.entered && r.st[S.s] >= FIELD.toS + 20, 'completed boosted field: ' + label);
    assert.deepEqual(r.bad, [], 'no boosted drop or fall: ' + label);
    assert.ok(r.worstLane <= 1.5, 'held the lane: ' + label);
    assert.ok(r.minSpeed >= 12, 'kept speed: ' + label);
    assert.equal(r.drifts, 0, 'no drift in the field: ' + label);
    outcomes.push((r.bad.length ? r.bad.join('+') : 'clean') + '@' + r.lane);
  }
  console.log('# stones: boosted entry outcomes ' + JSON.stringify(outcomes));
});

/** Eight racers from the grid, two laps: void drops / falls inside the field, lane and speed discipline. */
function gridRun(seed) {
  const race = makeRace({ track: TRACK, seed }), list = race.racers.list;
  const fieldBad = list.map(() => []), worstLane = list.map(() => 0), minSpeed = list.map(() => Infinity), entered = new Set();
  let logged = 0;
  for (let i = 0; i < 500 / DT; i++) {
    const before = list.map((r) => r.st[S.s]);
    race.tick();
    for (let k = logged; k < race.log.length; k++) {
      const e = race.log[k], name = e.name === 'racer-event' ? e.payload.name : e.name, p = e.name === 'racer-event' ? e.payload.p : e.payload;
      const who = e.name === 'racer-event' ? e.payload.racer : 0;
      if (name === 'drop' && inField(before[who])) fieldBad[who].push('drop:' + (p && p.cause) + '@' + before[who].toFixed(1));
      if (name === 'fall' && p && inField(p.s)) fieldBad[who].push('fall@' + p.s.toFixed(1));
    }
    logged = race.log.length;
    let done = true;
    for (const r of list) {
      const st = r.st, s = st[S.s];
      if (inField(s) && st[S.respawnPhase] === RESPAWN.NONE) {
        entered.add(r.i);
        worstLane[r.i] = Math.max(worstLane[r.i], laneErr(st[S.lateral]));
        minSpeed[r.i] = Math.min(minSpeed[r.i], Math.hypot(st[S.velX], st[S.velY], st[S.velZ]));
      }
      if (r.lapTimes.length < 2) done = false;
    }
    if (done) break;
  }
  const laps = list.map((r) => r.lapTimes.length);
  return { fieldBad, worstLane, minSpeed, entered, laps };
}

for (const seed of SEEDS) test('eight racers from the grid cross the stones twice without a void drop or a fall, on their lanes, at speed, seed ' + seed, () => {
  const g = gridRun(seed);
  const label = JSON.stringify({ laps: g.laps, worstLane: g.worstLane.map((v) => +v.toFixed(2)), minSpeed: g.minSpeed.map((v) => +v.toFixed(1)), bad: g.fieldBad });
  assert.equal(g.entered.size, 8, 'everybody reached the field: ' + label);
  assert.ok(g.laps.every((n) => n >= 2), 'two laps each within 500 s: ' + label);
  assert.deepEqual(g.fieldBad, g.fieldBad.map(() => []), 'no void drop, no fall inside the field: ' + label);
  for (let i = 0; i < 8; i++) {
    assert.ok(g.worstLane[i] <= 1.5, 'racer ' + i + ' within 1.5 m of a lane centre: ' + label);
    assert.ok(g.minSpeed[i] >= 12, 'racer ' + i + ' never under 12 m/s inside: ' + label);
  }
});
