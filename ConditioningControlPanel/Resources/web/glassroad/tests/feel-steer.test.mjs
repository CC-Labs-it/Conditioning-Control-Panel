// tests/feel-steer.test.mjs - polish 2 FEEL lane: the calmed grip yaw law, the human steering response
// curve, steering sensitivity, the softer camera heading follow and the tighter hop.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { S, createState } from '../sim/state.js';
import { qaxis } from '../core/math.js';
import { baseYaw, calmMul, gripYaw, bendSpeed, yawParams } from '../sim/steer.js';
import { expo, keyRampValue, createKeyRamp, shapeSteer, INPUT_DEFAULTS } from '../input/steerCurve.js';
import { createActionMap } from '../core/actions.js';
import { createRacers } from '../sim/racers.js';
import { createCamRig } from '../render/cameraMath.js';
import { DT, makeFeel, fakeTrack, place, recorder, acts, run } from './sim-fake.mjs';

const near = (a, b, e, msg = '') => assert.ok(Math.abs(a - b) <= e, `${msg} ${a} vs ${b} (+-${e})`);

test('calm: base law when slow, about a third off at cruise, more at top, half at boosted top', () => {
  const f = makeFeel();
  assert.equal(calmMul(0, f), 1);
  assert.equal(calmMul(f.get('steer.calmFromMps'), f), 1, 'nimble when slow');
  near(calmMul(20, f), 0.67, 1e-9, 'cruise');
  near(calmMul(30, f), 0.64, 1e-9, 'top');
  near(calmMul(39, f), 0.5, 1e-9, 'boosted top');
  near(calmMul(60, f), 0.5, 1e-9, 'held above');
  near(calmMul(-20, f), calmMul(20, f), 1e-12, 'reverse reads |v|');
  let last = 1;
  for (let v = 0; v <= 45; v += 0.5) { const m = calmMul(v, f); assert.ok(m <= last + 1e-12, 'never rises with speed at ' + v); last = m; }
  // full-lock numbers in deg/s against the Phase A law
  const deg = (r) => r * 180 / Math.PI;
  near(deg(gripYaw(20, f)) / deg(baseYaw(20, f)), 0.67, 1e-9);
  assert.ok(deg(gripYaw(30, f)) < 0.67 * deg(baseYaw(30, f)), 'top is calmer than cruise');
  near(deg(gripYaw(39, f)), deg(baseYaw(39, f)) / 2, 1e-9);
});

test('sensitivity: steer.userMul scales grip yaw; bots hold it at 1, the player follows the option', () => {
  const f = makeFeel({ 'steer.userMul': 1.4 });
  near(gripYaw(25, f), 1.4 * gripYaw(25, makeFeel()), 1e-12);
  const q = { emit() {} };
  const r = createRacers(fakeTrack(), f, q, { count: 3, seed: 1 });
  const race = { phase: 'racing', timeS: 1 };
  r.step(DT, Object.assign(acts({ throttle: 1 }), { item: false, back: false }), race, {});
  assert.equal(r.player.feel.get('steer.userMul'), 1.4);
  assert.equal(r.list[1].feel.get('steer.userMul'), 1);
  assert.equal(r.list[2].feel.get('steer.userMul'), 1);
});

test('bendSpeed: the crossing of k v and share x the calmed grip yaw; the Phase A closed form with no calm', () => {
  const f = makeFeel();
  assert.equal(bendSpeed(0, f), Infinity);
  const v = bendSpeed(1.2, f, 0.8), k = 1.2 * Math.PI / 180;
  near(k * v, 0.8 * gripYaw(v, f), 1e-4, 'on the law');
  const p = yawParams(f, {});
  assert.equal(bendSpeed(1.2, p, 0.8), v, 'params record and feel agree');
  const flat = makeFeel({ 'steer.cruiseYawMul': 1, 'steer.topYawMul': 1, 'steer.boostYawMul': 1 });
  const ymax = flat.rad('steer.yawRateDegps') * 0.8, ref = flat.get('steer.yawSpeedRefMps'), a = k / ref;
  near(bendSpeed(1.2, flat, 0.8), (-k + Math.sqrt(k * k + 4 * a * ymax)) / (2 * a), 1e-3);
  assert.ok(bendSpeed(1.2, f, 0.8) < bendSpeed(1.2, flat, 0.8), 'the calm slows a grip turn');
});

test('kart: grip yaw follows the calmed law at speed; a drift keeps the uncalmed base law', () => {
  const f = makeFeel(), tr = fakeTrack({ w: 400 });
  const st = place(tr, { speed: 30 });
  run(st, tr, f, acts({ throttle: 1, steer: 1 }), 0.6, recorder());
  const v = st[S.speedPct] * f.get('speed.topMps');
  near(st[S.yawRate], gripYaw(v, f), 0.03 * gripYaw(v, f), 'grip');
  const d = place(tr, { speed: 30 });
  run(d, tr, f, acts({ throttle: 1, hop: true, hopPressed: true, steer: 1 }), 0.6, recorder());
  assert.equal(d[S.driftDir], 1);
  const dv = d[S.speedPct] * f.get('speed.topMps');
  assert.ok(d[S.yawRate] >= f.get('drift.yawMul') * f.get('drift.tightMin') * baseYaw(dv, f) - 1e-6, 'drift is the strong turn');
  assert.ok(d[S.yawRate] > 1.5 * gripYaw(dv, f));
});

test('steer curve: expo is odd, fixed at 0 and 1, finer near centre', () => {
  for (const e of [0, 0.2, 0.35, 1]) {
    assert.equal(expo(0, e), 0); near(expo(1, e), 1, 1e-12); near(expo(-1, e), -1, 1e-12);
    near(expo(-0.4, e), -expo(0.4, e), 1e-12, 'left stays left');
  }
  assert.ok(expo(0.3, 0.35) < 0.3 && expo(0.3, 0.35) > 0.2);
  near(expo(2, 0.35), 1, 1e-12, 'clamped');
});

test('steer curve: a held key builds from keyStart to full over keyRampS; a side change starts over', () => {
  const { keyStart: s0, keyRampS: rs } = INPUT_DEFAULTS;
  assert.equal(keyRampValue(0, s0, rs), s0);
  near(keyRampValue(rs / 2, s0, rs), s0 + (1 - s0) / 2, 1e-12);
  assert.equal(keyRampValue(rs * 3, s0, rs), 1);
  const r = createKeyRamp();
  assert.equal(r.step(1, 0.016, s0, rs), s0, 'the press');
  let v = 0; for (let i = 0; i < 10; i++) v = r.step(1, 0.016, s0, rs);
  near(v, s0 + (1 - s0) * 0.16 / rs, 1e-9, 'builds');
  assert.equal(r.step(-1, 0.016, s0, rs), -s0, 'the other side starts over');
  assert.equal(r.step(0, 0.016, s0, rs), 0, 'release');
  assert.equal(shapeSteer(0.5, null, 0.016, null, r), 0.5, 'untagged sources pass through');
});

test('action map: keys ramp in wall time, sticks get the expo, the tag never leaks, a long gap never jumps', () => {
  const key = { steer: 0, poll(into) { into.steer = this.steer; into.steerKind = 'key'; }, dispose() {} };
  const stick = { steer: 0, poll(into) { into.steer = this.steer; into.steerKind = 'stick'; }, dispose() {} };
  const f = makeFeel({ 'input.keyStart': 0.4, 'input.keyRampS': 0.2 });
  const map = createActionMap([key, stick], { items: true, feel: f });
  key.steer = 1;
  let a = map.sample({ nowS: 10 });
  near(a.steer, 0.4, 1e-9, 'feel keyStart');
  assert.equal('steerKind' in a, false);
  a = map.sample({ nowS: 10.1 }); near(a.steer, 0.7, 1e-9, 'half the ramp');
  a = map.sample({ nowS: 10.2 }); near(a.steer, 1, 1e-9, 'full');
  key.steer = 0; stick.steer = -0.5;
  a = map.sample({ nowS: 10.25 }); near(a.steer, expo(-0.5, f.get('input.stickExpo')), 1e-12);
  stick.steer = 0; key.steer = -1;
  map.sample({ nowS: 11 }); a = map.sample({ nowS: 30 });
  near(a.steer, -(0.4 + 0.6 * 0.1 / 0.2), 1e-9, 'a 19 s gap counts as 0.1 s');
});

test('camera: the heading follows on yawFollowTcS, softer than the position follow', () => {
  const f = makeFeel(), rig = createCamRig(f), k = createState();
  rig.snap(k, 16 / 9);
  const yaw = -0.5 * Math.PI / 180, q = qaxis([0, 0, 0, 1], [0, 1, 0], yaw);
  k[S.quatX] = q[0]; k[S.quatY] = q[1]; k[S.quatZ] = q[2]; k[S.quatW] = q[3];
  rig.update(k, null, DT, 16 / 9);
  const want = yaw * (1 - Math.exp(-DT / f.get('camera.yawFollowTcS')));
  near(rig.yaw, want, 1e-9);
  assert.ok(f.get('camera.yawFollowTcS') > f.get('camera.followHTcS'));
});

test('hop: snappy, about 0.4 m and a third of a second, lands soft (no thud on every drift)', () => {
  const f = makeFeel(), tr = fakeTrack({ w: 400 }), rec = recorder();
  const st = place(tr, { speed: 25 }), y0 = st[S.posY];
  let apex = 0;
  run(st, tr, f, acts({ throttle: 1, hopPressed: true }), 0.8, rec, (s) => { apex = Math.max(apex, s[S.posY] - y0); return false; });
  const land = rec.first('land');
  assert.ok(apex > 0.3 && apex < 0.5, 'apex ' + apex);
  assert.ok(land.payload.airTime > 0.25 && land.payload.airTime < f.get('air.lightS'), 'air ' + land.payload.airTime);
  assert.equal(land.payload.tier, 0);
});
