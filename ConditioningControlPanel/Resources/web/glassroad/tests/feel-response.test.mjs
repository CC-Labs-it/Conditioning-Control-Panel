// Deliberate keyboard counter-steering through the action map and the real kart steering law.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createKeyRamp, keyRampValue, INPUT_DEFAULTS } from '../input/steerCurve.js';
import { createActionMap } from '../core/actions.js';
import { step } from '../sim/kart.js';
import { S } from '../sim/state.js';
import { fakeTrack, makeFeel, place } from './sim-fake.mjs';

const D = INPUT_DEFAULTS;
const near = (a, b, e = 1e-10) => assert.ok(Math.abs(a - b) <= e, `${a} vs ${b}`);
const drive = (r, side, dt = 1 / 60) => r.step(side, dt, D.keyStart, D.keyRampS, D.keyRampPow);
function commit(r, side = 1, dt = 1 / 60) {
  drive(r, side, 0);
  for (let n = 0; n < Math.ceil(0.5 / dt); n++) drive(r, side, dt);
}

test('fresh taps, held turns and same-side re-press retain the accepted response', () => {
  for (const dir of [-1, 1]) {
    const r = createKeyRamp();
    near(drive(r, dir, 0), dir * D.keyStart);
    for (let n = 1; n <= 36; n++) {
      near(drive(r, dir), dir * keyRampValue(n / 60, D.keyStart, D.keyRampS, D.keyRampPow));
    }
    assert.equal(drive(r, 0), 0, 'release gives the sim zero immediately');
    near(drive(r, dir), dir * D.keyStart);
  }
});

test('a committed reversal begins firmly, stays below half lock and remains symmetric', () => {
  for (const dir of [-1, 1]) {
    const r = createKeyRamp(); commit(r, dir);
    const first = drive(r, -dir) * -dir;
    assert.ok(first > 0.3 && first < 0.5, `bounded reverse response: ${first}`);
    assert.equal(drive(r, 0), 0);
  }
});

test('a brief neutral crossover retains intent; a settled release or same-side re-press does not', () => {
  const direct = createKeyRamp(); commit(direct);
  const want = drive(direct, -1);
  for (const gap of [0.01, 0.04]) {
    const r = createKeyRamp(); commit(r);
    assert.equal(drive(r, 0, gap), 0);
    near(drive(r, -1, 0.01), want);
  }
  const r = createKeyRamp(); commit(r);
  drive(r, 0, 0.1);
  near(drive(r, -1), -D.keyStart);
});

test('rapid alternating corrections never inherit their inherited ramp', () => {
  const r = createKeyRamp(); commit(r);
  assert.ok(drive(r, -1) < -0.3);
  for (let i = 0; i < 30; i++) {
    const dir = i % 2 ? -1 : 1;
    near(drive(r, dir, 0.05), dir * D.keyStart);
  }
  r.reset();
  near(drive(r, -1), -D.keyStart);
});

// At a fixed sim tick, compare the actual new action map against the former reset-on-reversal
// behavior. The reference resets only at the switch; both sides run the same shipped kart physics.
function reversalResponse(legacy, dir) {
  const f = makeFeel(), tr = fakeTrack({ w: 400 }), st = place(tr, { speed: 30 });
  const key = { steer: dir, poll(a) { a.steer = this.steer; a.steerKind = 'key'; a.throttle = 1; }, dispose() {} };
  let map = createActionMap([key], { feel: f });
  const events = { emit() {} }, dt = 1 / 60;
  for (let n = 0; n < 60; n++) step(st, map.sample({ nowS: n * dt }), tr, f, dt, events);
  key.steer = -dir;
  if (legacy) map = createActionMap([key], { feel: f });
  let zero = null, half = null, onset = null;
  for (let n = 0; n < 120; n++) {
    const a = map.sample({ nowS: 1 + n * dt });
    if (!n) onset = Math.abs(a.steer);
    step(st, a, tr, f, dt, events);
    const response = st[S.steerSlew] * -dir;
    if (response >= 0 && zero === null) zero = (n + 1) * dt;
    if (response >= 0.5 && half === null) half = (n + 1) * dt;
  }
  return { zero, half, onset, final: st[S.steerSlew] * -dir };
}

test('real kart counter-steer changes direction sooner without changing sustained lock', (t) => {
  for (const dir of [-1, 1]) {
    const before = reversalResponse(true, dir), after = reversalResponse(false, dir);
    assert.ok(after.zero <= before.zero - 0.03, 'the kart changes turning direction sooner');
    assert.ok(after.half <= before.half - 0.06, 'half opposite lock is materially sooner');
    assert.ok(after.onset < 0.5, 'no instant full-lock request');
    near(after.final, before.final, 0.002);
    t.diagnostic(JSON.stringify({ direction: dir, before, after }));
  }
});

test('input reversal timing is stable at common render rates', () => {
  const times = [];
  for (const hz of [30, 60, 120, 144]) {
    const r = createKeyRamp(), dt = 1 / hz; commit(r, 1, dt);
    let t = 0, v = drive(r, -1, dt);
    while (v > -0.75 && t < 1) { t += dt; v = drive(r, -1, dt); }
    times.push(t);
  }
  assert.ok(Math.max(...times) - Math.min(...times) <= 1 / 30 + 1e-10, times.join(', '));
});
