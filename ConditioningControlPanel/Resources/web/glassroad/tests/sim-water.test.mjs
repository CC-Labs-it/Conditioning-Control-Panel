/* tests/sim-water.test.mjs - wave 1 WATER: stream current, chute steer, pool landing (sim/flow.js, sim/kart.js).
 * Synthetic tracks: the sim-fake circle with a surface per segment, wrapped so the query carries `flow` and the
 * frame tangent can carry a grade (the TRACK lane's query grows `flow` later; the sim must run either way). */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { S } from '../sim/state.js';
import { step } from '../sim/kart.js';
import { waterAt, waterOut, gradeDeg, surfaceKind, blendPush, SURFACES } from '../sim/flow.js';
import { gripYaw } from '../sim/steer.js';
import { qrotate, v3dot } from '../core/math.js';
import { DT, makeFeel, fakeTrack, place, recorder, acts, run } from './sim-fake.mjs';

const near = (a, b, e, msg = '') => assert.ok(Math.abs(a - b) <= e, `${msg} ${a} vs ${b} (+-${e})`);
const vel = (st) => [st[S.velX], st[S.velY], st[S.velZ]];
const fwd = (st) => qrotate([0, 0, 0], [st[S.quatX], st[S.quatY], st[S.quatZ], st[S.quatW]], [0, 0, -1]);
const STREAM = [5, -2.5];

/** The fake circle with water: surface per segment, flow on stream segments, an optional grade on them. */
function waterTrack({ stream = (seg) => seg >= 12 && seg < 16, pool = () => false, flow = STREAM, grade = 0, w = 200, withFlow = true } = {}) {
  const surface = (seg) => (pool(seg) ? 'pool' : stream(seg) ? 'stream' : 'glass');
  const tr = fakeTrack({ surface, w });
  const base = tr.query, frame = tr.frameAt;
  tr.query = (x, y, z, hint, out) => {
    const q = base(x, y, z, hint, out);
    if (withFlow) q.flow = q.surface === 'stream' ? flow.slice() : [0, 0];
    return q;
  };
  tr.frameAt = (s, out) => {
    const f = frame(s, out);
    if (grade && f.surface === 'stream') {
      const g = grade * Math.PI / 180, c = Math.cos(g);
      f.t = [f.t[0] * c, -Math.sin(g), f.t[2] * c];
    }
    return f;
  };
  return tr;
}

test('flow.js: pure water table', () => {
  const feel = makeFeel(), o = waterOut();
  const glass = waterAt({ surface: 'glass', flow: [9, 9] }, 0, feel, o);
  assert.deepEqual(glass.push, [0, 0]); assert.equal(glass.gripMul, 1); assert.equal(glass.thrustMul, 1); assert.equal(glass.steerMul, 1);
  assert.equal(waterAt({ surface: 'mud' }, 0, feel, o).kind, 'glass', 'unknown surface is glass');
  assert.equal(waterAt(null, 0, feel, o).kind, 'glass');
  const st = waterAt({ surface: 'stream' }, 10, feel, o);
  assert.deepEqual(st.push, [0, 0], 'a stream with no flow yet pushes nothing');
  assert.equal(st.gripMul, 1.8); assert.equal(st.slideMul, 1.5); assert.equal(st.thrustMul, 0.95); assert.equal(st.chute, false);
  const fl = waterAt({ surface: 'stream', flow: [40, -40] }, 0, feel, o);
  assert.deepEqual(fl.push, [12, -6], 'clamped to the schema bounds');
  const ch = waterAt({ surface: 'stream', flow: [8, 0] }, 41, feel, o);
  assert.equal(ch.chute, true); assert.equal(ch.steerMul, 0.6);
  assert.equal(waterAt({ surface: 'stream', flow: [8, 0] }, -41, feel, o).chute, true, 'grade sign does not matter');
  const po = waterAt({ surface: 'pool' }, 0, feel, o);
  assert.equal(po.pool, true); assert.equal(po.thrustMul, 0.85); assert.equal(po.landMul, 0.75); assert.equal(po.dragTcS, 1.2);
  assert.deepEqual(po.push, [0, 0]);
  near(gradeDeg([Math.cos(Math.PI / 6), -0.5, 0]), 30, 1e-9, 'grade');
  near(gradeDeg([0, 0, 1]), 0, 1e-9);
  assert.equal(surfaceKind(undefined), 'glass');
  assert.deepEqual(SURFACES, ['glass', 'grip', 'haze', 'stream', 'pool']);
  const cur = blendPush([0, 0], [5, -2.5], DT, 0.4);
  near(cur[0], 5 * (1 - Math.exp(-DT / 0.4)), 1e-9); near(cur[1], -2.5 * (1 - Math.exp(-DT / 0.4)), 1e-9);
});

test('stream: the current carries the kart along and sideways, eased in with no jerk', () => {
  const feel = makeFeel(), tr = waterTrack({ stream: (seg) => seg >= 12 }), rec = recorder();
  const st = place(tr, { speed: 20 }), a = acts();
  let maxDv = 0, prev = vel(st), entered = -1;
  run(st, tr, feel, a, 3, rec, (s) => {
    const v = vel(s), dv = Math.hypot(v[0] - prev[0], v[1] - prev[1], v[2] - prev[2]);
    prev = v;
    if (entered < 0 && rec.first('surface')) entered = rec.t;
    if (entered >= 0 && rec.t <= entered + 0.5) maxDv = Math.max(maxDv, dv);
    return false;
  });
  assert.ok(entered > 0.9 && entered < 1.3, 'entered the stream at s 120: ' + entered);
  assert.ok(maxDv < 0.5, 'no jerk at the entry: ' + maxDv + ' m/s in one tick');
  const f = tr.frameAt(st[S.s]), v = vel(st);
  near(v3dot(v, f.t) - st[S.engineV], STREAM[0], 0.5, 'carried along at the flow over the engine');
  near(v3dot(v, f.b), STREAM[1], 0.3, 'carried sideways at the lateral flow');
  assert.ok(st[S.lateral] < -2.5, 'drifted to the outside (left): ' + st[S.lateral]);
  near(st[S.engineV], 20 - 3 * feel.get('engine.coastMps2'), 0.6, 'the engine only coasted');
});

test('stream: no push on glass, none without a flow field, nor on a stream with no flow', () => {
  const feel = makeFeel();
  for (const tr of [waterTrack({ stream: () => false }), waterTrack({ stream: () => true, withFlow: false }), waterTrack({ stream: () => true, flow: [0, 0] })]) {
    const st = place(tr, { speed: 20 }), rec = recorder();
    run(st, tr, feel, acts(), 2, rec);
    const f = tr.frameAt(st[S.s]), v = vel(st);
    near(v3dot(v, f.b), 0, 0.15, 'no sideways drift (the fake circle alone bends about 0.06)');
    near(v3dot(v, f.t) - st[S.engineV], 0, 0.8, 'no free speed');
    near(st[S.lateral], 0, 0.1);
    assert.ok(!rec.first('splash') && !rec.first('chute'));
  }
});

test('stream: surface events once per change, and the current eases out on leaving', () => {
  const feel = makeFeel(), tr = waterTrack(), rec = recorder();   // stream from s 120 to 160
  const st = place(tr, { speed: 20 });
  run(st, tr, feel, acts(), 5, rec);
  const ev = rec.all('surface');
  assert.equal(ev.length, 2, 'two changes: ' + JSON.stringify(ev.map((e) => e.payload)));
  assert.deepEqual(ev[0].payload, { kind: 'stream', from: 'glass' });
  assert.deepEqual(ev[1].payload, { kind: 'glass', from: 'stream' });
  const f = tr.frameAt(st[S.s]), v = vel(st);
  near(v3dot(v, f.b), 0, 0.3, 'the sideways carry is gone on glass');
  assert.ok(st[S.lateral] < -1, 'but the river moved the kart over: ' + st[S.lateral]);
});

test('stream: grip loosens there, steering still turns the kart', () => {
  const feel = makeFeel(), rec = recorder();
  const glass = waterTrack({ stream: () => false }), water = waterTrack({ stream: () => true, flow: [0, 0] });
  const a = place(glass, { speed: 25 }), b = place(water, { speed: 25 });
  run(a, glass, feel, acts({ throttle: 1, steer: 1 }), 0.6, rec);
  run(b, water, feel, acts({ throttle: 1, steer: 1 }), 0.6, rec);
  assert.ok(b[S.lateral] > 0.3 && a[S.lateral] > 0.3, 'both turned right');
  // the heading leads the velocity more on water: the slip angle is larger
  const slip = (st, tr) => { const f = fwd(st), v = vel(st), fr = tr.frameAt(st[S.s]); return Math.abs(v3dot(v, fr.b) / Math.max(1, v3dot(v, fr.t)) - v3dot(f, fr.b) / Math.max(1e-3, v3dot(f, fr.t))); };
  assert.ok(slip(b, water) > slip(a, glass) * 1.3, 'more slide on water ' + slip(b, water) + ' vs ' + slip(a, glass));
});

test('chute: steer scaled by chuteSteerMul, chute event once per entry', () => {
  const feel = makeFeel(), rec = recorder();
  const tr = waterTrack({ stream: (seg) => seg >= 12, flow: [0, 0], grade: 35 });
  const st = place(tr, { speed: 25 }), a = acts({ throttle: 1, steer: 1 });
  run(st, tr, feel, a, 1.2, rec, () => !!rec.first('chute'));
  const ch = rec.all('chute');
  assert.equal(ch.length, 1); near(ch[0].payload.s, 120, 1.5, 'at the chute lip');
  run(st, tr, feel, a, 0.5, rec);
  assert.equal(rec.all('chute').length, 1, 'still once');
  const speed = v3dot(vel(st), fwd(st));
  near(st[S.yawRate] / gripYaw(speed, feel), feel.get('water.chuteSteerMul'), 0.06, 'yaw share on the chute');
  assert.ok(rec.all('surface').length === 1 && rec.first('surface').payload.kind === 'stream');
});

test('pool: a landing kills the vertical speed with no bounce, costs speed once, one splash', () => {
  const feel = makeFeel(), tr = waterTrack({ stream: () => false, pool: () => true }), rec = recorder();
  const st = place(tr, { speed: 20, height: 3, airborne: true });
  const t = run(st, tr, feel, acts(), 2, rec, () => !!rec.first('splash'));
  const sp = rec.all('splash');
  assert.equal(sp.length, 1);
  const p = sp[0].payload;
  near(p.down, Math.sqrt(2 * feel.get('hop.gravityMps2') * 3), 1.0, 'down = vertical speed at touch-down, positive down');
  near(p.speed, 20, 1.0, 'ground speed at touch-down');
  near(p.x, 0, 0.1); near(p.s, 100 + 20 * t, 1.5);
  assert.equal(st[S.airborne], 0); assert.equal(st[S.grounded], 1);
  near(st[S.velY], 0, 0.05, 'no bounce');
  const f = tr.frameAt(st[S.s]);
  near(v3dot(vel(st), f.t), 20 * feel.get('water.poolLandMul'), 0.5, 'speed cut once');
  near(st[S.engineV], (20 - feel.get('engine.coastMps2') * t) * feel.get('water.poolLandMul'), 0.6, 'engine cut with it (it coasted in the air)');
  run(st, tr, feel, acts(), 0.5, rec);
  assert.equal(rec.all('splash').length, 1, 'still one splash');
  assert.ok(!rec.all('airborne').length, 'never took off again');
  assert.equal(st[S.grounded], 1);
  // the same drop onto glass bounces (a tier landing) and never splashes
  const g = waterTrack({ stream: () => false }), r2 = recorder(), gs = place(g, { speed: 20, height: 3, airborne: true });
  run(gs, g, feel, acts(), 2, r2);
  assert.ok(r2.first('land') && !r2.first('splash'));
});

test('pool: thrust capped at poolThrustMul of top, an engine above the cap is dragged down', () => {
  const feel = makeFeel(), tr = waterTrack({ stream: () => false, pool: () => true }), rec = recorder();
  const top = feel.get('speed.topMps'), cap = top * feel.get('water.poolThrustMul');
  const st = place(tr, {});
  run(st, tr, feel, acts({ throttle: 1 }), 8, rec);
  near(st[S.engineV], cap, 0.05, 'capped');
  const hot = place(tr, { speed: top, engine: top });
  run(hot, tr, feel, acts({ throttle: 1 }), 1.2, rec);
  assert.ok(hot[S.engineV] < cap + (top - cap) * Math.exp(-1) + 0.05, 'dragged toward the cap within a time constant: ' + hot[S.engineV]);
  assert.ok(hot[S.engineV] >= cap - 1e-6);
  assert.equal(rec.all('surface').length, 0, 'no surface change on a one-surface road');
});
