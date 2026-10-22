import { test } from 'node:test';
import assert from 'node:assert/strict';
import { S, BOOST } from '../sim/state.js';
import { step, applyBoost, spawn } from '../sim/kart.js';
import { createState } from '../sim/state.js';
import { gripYaw } from '../sim/steer.js';
import { qrotate, v3dot, qlook } from '../core/math.js';
import { DT, makeFeel, fakeTrack, place, recorder, acts, run } from './sim-fake.mjs';

const near = (a, b, e, msg = '') => assert.ok(Math.abs(a - b) <= e, `${msg} ${a} vs ${b} (+-${e})`);
const QW = [0, 0, 0, 1];
const vel = (st) => [st[S.velX], st[S.velY], st[S.velZ]];
const up = (st) => qrotate([0, 0, 0], [st[S.quatX], st[S.quatY], st[S.quatZ], st[S.quatW]], [0, 1, 0]);

test('spawn: at rest on the grid pose', () => {
  const tr = fakeTrack(), st = spawn(createState(), tr, makeFeel(), 1);
  near(st[S.s], tr.lengthM - 6, 1e-3); assert.equal(st[S.grounded], 1); assert.equal(st[S.engineV], 0);
});

test('engine: five-band ramp to top, velocity chases', () => {
  const feel = makeFeel(), tr = fakeTrack(), st = place(tr, {}), rec = recorder(), a = acts({ throttle: 1 });
  const top = feel.get('speed.topMps'), g = feel.get('engine.bandGains'), min = feel.get('speed.minMps');
  let expected = 0;
  for (let i = 0; i < g.length; i++) {
    const lo = Math.max(min, (i * top) / g.length), hi = ((i + 1) * top) / g.length;
    if (hi > lo) expected += (hi - lo) / (g[i] * top);
  }
  let t80 = 0;
  const tTop = run(st, tr, feel, a, 5, rec, (s) => {
    if (!t80 && v3dot(vel(s), tr.frameAt(s[S.s]).t) >= 0.8 * top) t80 = rec.t;
    return s[S.engineV] >= top - 1e-4;
  });
  near(tTop, expected, 2.5 * DT, 'engine top time');
  near(t80, 1.55, 0.12, 'velocity 80% of top');
  run(st, tr, feel, a, 1, rec);
  near(st[S.speedPct], 1, 0.01, 'cruise speedPct');
  // the last bands carry the weight: band 4 takes longer than band 0
  assert.ok(1 / g[4] > 3 * (1 / g[0]));
});

test('brake then reverse, coast', () => {
  const feel = makeFeel(), tr = fakeTrack(), rec = recorder();
  const st = place(tr, { speed: 10 });
  run(st, tr, feel, acts({ brake: 1 }), 2, rec);
  near(st[S.engineV], -feel.get('speed.reverseMps'), 1e-3);
  assert.ok(v3dot(vel(st), tr.frameAt(st[S.s]).t) < -5, 'rolling backwards');
  const c = place(tr, { speed: 10 });
  run(c, tr, feel, acts(), 1, rec);
  near(c[S.engineV], 10 - feel.get('engine.coastMps2'), 0.05);
});

test('steering: yaw law, right is right', () => {
  const feel = makeFeel(), tr = fakeTrack(), rec = recorder();
  const st = place(tr, { speed: 25 });
  run(st, tr, feel, acts({ throttle: 1, steer: 1 }), 0.5, rec);
  // the calmed grip law (sim/steer.js) at the kart's own speed
  const want = gripYaw(st[S.speedPct] * feel.get('speed.topMps'), feel);
  assert.ok(st[S.yawRate] > 0 && Math.abs(st[S.yawRate] - want) < 0.15 * want, 'yaw ' + st[S.yawRate] + ' vs ' + want);
  assert.ok(st[S.lateral] > 0.3, 'moved right ' + st[S.lateral]);
});

test('boost envelope: rise, hold, exponential tail', () => {
  const feel = makeFeel(), tr = fakeTrack({ w: 200 }), rec = recorder(), a = acts({ throttle: 1 });
  const top = feel.get('speed.topMps'), st = place(tr, { speed: top });
  applyBoost(st, BOOST.DRIFT1, 1, feel, rec);
  const amt = feel.get('boost.tierAmount')[0], hold = feel.get('boost.tierHoldS')[0];
  const rise = feel.get('boost.riseS'), tc = feel.get('boost.tailTcS');
  assert.deepEqual(rec.first('boost-start').payload, { kind: 'drift', tier: 1, amount: amt, holdS: hold });
  run(st, tr, feel, a, rise, rec);
  near(st[S.engineV], top * (1 + amt), 0.05, 'risen');
  run(st, tr, feel, a, hold, rec);
  near(st[S.engineV], top * (1 + amt), 0.05, 'held');
  const end = rec.first('boost-end');
  near(end.t, rise + hold, 1.5 * DT, 'boost-end at tail start');
  run(st, tr, feel, a, tc, rec);
  near(st[S.engineV], top * (1 + amt * Math.exp(-1)), 0.1, 'one tail constant');
  run(st, tr, feel, a, 4 * tc, rec);
  assert.equal(st[S.boostKind], BOOST.NONE);
  near(st[S.engineV], top, 0.05);
  assert.equal(rec.all('boost-end').length, 1);
});

test('walls: tangent kept, normal x0.4 out, engine -12%, drift and boost cancelled', () => {
  const feel = makeFeel(), tr = fakeTrack(), rec = recorder(), a = acts({ throttle: 1 });
  const st = place(tr, { lateral: 5, headingDeg: 20, speed: 30, engine: 30 });
  let pre = null, preE = 0;
  run(st, tr, feel, a, 1, rec, (s) => {
    if (rec.first('wall')) return true;
    pre = vel(s); preE = s[S.engineV]; return false;
  });
  const hit = rec.first('wall'), b = tr.frameAt(st[S.s]).b;
  const into = v3dot(pre, b);
  near(hit.payload.dv, into * 1.4, 0.3, 'dv');
  assert.equal(hit.payload.side, 1);
  near(v3dot(vel(st), b), -0.4 * into, 0.3, 'bounced out');
  near(st[S.engineV], preE * 0.88, 0.2, 'engine loss');
  assert.ok(st[S.lateral] <= tr.frameAt(0).w / 2 - 0.69);

  const d = place(tr, { lateral: 5, headingDeg: 25, speed: 30, engine: 30 }), r2 = recorder();
  d[S.driftDir] = 1; applyBoost(d, BOOST.PAD, 0, feel, r2);
  run(d, tr, feel, acts({ throttle: 1, hop: true, steer: 1 }), 1, r2, () => !!r2.first('wall'));
  assert.equal(r2.first('drift-cancel').payload.reason, 'wall');
  assert.ok(r2.first('boost-end'));
  assert.equal(d[S.driftDir], 0); assert.equal(d[S.boostKind], BOOST.NONE);
});

test('gentle: open sides act as soft rails', () => {
  const feel = makeFeel({ 'gentle.on': true }), tr = fakeTrack({ flags: () => [] }), rec = recorder();
  const st = place(tr, { lateral: 5, headingDeg: 20, speed: 25 });
  run(st, tr, feel, acts({ throttle: 1 }), 1, rec);
  assert.ok(rec.first('wall')); assert.ok(!rec.first('edge-enter')); assert.equal(st[S.grounded], 1);
});

test('off-road haze: thrust loss, skipped while boosting', () => {
  const feel = makeFeel(), tr = fakeTrack({ surface: () => 'haze', w: 200 }), rec = recorder();
  const st = place(tr, {});
  run(st, tr, feel, acts({ throttle: 1 }), 6, rec);
  near(st[S.engineV], feel.get('speed.topMps') * (1 - feel.get('offroad.hazeThrustLoss')), 0.05);
  applyBoost(st, BOOST.PAD, 0, feel, rec);
  run(st, tr, feel, acts({ throttle: 1 }), 0.5, rec);
  near(st[S.engineV], feel.get('speed.topMps') * (1 + feel.get('pad.amount')), 0.05);
});

test('boost pad: once per entry, pinned at top + pad amount', () => {
  const feel = makeFeel(), tr = fakeTrack({ boosts: [{ node: 20, x: 0, w: 4, len: 6 }] }), rec = recorder();
  const st = place(tr, { s: 190, speed: 20 });
  run(st, tr, feel, acts({ throttle: 1 }), 0.6, rec);
  assert.equal(rec.all('pad').length, 1);
  assert.deepEqual(rec.first('pad').payload, { index: 0 });
  assert.equal(rec.first('boost-start').payload.kind, 'pad');
  near(st[S.engineV], feel.get('speed.topMps') * (1 + feel.get('pad.amount')), 0.05);
});

test('banked road: kart slerps to the road normal and holds its line', () => {
  const feel = makeFeel(), tr = fakeTrack({ rollDeg: 25 }), rec = recorder();
  const st = place(tr, { speed: 25 });
  // tip the kart upright (world up) first: it has to find the bank on its own
  qlook(QW, tr.frameAt(100).t, [0, 1, 0]).forEach((v, i) => { st[S.quatX + i] = v; });
  run(st, tr, feel, acts({ throttle: 1 }), 1, rec);
  assert.ok(v3dot(up(st), tr.frameAt(st[S.s]).n) > 0.999);
  near(st[S.height], 0, 1e-3); near(st[S.lateral], 0, 0.3);
});

test('magnet: airborne gravity follows minus the road normal', () => {
  const feel = makeFeel(), rec = recorder();
  const tr = fakeTrack({ rollDeg: 60, flags: () => ['rail', 'magnet'] });
  const st = place(tr, { speed: 20 });
  const a = acts({ throttle: 1, hopPressed: true });
  run(st, tr, feel, a, 1, rec);
  assert.ok(rec.first('hop')); assert.ok(rec.first('land'));
  near(st[S.lateral], 0, 0.3, 'landed where it hopped');
  assert.equal(st[S.grounded], 1);
});

test('step leaves the kart alone while respawn owns it', () => {
  const feel = makeFeel(), tr = fakeTrack(), rec = recorder(), st = place(tr, { speed: 10 });
  st[S.respawnPhase] = 2;
  const before = st.slice();
  step(st, acts({ throttle: 1 }), tr, feel, DT, rec);
  assert.deepEqual(st, before);
});
