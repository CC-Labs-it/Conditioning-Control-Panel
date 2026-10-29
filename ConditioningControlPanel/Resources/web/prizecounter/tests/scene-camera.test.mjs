import { test } from 'node:test';
import assert from 'node:assert/strict';
import { motionAmp, damp, fitFov, emptyToCameraQuat, forwardOf, lookPoint, follow, dipFor, bob, DIP, BOB } from '../scene/camera-math.js';

const near = (a, b, eps = 1e-3) => assert.ok(Math.abs(a - b) < eps, `${a} != ${b}`);

test('motion: full, less is half, off cuts', () => {
  assert.deepEqual(['full', 'less', 'off', undefined].map(motionAmp), [1, 0.5, 0, 1]);
});

test('damp is frame-rate independent', () => {
  const two = 1 - (1 - damp(1 / 60, 4)) ** 2;
  near(two, damp(2 / 60, 4), 1e-9);
  assert.equal(damp(0, 4), 0);
  assert.ok(damp(10, 4) > 0.999);
});

test('fitFov keeps the wider of width and height in frame', () => {
  near(fitFov(1, 2, 2, 1), 90);
  assert.ok(fitFov(0.5, 4, 2, 5) > fitFov(2, 4, 2, 5));   // portrait needs more lens for the same width
  near(fitFov(16 / 9, 1, 2, 1), 90);                      // tall box on a wide screen: height wins
});

test('the Blender camera empties look forward and slightly down', () => {
  // cam_wide in prize-counter.glb: (0, 6, 15), quaternion [0.609, 0, 0, 0.793] = 75 deg about X.
  const f = forwardOf(emptyToCameraQuat([0.609, 0, 0, 0.793]));
  near(f[0], 0); assert.ok(f[1] < -0.2 && f[1] > -0.32); assert.ok(f[2] < -0.95);
  const look = lookPoint([0, 6, 15], f, -0.1);
  near(look[2], -0.1); assert.ok(look[1] > 1.5 && look[1] < 2.5);
  // cam_bay_*: 87.4 deg about X, nearly level.
  const b = forwardOf(emptyToCameraQuat([0.691, 0, 0, 0.722]));
  assert.ok(b[1] < 0 && b[1] > -0.08);
});

test('lazy follow drifts part of the way, never past the item', () => {
  const f = follow([0, 2.6, -0.1], [0.84, 3.5, -0.1]);
  assert.ok(f.look[0] > 0 && f.look[0] < 0.84);
  assert.ok(f.look[1] > 2.6 && f.look[1] < 3.5);
  assert.ok(f.eyeShift[0] < f.look[0]);
  assert.deepEqual(follow([0, 2.6, -0.1], [0.84, 3.5, -0.1], 0).look, [0, 2.6, -0.1]);
  const half = follow([0, 0, 0], [1, 0, 0], 0.5).look[0], full = follow([0, 0, 0], [1, 0, 0]).look[0];
  near(half * 2, full);
});

test('jump dip grows with distance, is capped, and is gone when close or off', () => {
  assert.equal(dipFor(0.05), 0);
  assert.ok(dipFor(1) < dipFor(3));
  assert.equal(dipFor(100), DIP.max);
  near(dipFor(3, 0.5) * 2, dipFor(3));
  assert.equal(dipFor(3, 0), 0);
});

test('bob rests at zero and peaks at half a period', () => {
  near(bob(0), 0); near(bob(BOB.period / 2), BOB.height); near(bob(BOB.period / 2, 0.5), BOB.height / 2);
  assert.equal(bob(1, 0), 0);
});
