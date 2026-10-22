// The fall gathers pace (engine/fallPace.js).
import test from 'node:test';
import assert from 'node:assert/strict';
import { fallRamp, RAMP_SEC, RAMP_MAX, VEIN_RUSH } from '../engine/fallPace.js';

test('the ramp starts at the old pace, ends 20% up, and never goes back', () => {
  assert.equal(fallRamp(0), 1);
  assert.equal(fallRamp(-5), 1);
  assert.ok(Math.abs(fallRamp(RAMP_SEC) - (1 + RAMP_MAX)) < 1e-9);
  assert.ok(Math.abs(fallRamp(RAMP_SEC * 10) - 1.2) < 1e-9);
  let prev = 1;
  for (let s = 0; s <= RAMP_SEC; s += 5) { const r = fallRamp(s); assert.ok(r >= prev); prev = r; }
  assert.ok(fallRamp(30) < 1.01, 'the first half minute is barely touched');
});

test('a junction tube is a rush', () => {
  assert.ok(VEIN_RUSH > 1 && VEIN_RUSH <= 2);
});
