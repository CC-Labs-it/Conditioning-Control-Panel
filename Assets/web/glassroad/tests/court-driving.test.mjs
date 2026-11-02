import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { holeAt } from '../track/courts.js';
import { createCourtPaths, courtRescueNode } from '../sim/court-paths.js';
import { createAi } from '../sim/ai.js';
import { createAssist } from '../sim/assist.js';
import { createRacers } from '../sim/racers.js';
import { step } from '../sim/kart.js';
import { anchorNode, updateRespawn } from '../sim/respawn.js';
import { S, RESPAWN, BOOST, createState, setPos, setQuat, setVel } from '../sim/state.js';
import { applyBoost } from '../sim/boost.js';
import { qfromBasis } from '../core/math.js';
import { createActions } from '../core/actions.js';
import { makeFeel, DT } from './sim-fake.mjs';
import { makeRace } from './sim-racers-harness.mjs';

const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8')));
const court = track.courts[0], jump = track.jumps.find(j => j.s > court.toS);

function place(st, s, x, v) {
  st.fill(0);
  const f = track.frameAt(s);
  setPos(st, f.p.map((p, i) => p + f.b[i] * x));
  setQuat(st, qfromBasis([0, 0, 0, 1], f.b, f.n, f.t.map(v => -v)));
  setVel(st, f.t.map(t => t * v));
  st[S.engineV] = v; st[S.speedPct] = v / 30; st[S.grounded] = 1;
  st[S.seg] = f.seg; st[S.latchSeg] = f.seg; st[S.s] = s; st[S.lateral] = x; st[S.hopT] = 1e3;
}

function drive({ seed = 1, racer = 1, speed = 30, lateral = 0, start = court.fromS - 55, mode = null, boost = false, rescue = false } = {}) {
  const feel = makeFeel(), names = [], ev = { emit: n => names.push(n) };
  const rs = createRacers(track, feel, ev, { count: 8, seed }), r = rs.list[racer];
  place(r.st, start, lateral, speed);
  if (rescue) { r.st[S.respawnPhase] = RESPAWN.PULL; r.st[S.posY] -= 5; }
  const ai = createAi(track, feel, { seed }), assist = mode ? createAssist(track, feel, { mode }) : null;
  const a = createActions(), race = { phase: 'racing', timeS: 10 };
  let boostPending = boost;
  let entered = false, jumped = false, crossedHole = false;
  const lanes = new Set();
  for (let i = 0; i < 20 / DT; i++) {
    if (boostPending && r.st[S.s] >= court.fromS - 10) {
      applyBoost(r.st, BOOST.PAD, 0, feel, ev, { amount: 0.24, holdS: 3 }); boostPending = false;
    }
    if (assist) Object.assign(a, { steer: 0, throttle: 1, brake: 0, hop: false, hopPressed: false });
    else ai.drive(r, { race, racers: [r] }, DT, a);
    step(r.st, assist ? assist.apply(a, r.st, DT, 'racing') : a, track, feel, DT, ev);
    updateRespawn(r.st, track, feel, DT, ev);
    race.timeS += DT;
    const s = r.st[S.s], x = r.st[S.lateral];
    if (s > court.holes[0].s && s < court.holes[1].s + court.holes[1].len) {
      entered = true; lanes.add(x < -8 ? 'left' : x > 8 ? 'right' : 'inner');
      if (r.st[S.grounded] && holeAt(track.courts, s, x, 0.54)) crossedHole ||= { s, x };
    }
    if (s >= jump.s && r.st[S.airborne]) jumped = true;
    if (names.includes('drop')) break;
    if (s > jump.landS + 8 && r.st[S.grounded]) break;
  }
  return { st: r.st, names, entered, jumped, crossedHole, lanes };
}

test('court routes are seeded, varied and keep the entire kart footprint off both holes', () => {
  const paths = createCourtPaths(track), p = paths.at(court.fromS), chosen = new Set();
  for (let i = 1; i <= 7; i++) {
    const route = paths.choose(p, 3, i, 0);
    assert.deepEqual(route, paths.choose(p, 3, i, 0)); chosen.add(route.band.x);
    for (let s = p.first - 1; s <= p.last + 1; s += 0.25) {
      assert.equal(holeAt(track.courts, s, paths.target(route, s, 30), 0.54), null);
    }
  }
  assert.equal(chosen.size, 3);
});

test('real bots traverse all court lines and clear the exit jump at ordinary and boosted entry speeds', () => {
  const lanes = new Set();
  for (const speed of [18, 30]) for (const boost of [false, true]) for (let racer = 1; racer <= 7; racer++) {
    const r = drive({ racer, speed, boost });
    const label = JSON.stringify({ racer, speed, boost, s: r.st[S.s], x: r.st[S.lateral], crossedHole: r.crossedHole, names: r.names });
    assert.ok(r.entered && r.jumped && r.st[S.s] > jump.landS + 8, label);
    assert.ok(!r.crossedHole && !r.names.includes('drop'), label);
    for (const lane of r.lanes) lanes.add(lane);
  }
  assert.equal(lanes.size, 3);
});

test('hands-off steering preserves an outer court line instead of pulling across a hole', () => {
  const bands = createCourtPaths(track).at(court.fromS).bands;
  for (const mode of ['light', 'strong']) for (const { x: lateral } of bands) {
    const r = drive({ mode, lateral, start: court.fromS + 12, speed: 24 });
    assert.ok(!r.crossedHole && !r.names.includes('drop'), JSON.stringify({ mode, lateral, s: r.st[S.s], names: r.names }));
    assert.ok(r.st[S.s] > jump.landS + 8, 'exit jump after assist ' + mode + ' ' + lateral);
  }
});

test('court rescue anchors have supported rollouts and recover through the exit jump', () => {
  for (let node = court.from; node < court.to; node++) {
    const st = createState(); st[S.latchSeg] = node;
    const anchor = anchorNode(st, track);
    for (let d = 0; d <= 12; d++) assert.equal(holeAt(track.courts, track.nodeS[anchor] + d, 0, 0.54), null);
    const r = drive({ start: track.nodeS[node], speed: 0, rescue: true });
    assert.ok(!r.names.includes('drop') && r.st[S.s] > jump.landS + 8, JSON.stringify({ node, s: r.st[S.s], names: r.names }));
  }
  const blocked = { ...track, courts: [{ ...court, holes: [{ id: 'test', s: track.nodeS[court.from + 2], x: 0, w: 4, len: 8 }] }] };
  assert.ok(courtRescueNode(blocked, court.from + 2) < court.from + 2, 'backs away from unsupported centerline');
});

for (const seed of [1, 20261023, 241023]) test('eight racers reach and leave the court from the real starting grid without a court fall, seed ' + seed, () => {
  const race = makeRace({ track, seed }), seen = new Set(), exited = new Set();
  const falls = race.racers.list.map(() => 0);
  for (let i = 0; i < 300 / DT && exited.size < 8; i++) {
    const before = race.racers.list.map(r => r.st[S.s]);
    race.tick();
    for (const r of race.racers.list) {
      if (r.st[S.s] >= court.fromS && r.st[S.s] <= court.toS) seen.add(r.i);
      if (seen.has(r.i) && r.st[S.s] > jump.landS + 8 && r.st[S.grounded]) exited.add(r.i);
      if (r.falls > falls[r.i]) assert.ok(before[r.i] < court.fromS - 5 || before[r.i] > court.toS + 5,
        'court fall for racer ' + r.i + ' at ' + before[r.i]);
      falls[r.i] = r.falls;
    }
  }
  assert.equal(seen.size, 8, 'all entered from the real preceding bend');
  assert.equal(exited.size, 8, 'all cleared the existing exit jump');
});
