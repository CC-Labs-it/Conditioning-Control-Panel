import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { getBoudoirBoostPlan } from '../track/boudoir-boosts.js';
import { BOOST_TURN, safeBoostTurn } from '../track/boostSafety.js';
import { safeBoostCorridor } from '../track/boostCorridor.js';
import { qlook } from '../core/math.js';
import { S, BOOST } from '../sim/state.js';
import { step } from '../sim/kart.js';
import { updateRespawn } from '../sim/respawn.js';
import { calmMul } from '../sim/steer.js';
import { steerTo } from '../dev/hook.js';
import { makeFeel, place, acts, recorder, DT } from './sim-fake.mjs';

const json = JSON.parse(readFileSync(new URL('../tracks/boudoir.json', import.meta.url), 'utf8'));
const track = loadTrack(json, { moving: true });
const plan = getBoudoirBoostPlan(track), defaults = makeFeel();
const top = BOOST_TURN.padSpeedMps / (1 + defaults.get('pad.amount'));
const speeds = [BOOST_TURN.padSpeedMps, top * (1 + Math.max(...defaults.get('boost.tierAmount')))];
const runoffM = Math.max(333, BOOST_TURN.rolloutM);
const identity = p => p.s + '/' + p.x;
const indexOf = p => track.boosts.findIndex(b => b.s === p.s && b.x === p.x);

test('live Boudoir moving layout exposes every real dry and wet boost with its full support guard', t => {
  assert.ok(track.moving && track.forks.some(f => f.movingBridge), 'use the boot layout, including its timed bridge');
  assert.ok(Math.abs(speeds[0] - 64.584) < 1e-9 && Math.abs(speeds[1] - 66.7368) < 1e-9);
  assert.equal(Math.round(BOOST_TURN.rolloutM), 333, 'retain the maximum-speed carry and braking envelope');
  const actual = track.boosts.map(identity).sort(), planned = plan.runs.flatMap(r => r.pads).map(identity).sort();
  assert.deepEqual(planned, actual, 'every moving-layout pad belongs to exactly one tested physical train');
  assert.equal(new Set(planned).size, actual.length);
  t.diagnostic(JSON.stringify({ movingPads: actual.length, actualTrains: plan.runs.length, speeds, runoffM }));
  assert.ok(plan.runs.length >= 3 && actual.length >= 6);
  assert.ok(plan.runs.some(r => track.frameAt(r.pads[0].s).surface === 'stream'), 'wet choices remain');
  assert.ok(plan.runs.some(r => track.frameAt(r.pads[0].s).surface !== 'stream'), 'dry choices remain');
  for (const pad of track.boosts) {
    assert.ok(safeBoostTurn(track, pad) && safeBoostCorridor(track, pad), 'complete live guard at ' + identity(pad));
    for (const ds of [0, pad.len / 2, pad.len]) for (const x of [pad.x - pad.w / 2, pad.x, pad.x + pad.w / 2]) {
      const f = track.surfaceAt(pad.s + ds, x), q = track.query(...f.p, f.seg);
      assert.ok(q.onRoad && q.railed && Math.abs(q.height) < .2 && !q.forkId, 'painted lane has real main-road support');
    }
  }
});

for (const run of plan.runs) for (const entrySpeed of speeds) test('live ' + run.id + ' pays every pad and clears the maximum-speed runoff at ' + entrySpeed.toFixed(4), () => {
  const first = run.pads[0], last = run.pads.at(-1), start = first.s - 12;
  const target = last.s + last.len + runoffM - start;
  const feel = makeFeel({ 'speed.topMps': top }), events = recorder();
  const st = place(track, { s: start, lateral: first.x, speed: entrySpeed });
  const surface = track.surfaceAt(start, first.x);
  st.set(surface.p, S.posX); st.set(qlook([0, 0, 0, 1], track.frameAt(start).t, surface.n), S.quatX);
  let distance = 0, previous = st[S.s], paidFloor = false;
  for (let tick = 0; tick < 24 / DT && distance < target; tick++) {
    const afterTrain = Math.max(0, distance - (last.s + last.len - start));
    const slow = afterTrain > BOOST_TURN.forcedM, braking = slow && st[S.engineV] > 30.5;
    const steer = steerTo(st, track, st[S.s] + 14, first.x, 3) / Math.max(.2, calmMul(st[S.engineV], feel));
    events.t += DT;
    step(st, acts({ throttle: slow ? (st[S.engineV] < 29.5 ? .2 : 0) : 1, brake: braking ? 1 : 0, steer }), track, feel, DT, events);
    updateRespawn(st, track, feel, DT, events);
    let ds = st[S.s] - previous;
    if (ds < -track.lengthM / 2) ds += track.lengthM;
    distance += ds; previous = st[S.s];
    if (st[S.boostKind] === BOOST.PAD && st[S.boostT] > .12 && st[S.boostT] < .9
      && Math.abs(st[S.engineV] - BOOST_TURN.padSpeedMps) < .01) paidFloor = true;
  }
  const expected = run.pads.map(indexOf), paid = events.all('pad').map(e => e.payload.index);
  assert.ok(expected.every(i => i >= 0));
  assert.deepEqual(paid.filter(i => expected.includes(i)), expected, 'exact live payout indices, including fractional pad positions');
  assert.equal(events.all('boost-start').filter(e => e.payload.kind === 'pad').length, paid.length);
  assert.ok(paidFloor, 'the actual paid boost holds the expected high-speed engine floor');
  assert.deepEqual(events.list.filter(e => ['wall', 'bump', 'drop', 'rescue-pull', 'void'].includes(e.name)).map(e => e.name), []);
  assert.ok(distance >= target, 'the complete last-pad runoff was driven');
});
