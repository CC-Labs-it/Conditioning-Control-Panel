import { test } from 'node:test';
import assert from 'node:assert/strict';
import { TETHER_LOOK, tetherPoints, tetherSegment, tetherBreakPose, tetherScale } from '../render/items/tetherMath.js';

test('tether links cover the authored sag without moving their terminal anchors', () => {
  const a = [-3, 7, 4], b = [12, 9, -18], points = new Float32Array(33);
  tetherPoints(a, b, 0.6, 12, points);
  for (let i = 0; i < 10; i++) {
    const s = tetherSegment(points, i);
    assert.ok(Math.abs(Math.hypot(s.dx, s.dy, s.dz) - 1) < 1e-6);
    for (const [axis, dir, j] of [['x', 'dx', 0], ['y', 'dy', 1], ['z', 'dz', 2]]) assert.ok(Math.abs(s[axis] + s[dir] * s.length - points[(i + 1) * 3 + j]) < 1e-5);
    assert.equal(s.roll, (i & 1) * Math.PI / 2);
  }
  assert.ok(Math.abs(points[1] - a[1] - TETHER_LOOK.liftM) < 1e-6);
  assert.ok(Math.abs(points[31] - b[1] - TETHER_LOOK.liftM) < 1e-6);
});

test('Gentle tether geometry is still and retains the tightening sag', () => {
  const a = [0, 0, 0], b = [0, 0, 20], early = new Float32Array(33), late = new Float32Array(33);
  tetherPoints(a, b, 0.3, 0, early, TETHER_LOOK, true);
  tetherPoints(a, b, 0.3, 99, late, TETHER_LOOK, true);
  assert.deepEqual(early, late);
  tetherPoints(a, b, 1, 99, late, TETHER_LOOK, true);
  assert.ok(late[16] > early[16]);
});

test('zero-length segments remain finite and chain width never expands with distance', () => {
  const s = tetherSegment(new Float32Array(6), 0);
  assert.deepEqual([s.length, s.dx, s.dy, s.dz], [0, 0, 1, 0]);
  assert.equal(tetherScale(0.1, 0.35).width, tetherScale(4.5, 0.35).width);
  assert.ok(tetherScale(4.5, 0.35).length > tetherScale(0.1, 0.35).length);
});

test('snap halves separate in opposing directions and only pass carries a spin cue', () => {
  const a = tetherBreakPose(0.35, 0, 'pass'), b = tetherBreakPose(0.35, 9, 'pass');
  assert.ok(a.recoil < 0 && b.recoil > 0); assert.ok(a.spin > 0);
  for (const reason of ['timeout', 'range', 'snap', 'out']) assert.equal(tetherBreakPose(0.35, 0, reason).spin, 0);
  const still = tetherBreakPose(0.35, 0, 'pass', true);
  assert.deepEqual([still.recoil, still.lift, still.turn, still.spin], [0, 0, 0, 0]);
});

test('break keeps the chain continuous while the spin cue rises and both fade over 300ms', () => {
  assert.equal(tetherBreakPose(0, 0, 'pass').alpha, 1);
  assert.equal(tetherBreakPose(0, 0, 'pass').cueAlpha, 0);
  assert.equal(tetherBreakPose(0.3, 0, 'pass').cueAlpha, 1);
  assert.equal(tetherBreakPose(0.3, 0, 'pass').alpha, 1);
  assert.equal(tetherBreakPose(0.45, 0, 'pass').alpha, 1);
  assert.equal(tetherBreakPose(0.8, 0, 'pass').alpha, 0);
  for (let t = 0; t < 1; t += 0.01) { const p = tetherBreakPose(t, 0, 'pass'); assert.ok(p.alpha >= 0 && p.alpha <= 1); assert.equal(p.done, t >= 0.8); }
  assert.deepEqual(tetherBreakPose(0.4, 3, 'pass'), tetherBreakPose(0.4, 3, 'pass'), 'same paused age is identical');
});
