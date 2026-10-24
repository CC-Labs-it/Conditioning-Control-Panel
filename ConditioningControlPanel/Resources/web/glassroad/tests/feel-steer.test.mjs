// tests/feel-steer.test.mjs - polish 2 FEEL lane: the calmed grip yaw law, the human steering response
// curve, steering sensitivity, the softer camera heading follow and the tighter hop. Polish 4: the calmer
// law (about a third off again), the progressive key ramp and the stick / touch power curve.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { S, createState } from '../sim/state.js';
import { qaxis } from '../core/math.js';
import { baseYaw, calmMul, gripYaw, bendSpeed, yawParams } from '../sim/steer.js';
import { expo, powCurve, keyRampValue, createKeyRamp, shapeSteer, INPUT_DEFAULTS } from '../input/steerCurve.js';
import { createActionMap } from '../core/actions.js';
import { createRacers } from '../sim/racers.js';
import { createCamRig } from '../render/cameraMath.js';
import { DT, makeFeel, fakeTrack, place, recorder, acts, run } from './sim-fake.mjs';

const near = (a, b, e, msg = '') => assert.ok(Math.abs(a - b) <= e, `${msg} ${a} vs ${b} (+-${e})`);

test('calm: base law when slow, then the feel multipliers at cruise, top and boosted top', () => {
  const f = makeFeel();
  assert.equal(calmMul(0, f), 1);
  assert.equal(calmMul(f.get('steer.calmFromMps'), f), 1, 'nimble when slow');
  near(calmMul(20, f), f.get('steer.cruiseYawMul'), 1e-9, 'cruise');
  near(calmMul(30, f), f.get('steer.topYawMul'), 1e-9, 'top');
  near(calmMul(39, f), f.get('steer.boostYawMul'), 1e-9, 'boosted top');
  near(calmMul(60, f), f.get('steer.boostYawMul'), 1e-9, 'held above');
  near(calmMul(-20, f), calmMul(20, f), 1e-12, 'reverse reads |v|');
  let last = 1;
  for (let v = 0; v <= 45; v += 0.5) { const m = calmMul(v, f); assert.ok(m <= last + 1e-12, 'never rises with speed at ' + v); last = m; }
  assert.ok(gripYaw(30, f) < f.get('steer.cruiseYawMul') * baseYaw(30, f), 'top is calmer than cruise');
});

test('polish 4: full-lock grip yaw is about a third under polish 3 at 20, 30 and 39 m/s', () => {
  const f = makeFeel(), deg = (r) => r * 180 / Math.PI;
  // polish 3 full-lock deg/s: 51.2, 40.0, 26.9
  for (const [v, was] of [[20, 51.2], [30, 40.0], [39, 26.9]]) {
    const now = deg(gripYaw(v, f));
    assert.ok(now < was * 0.75 && now > was * 0.6, `${v} m/s: ${now.toFixed(1)} deg/s vs ${was}`);
  }
  near(deg(gripYaw(8, f)), 104.2, 0.1, 'slow is still nimble');
  // full lock at top is a wide arc: over 55 m
  assert.ok(30 / gripYaw(30, f) > 55);
});

test('every Threshold bend remains makeable at top speed with the calmed drift', () => {
  const f = makeFeel(), top = f.get('speed.topMps');
  const drift = f.get('drift.yawMul') * gripYaw(top, f);
  for (const r of [36, 48, 56, 70]) {
    const need = top / r;
    assert.ok(drift * f.get('drift.tightMax') >= need, 'drift holds r ' + r);
    // a held grip key alone makes the wide ones at near top, the tight ones only after a lift
    const v = bendSpeed(180 / Math.PI / r, f, 1);
    assert.ok(v > 20, 'grip turn at r ' + r + ' holds ' + v.toFixed(1) + ' m/s');
  }
  assert.ok(drift * f.get('drift.tightMin') < top / 36 * 1.1, 'stick away from the turn widens a drift to about the helix');
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

test('kart: grip and drift both follow speed calm, drift remains the stronger turn', () => {
  const f = makeFeel(), tr = fakeTrack({ w: 400 });
  const st = place(tr, { speed: 30 });
  run(st, tr, f, acts({ throttle: 1, steer: 1 }), 0.6, recorder());
  const v = st[S.speedPct] * f.get('speed.topMps');
  near(st[S.yawRate], st[S.steerSlew] * gripYaw(v, f), 0.03 * gripYaw(v, f), 'grip');
  const d = place(tr, { speed: 30 });
  run(d, tr, f, acts({ throttle: 1, hop: true, hopPressed: true, steer: 1 }), 0.6, recorder());
  assert.equal(d[S.driftDir], 1);
  const dv = d[S.speedPct] * f.get('speed.topMps');
  assert.ok(d[S.yawRate] >= f.get('drift.yawMul') * f.get('drift.tightMin') * gripYaw(dv, f) - 1e-6, 'drift uses grip calm');
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

test('steer curve: a held key builds from keyStart to full; a short correction changing side starts over', () => {
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

test('polish 4: a held key eases in to full over 0.35 to 0.45 s; stick and touch are gentle near centre', () => {
  const f = makeFeel(), s0 = f.get('input.keyStart'), rs = f.get('input.keyRampS'), p = f.get('input.keyRampPow');
  assert.ok(rs >= 0.35 && rs <= 0.45, 'ramp ' + rs);
  assert.ok(s0 <= 0.2, 'a tap is small: ' + s0);
  assert.ok(keyRampValue(rs / 2, s0, rs, p) < s0 + (1 - s0) / 2, 'eases in (progressive)');
  assert.equal(keyRampValue(rs, s0, rs, p), 1);
  let last = 0;
  for (let t = 0; t <= rs + 0.05; t += 0.01) { const v = keyRampValue(t, s0, rs, p); assert.ok(v >= last); last = v; }
  // the kart's slewed steer under a held key: half lock takes a third of a second
  const key = { poll(into) { into.steer = 1; into.steerKind = 'key'; }, dispose() {} };
  const map = createActionMap([key], { feel: f });
  let slew = 0, t50 = -1;
  for (let i = 0; i < 60 && t50 < 0; i++) {
    const a = map.sample({ nowS: 5 + i * DT });
    const tc = f.get('steer.slewCentreS') + (f.get('steer.slewFullS') - f.get('steer.slewCentreS')) * Math.abs(slew);
    slew += (a.steer - slew) * (1 - Math.exp(-DT / tc));
    if (slew >= 0.5) t50 = i * DT;
  }
  assert.ok(t50 > 0.25 && t50 < 0.45, 'half lock at ' + t50);
  for (const k of ['stickPow', 'touchPow']) {
    const e = f.get('input.' + k);
    assert.ok(e >= 1.8 && e <= 2.2, k);
    near(powCurve(-0.5, e), -Math.pow(0.5, e), 1e-12, 'left stays left');
    assert.equal(powCurve(0, e), 0); near(powCurve(1, e), 1, 1e-12); near(powCurve(-3, e), -1, 1e-12);
    assert.ok(shapeSteer(0.3, k === 'stickPow' ? 'stick' : 'touch', DT, f, null) < 0.12, 'gentle near centre');
  }
});

test('action map: keys ramp in wall time, sticks get the curve, the tag never leaks, a long gap never jumps', () => {
  const key = { steer: 0, poll(into) { into.steer = this.steer; into.steerKind = 'key'; }, dispose() {} };
  const stick = { steer: 0, poll(into) { into.steer = this.steer; into.steerKind = 'stick'; }, dispose() {} };
  const f = makeFeel({ 'input.keyStart': 0.4, 'input.keyRampS': 0.2, 'input.keyRampPow': 1 });
  const map = createActionMap([key, stick], { items: true, feel: f });
  key.steer = 1;
  let a = map.sample({ nowS: 10 });
  near(a.steer, 0.4, 1e-9, 'feel keyStart');
  assert.equal('steerKind' in a, false);
  a = map.sample({ nowS: 10.1 }); near(a.steer, 0.7, 1e-9, 'half the ramp');
  a = map.sample({ nowS: 10.2 }); near(a.steer, 1, 1e-9, 'full');
  key.steer = 0; stick.steer = -0.5;
  a = map.sample({ nowS: 10.25 }); near(a.steer, powCurve(-0.5, f.get('input.stickPow')), 1e-12);
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

test('hop: visible .8 m jump with half a second of air, plain drift hops still land soft', () => {
  const f = makeFeel(), tr = fakeTrack({ w: 400 }), rec = recorder();
  const st = place(tr, { speed: 25 }), y0 = st[S.posY];
  let apex = 0;
  run(st, tr, f, acts({ throttle: 1, hopPressed: true }), 0.8, rec, (s) => { apex = Math.max(apex, s[S.posY] - y0); return false; });
  const land = rec.first('land');
  assert.ok(apex > 0.75 && apex < 0.9, 'apex ' + apex);
  assert.ok(land.payload.airTime > 0.45 && land.payload.airTime < f.get('air.lightS'), 'air ' + land.payload.airTime);
  assert.equal(land.payload.tier, 0);
});
