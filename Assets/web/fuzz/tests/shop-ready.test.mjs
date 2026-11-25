import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readyStep, nextNudge, NUDGE_MIN_MS, NUDGE_SPREAD_MS } from '../ui/shop-ready.js';
import { pillPlan, PILL_GAP, PILL_WIDE_MAX } from '../ui/dock.js';

test('a tile jumps only when it turns buyable, never on its first paint', () => {
  assert.equal(readyStep(false, true), 'jump');
  assert.equal(readyStep(undefined, true), 'arm');
  assert.equal(readyStep(true, true), 'stay');
  assert.equal(readyStep(true, false), 'none');
  assert.equal(readyStep(undefined, false), 'none');
});

test('reminder hops come 6 to 8 seconds apart', () => {
  assert.equal(nextNudge(1000, 0), 1000 + NUDGE_MIN_MS);
  assert.equal(nextNudge(1000, 1), 1000 + NUDGE_MIN_MS + NUDGE_SPREAD_MS);
});

test('on a desktop strip, 19 or more pills spread across the bar and still fit', () => {
  for (const n of [19, 24, 30]) {
    const avail = 1248;
    const { rows, basis } = pillPlan(n, avail, { max: PILL_WIDE_MAX });
    const across = Math.ceil(n / rows);
    assert.ok(across * basis + (across - 1) * PILL_GAP <= avail, `${n} pills overflow`);
    assert.ok(across * (basis + PILL_GAP) > avail * 0.9, `${n} pills leave the bar empty: basis ${basis}`);
  }
});
