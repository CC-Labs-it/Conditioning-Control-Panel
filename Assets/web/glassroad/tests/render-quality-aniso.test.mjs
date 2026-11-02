// render/quality.js anisotropy per tier (polish 5, V1): high 8, mid 4, phone 2, never below 1
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { TIERS, ORDER, setTier, Q } from '../render/quality.js';

test('each tier carries its anisotropy, falling with the tier', () => {
  assert.equal(TIERS.high.anisotropy, 8);
  assert.equal(TIERS.mid.anisotropy, 4);
  assert.equal(TIERS.phone.anisotropy, 2);
  for (let i = 1; i < ORDER.length; i++) assert.ok(TIERS[ORDER[i]].anisotropy >= TIERS[ORDER[i - 1]].anisotropy);
});

test('setTier puts the start tier anisotropy on Q', () => {
  setTier('phone'); assert.equal(Q.anisotropy, 2);
  setTier('high'); assert.equal(Q.anisotropy, 8);
});
