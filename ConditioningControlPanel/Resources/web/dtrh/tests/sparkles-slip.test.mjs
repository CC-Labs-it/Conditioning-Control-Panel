import test from 'node:test';
import assert from 'node:assert/strict';

import { slipPlan, showSlip } from '../sparkles-slip.js';

/* B1 W5: what one receipt plays. The host says what was paid and in which words; the slip only
 * decides how much motion it may use. */

test('a paid receipt stamps, flies and bumps the chip at full motion', () => {
  const p = slipPlan({ credited: 80, sp: 4310, lines: ['Place 2 of 8', 'Online x5'] }, false);
  assert.equal(p.amount, 80);
  assert.equal(p.motion, 'full');
  assert.ok(p.stamp && p.chip && p.flight);
  assert.deepEqual(p.lines, ['Place 2 of 8', 'Online x5']);
  assert.ok(p.sparks >= 3 && p.sparks <= 8);
});

test('Gentle counts up only, reduced motion is static', () => {
  const gentle = slipPlan({ credited: 20, sp: 100, gentle: true, lines: ['Gold'] }, false);
  assert.equal(gentle.motion, 'gentle');
  assert.equal(gentle.flight, false);
  const still = slipPlan({ credited: 20, sp: 100, lines: ['Gold'] }, true);
  assert.equal(still.motion, 'static');
  assert.equal(still.flight, false);
});

test('zero pay keeps the reason line and drops the stamp and chip', () => {
  const p = slipPlan({ credited: 0, sp: 100, lines: ['Place 3 of 4', 'Too short to pay'] }, false);
  assert.equal(p.stamp, false);
  assert.equal(p.chip, false);
  assert.equal(p.flight, false);
  assert.equal(p.lines.length, 2);
});

test('no balance, no chip; bad lines are dropped; nothing runs without a document', () => {
  const p = slipPlan({ credited: 5, lines: ['Gold', 3, '', null] }, false);
  assert.equal(p.chip, false);
  assert.deepEqual(p.lines, ['Gold']);
  assert.doesNotThrow(() => showSlip({ type: 'sparkles-receipt', credited: 5, lines: ['Gold'] }));
});
