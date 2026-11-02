// tests/render-tethers.test.mjs - the Tether chain shape (render/items/tetherMath.js): ends on both karts,
// sags in the middle and tightens as the link ages.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { TETHER_LOOK, tetherPoints } from '../render/items/tetherMath.js';

test('the chain starts and ends above the karts and sags less as it tightens', () => {
  const a = [0, 0, 0], b = [10, 0, 0], out = new Float32Array((TETHER_LOOK.links + 1) * 3);
  const n = tetherPoints(a, b, 0, 0, out);
  assert.equal(n, TETHER_LOOK.links + 1);
  assert.ok(Math.abs(out[1] - TETHER_LOOK.liftM) < 1e-6 && Math.abs(out[(n - 1) * 3] - 10) < 1e-6);
  const mid = (k) => { tetherPoints(a, b, k, 0, out); return out[Math.floor(n / 2) * 3 + 1]; };
  assert.ok(mid(0) < TETHER_LOOK.liftM - 0.5, 'slack chain sags');
  assert.ok(mid(1) > mid(0), 'a long tow pulls it taut');
  for (const v of out) assert.ok(Number.isFinite(v));
});
