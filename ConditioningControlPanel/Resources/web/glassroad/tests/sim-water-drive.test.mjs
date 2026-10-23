/* tests/sim-water-drive.test.mjs - the water driving proof (wave 1, BOTS lane), on the Aperture pattern.
 *
 * Every bot persona at 18 and 30 m/s, with and without a boost, drives the stream bend (sweepA + L2) and the
 * chute + pool on the descent; eight racers from the grid for seeds 1 / 20261023 / 241023. Nobody leaves the road
 * on the stream bend, nobody falls off the chute, everybody exits the pool and reaches the court entry (L4d).
 * Until the TRACK lane regenerates tracks/threshold.json (stream / pool nodes present), the real track is
 * decorated with a stub: query() reports stream + flow on sweepA (u >= 0.15) + L2 and a chute + pool on L4a.
 * The lap-time delta versus the same geometry with every water node read as glass prints as a TAP comment.
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
const REGEN = JSON_TRACK.nodes.some((n) => n.surface === 'stream' || n.surface === 'pool');
const SEEDS = [1, 20261023, 241023];

/** The same geometry with every water node read as glass and no flow: the lap-time baseline. */
function dryTrack() {
  const json = { ...JSON_TRACK, nodes: JSON_TRACK.nodes.map((n) => { const m = { ...n }; if (m.surface === 'stream' || m.surface === 'pool') m.surface = 'glass'; delete m.flow; return m; }) };
  return loadTrack(json);
}

/** The water track: the regenerated one as is, else the stub over the accepted track. */
function waterTrack() {
  if (REGEN) return loadTrack(JSON_TRACK);
  const base = dryTrack();
  // the pieces from the road itself (the generator's node counts drift between runs): sweepA = the first right
  // turn after the start, L2 = the straight after it up to the hairpin, L4a = the straight descent before the court
  const A = { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] }, B = { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] };
  const bendAt = (s) => { base.frameAt(s - 4, A); base.frameAt(s + 4, B); const d = A.t[0] * B.t[0] + A.t[1] * B.t[1] + A.t[2] * B.t[2], side = B.t[0] * A.b[0] + B.t[1] * A.b[1] + B.t[2] * A.b[2]; return Math.atan2(side, Math.abs(d)) * 180 / Math.PI / 8; };
  const downAt = (s) => Math.asin(-base.frameAt(s, A).t[1]) * 180 / Math.PI;
  let s = 0;
  while (bendAt(s) < 0.5) s += 2;
  const sweep0 = s;
  while (bendAt(s) >= 0.5) s += 2;
  const sweep1 = s;
  while (Math.abs(bendAt(s)) < 0.5) s += 2;
  const bend0 = sweep0 + 0.15 * (sweep1 - sweep0), bend1 = s - 4;
  s = base.courts[0].fromS - 2;
  while (!(Math.abs(bendAt(s)) < 0.2 && downAt(s) > 10)) s -= 2;
  const l4e = s;
  while (Math.abs(bendAt(s)) < 0.2 && downAt(s) > 10) s -= 2;
  const l4s = s + 2, chute0 = l4s + 15, chute1 = Math.min(chute0 + 24, l4e), pool1 = chute1 + 36;
  const zones = [
    { s0: bend0, s1: bend1, surface: 'stream', flow: [5, -2.5] },
    { s0: chute0, s1: chute1, surface: 'stream', flow: [8, 0] },
    { s0: chute1, s1: pool1, surface: 'pool', flow: [0, 0] },
  ];
  const t = Object.create(base);
  const query = (px, py, pz, hint = -1, out) => {
    const q = base.query(px, py, pz, hint, out);
    if (!q) return q;
    q.flow = [0, 0];
    for (const z of zones) {
      if (q.s < z.s0 || q.s >= z.s1) continue;
      const e = Math.min(1, (q.s - z.s0) / 8, (z.s1 - q.s) / 8);      // eased in and out like the generator's
      q.surface = z.surface; q.flow[0] = z.flow[0] * e; q.flow[1] = z.flow[1] * e;
    }
    return q;
  };
  query.below = base.query.below;
  t.query = query;
  return t;
}

const WATER = waterTrack(), DRY = dryTrack();
const RUNS = createAi(WATER, makeFeel(), { seed: 1 }).water();
const BEND = RUNS.find((r) => r.kind === 'bend'), CHUTE = RUNS.find((r) => r.kind === 'chute'), POOL = RUNS.find((r) => r.kind === 'pool');
const COURT = WATER.courts[0];
const inRun = (run, s) => s >= run.s0 && s < run.s1;

function place(track, st, s, x, v) {
  st.fill(0);
  const f = track.frameAt(s);
  setPos(st, f.p.map((p, i) => p + f.b[i] * x));
  setQuat(st, qfromBasis([0, 0, 0, 1], f.b, f.n, f.t.map((c) => -c)));
  setVel(st, f.t.map((c) => c * v));
  st[S.engineV] = v; st[S.speedPct] = v / 30; st[S.grounded] = 1;
  st[S.seg] = f.seg; st[S.latchSeg] = f.seg; st[S.s] = s; st[S.lateral] = x; st[S.hopT] = 1e3;
}

/** One bot from 55 m before `from`, boosted 10 m before it, until `until` (s) or 25 s; the road check runs over check(s). */
function drive({ track = WATER, seed = 1, racer = 1, speed = 30, boost = false, from, until, check }) {
  const feel = makeFeel(), names = [], ev = { emit: (n) => names.push(n) };
  const rs = createRacers(track, feel, ev, { count: 8, seed }), r = rs.list[racer];
  place(track, r.st, from - 55, 0, speed);
  const ai = createAi(track, feel, { seed }), a = createActions(), race = { phase: 'racing', timeS: 10 };
  let boostPending = boost, offRoad = null, entered = false, lanes = new Set(), maxSteer = 0;
  for (let i = 0; i < 25 / DT; i++) {
    if (boostPending && r.st[S.s] >= from - 10) { applyBoost(r.st, BOOST.PAD, 0, feel, ev, { amount: 0.24, holdS: 3 }); boostPending = false; }
    const sBefore = r.st[S.s];
    ai.drive(r, { race, racers: [r] }, DT, a);
    step(r.st, a, track, feel, DT, ev);
    updateRespawn(r.st, track, feel, DT, ev);
    race.timeS += DT;
    const s = r.st[S.s], x = r.st[S.lateral];
    if (check(s)) {
      entered = true; lanes.add(x < -1.5 ? 'L' : x > 1.5 ? 'R' : 'C');
      if (inRun(CHUTE, sBefore)) maxSteer = Math.max(maxSteer, Math.abs(a.steer));
      const half = track.frameAt(s).w / 2;
      if (r.st[S.grounded] && Math.abs(x) > half && !offRoad) offRoad = { s, x, half };
    }
    if (names.includes('drop') || r.st[S.respawnPhase] !== RESPAWN.NONE) break;
    if (s >= until && r.st[S.grounded]) break;
  }
  return { st: r.st, names, entered, offRoad, lanes, maxSteer, persona: ai.personaOf(racer) };
}

test('water runs are found on the track: a stream bend, a chute and a pool, in that order', () => {
  assert.ok(BEND && CHUTE && POOL, JSON.stringify(RUNS));
  assert.ok(BEND.side !== 0 && BEND.s1 - BEND.s0 >= 60, 'the stream bend spans the sweep: ' + JSON.stringify(BEND));
  assert.ok(CHUTE.s0 > BEND.s1 && POOL.s0 >= CHUTE.s1 - 2 && COURT.fromS > POOL.s1, 'order bend < chute < pool < court');
  assert.ok(POOL.s1 - POOL.s0 >= 30, 'the pool is long enough to land in: ' + JSON.stringify(POOL));
});

test('every persona takes the stream bend at 18 and 30 m/s, boosted or not, without leaving the road', () => {
  const personas = new Set(), lines = new Set();
  for (const speed of [18, 30]) for (const boost of [false, true]) for (let racer = 1; racer <= 7; racer++) {
    const r = drive({ racer, speed, boost, from: BEND.s0, until: BEND.s1 + 40, check: (s) => inRun(BEND, s) });
    const label = JSON.stringify({ racer, persona: r.persona, speed, boost, s: r.st[S.s], x: r.st[S.lateral], offRoad: r.offRoad, names: r.names.filter((n) => n === 'drop' || n === 'fall') });
    assert.ok(r.entered && r.st[S.s] >= BEND.s1 + 40, 'through the bend: ' + label);
    assert.equal(r.offRoad, null, 'on the road all the way: ' + label);
    assert.ok(!r.names.includes('drop') && r.st[S.respawnPhase] === RESPAWN.NONE, 'no fall, no rescue: ' + label);
    personas.add(r.persona); for (const l of r.lanes) lines.add(l);
  }
  assert.equal(personas.size, PERSONAS.length, 'every persona drove');
  assert.ok(lines.has('L') && lines.has('R'), 'inside and outside lines both taken: ' + [...lines]);
});

test('every persona holds a lane down the chute, lands in the pool and reaches the court entry', () => {
  const lanes = new Set();
  for (const speed of [18, 30]) for (const boost of [false, true]) for (let racer = 1; racer <= 7; racer++) {
    const r = drive({ racer, speed, boost, from: CHUTE.s0, until: COURT.fromS, check: (s) => inRun(CHUTE, s) || inRun(POOL, s) });
    const label = JSON.stringify({ racer, persona: r.persona, speed, boost, s: r.st[S.s], x: r.st[S.lateral], offRoad: r.offRoad, maxSteer: r.maxSteer, names: r.names.filter((n) => n === 'drop' || n === 'fall') });
    assert.ok(r.entered && r.st[S.s] >= COURT.fromS, 'out of the pool and at the court: ' + label);
    assert.equal(r.offRoad, null, 'never off the chute or the pool: ' + label);
    assert.ok(!r.names.includes('drop') && r.st[S.respawnPhase] === RESPAWN.NONE, 'no fall, no rescue: ' + label);
    assert.ok(r.maxSteer <= 0.3 + 1e-6, 'small wheel on the chute: ' + label);
    for (const l of r.lanes) lanes.add(l);
  }
  assert.ok(lanes.size >= 2, 'more than one chute lane across the bots: ' + [...lanes]);
});

/** Eight racers from the grid: first-lap times per racer (or null), falls inside the water, court arrivals. */
function gridRun(track, seed) {
  const race = makeRace({ track, seed });
  const list = race.racers.list, falls = list.map(() => 0), waterFalls = list.map(() => 0), court = new Set(), lap1 = list.map(() => null);
  const water = (s) => inRun(BEND, s) || inRun(CHUTE, s) || inRun(POOL, s);
  for (let i = 0; i < 150 / DT; i++) {
    const before = list.map((r) => r.st[S.s]);
    race.tick();
    let done = true;
    for (const r of list) {
      if (r.falls > falls[r.i] && water(before[r.i])) waterFalls[r.i]++;
      falls[r.i] = r.falls;
      if (r.st[S.s] >= COURT.fromS && r.st[S.s] < COURT.toS) court.add(r.i);
      if (lap1[r.i] === null && r.lapTimes.length) lap1[r.i] = r.lapTimes[0];
      if (lap1[r.i] === null) done = false;
    }
    if (done) break;
  }
  return { waterFalls, court, lap1 };
}

for (const seed of SEEDS) test('eight racers from the grid take the stream bend, the chute and the pool and reach the court, seed ' + seed, () => {
  const w = gridRun(WATER, seed), d = gridRun(DRY, seed);
  assert.deepEqual(w.waterFalls, w.waterFalls.map(() => 0), 'no fall on the stream bend, the chute or the pool');
  assert.equal(w.court.size, 8, 'everybody reached the court entry');
  const deltas = w.lap1.map((t, i) => (t !== null && d.lap1[i] !== null ? +(t - d.lap1[i]).toFixed(2) : null));
  const have = deltas.filter((x) => x !== null);
  const mean = have.length ? (have.reduce((a, b) => a + b, 0) / have.length).toFixed(2) : 'n/a';
  console.log('# water lap 1 delta vs glass, seed ' + seed + ': mean ' + mean + ' s, per racer ' + JSON.stringify(deltas) +
    ' (water ' + JSON.stringify(w.lap1.map((t) => (t === null ? null : +t.toFixed(1)))) + ')');
  assert.ok(w.lap1.every((t) => t !== null), 'everybody finished lap 1 within 150 s: ' + JSON.stringify(w.lap1));
});
