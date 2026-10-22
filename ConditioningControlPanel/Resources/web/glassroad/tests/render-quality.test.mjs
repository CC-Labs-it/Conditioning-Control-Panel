import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createGovernor, pickTier, p95, setTier, Q, TIERS, GOVERNOR } from '../render/quality.js';

const feed = (g, ms, seconds) => {
  const out = [];
  for (let t = 0; t < seconds; t += ms / 1000) { const r = g.sample(ms); if (r) out.push(r); }
  return out;
};

test('render: p95 picks the slow tail', () => {
  const a = Array.from({ length: 100 }, (_, i) => i + 1);
  assert.equal(p95(a), 95);
  assert.equal(p95([]), 0);
});

test('render: governor steps down once after 2 s over 20 ms, not before', () => {
  const g = createGovernor({ tier: 'high' });
  assert.deepEqual(feed(g, 25, GOVERNOR.downS - 0.6), []);
  assert.deepEqual(feed(g, 25, 1), ['mid']);
  assert.equal(g.tier, 'mid');
});

test('render: governor steps up after 10 s under 12 ms, never past the ceiling', () => {
  const g = createGovernor({ tier: 'mid', ceiling: 'high' });
  assert.deepEqual(feed(g, 8, 9), []);
  assert.deepEqual(feed(g, 8, 2), ['high']);
  assert.deepEqual(feed(g, 8, 30), []);
});

test('render: frames between 12 and 20 ms hold the tier; long gaps are ignored', () => {
  const g = createGovernor({ tier: 'mid', ceiling: 'high' });
  assert.deepEqual(feed(g, 16, 30), []);
  assert.equal(g.sample(900), null);
  assert.equal(g.tier, 'mid');
});

test('render: governor floors at phone', () => {
  const g = createGovernor({ tier: 'phone' });
  assert.deepEqual(feed(g, 40, 10), []);
});

test('render: pickTier and setTier', () => {
  assert.equal(pickTier({ coarse: true }), 'phone');
  assert.equal(pickTier({ webgl2: true }), 'mid');
  assert.equal(pickTier({}), 'high');
  assert.equal(pickTier({ override: 'phone' }), 'phone');
  setTier('mid');
  assert.equal(Q.particles, TIERS.mid.particles);
  setTier('high');
});
