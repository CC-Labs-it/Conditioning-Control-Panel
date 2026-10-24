import { test } from 'node:test';
import assert from 'node:assert/strict';
import { advanceRibbon, flightSpark } from '../render/items/flight.js';

const history = (n = 28) => ({ pts: new Float32Array(n * 3), n: 0 });

test('projectile ribbon preserves distance and density through a slow frame', () => {
  const fast = history(), slow = history();
  advanceRibbon(fast, 0, 0, 0, 0.5);
  advanceRibbon(slow, 0, 0, 0, 0.5);
  for (let x = 0.5; x <= 12; x += 0.5) advanceRibbon(fast, x, x * 0.2, 0, 0.5);
  for (let x = 3; x <= 12; x += 3) advanceRibbon(slow, x, x * 0.2, 0, 0.5);
  assert.equal(fast.n, slow.n);
  for (let j = 0; j < fast.n * 3; j++) assert.ok(Math.abs(fast.pts[j] - slow.pts[j]) < 0.00001);
});

test('ribbon bounds long-frame work and clears rescue discontinuities', () => {
  const h = history(5);
  advanceRibbon(h, 0, 0, 0, 0.5);
  advanceRibbon(h, 18, 0, 0, 0.5);
  assert.equal(h.n, 5);
  assert.deepEqual(Array.from(h.pts).filter((_, i) => i % 3 === 0), [16, 16.5, 17, 17.5, 18]);
  advanceRibbon(h, 200, 0, 0, 0.5);
  assert.equal(h.n, 1);
  assert.equal(h.pts[0], 200);
  advanceRibbon(h, 200, 0, 0, 0.5);
  assert.equal(h.n, 1, 'paused projectile adds no samples');
});

test('seeking sparks remain finite for horizontal and vertical flight and follow the nose', () => {
  const out = [0, 0, 0];
  for (const velocity of [[20, 0, 0], [0, 20, 0], [0, -20, 0], [0, 0, 0], { x: 3, y: 7, z: -9 }]) {
    for (let i = 0; i < 8; i++) {
      const alpha = flightSpark(i, 8, 1, 5, velocity, 1.05, out);
      assert.ok(out.every(Number.isFinite));
      assert.ok(alpha >= 0 && alpha <= 0.85);
      assert.ok(Math.hypot(...out) < 5);
    }
  }
  flightSpark(4, 8, 0, 0, [20, 0, 0], 1, out);
  assert.equal(out[0], -2, 'sparks trail behind the direction of travel');
  flightSpark(4, 8, 0, 0, [-20, 0, 0], 1, out);
  assert.equal(out[0], 2, 'turning the projectile turns its corkscrew');
});
