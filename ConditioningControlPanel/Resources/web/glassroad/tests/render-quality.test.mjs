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

// polish 4: the owner sat on phone at 59 Hz because a vsynced frame (16.7 ms) is never under 12 ms
const feedAt = (g, ms, displayMs, seconds) => {
  const out = [];
  for (let t = 0; t < seconds; t += ms / 1000) { const r = g.sample(ms, displayMs); if (r) out.push(r); }
  return out;
};

test('render: at 60 Hz a tier that holds vsync steps back up', () => {
  const g = createGovernor({ tier: 'phone', ceiling: 'high' });
  assert.deepEqual(feedAt(g, 16.7, 16.7, GOVERNOR.upS * 2 + 1), ['mid', 'high']);
  assert.deepEqual(feedAt(createGovernor({ tier: 'phone', ceiling: 'high' }), 16.7, 0, GOVERNOR.upS * 3), []);   // the old rule
});

test('render: at 60 Hz only a slow mean steps down, and never in the boot grace', () => {
  assert.deepEqual(feedAt(createGovernor({ tier: 'high' }), 19, 16.7, 6), []);          // 53 fps: under the 1.2 line
  // Firefox WebGPU paces rAF unevenly: 9 ms and 24.4 ms alternating is a steady 60 fps with a p95 over 1.4 intervals
  const jitter = createGovernor({ tier: 'mid', ceiling: 'high' }), out = [];
  for (let t = 0; t < GOVERNOR.upS * 1.5; t += 0.0167) { const r = jitter.sample(t * 60 % 2 < 1 ? 9 : 24.4, 16.7); if (r) out.push(r); }
  assert.deepEqual(out, ['high']);
  assert.deepEqual(feedAt(createGovernor({ tier: 'high' }), 33.4, 16.7, 3), ['mid']);
  const graced = createGovernor({ tier: 'high', rules: { ...GOVERNOR, graceS: 6 } });
  assert.deepEqual(feedAt(graced, 33.4, 16.7, 5), []);
  assert.deepEqual(feedAt(graced, 33.4, 16.7, 4), ['mid']);
});

test('render: each step down makes the next step up wait longer', () => {
  const g = createGovernor({ tier: 'high' });
  assert.deepEqual(feedAt(g, 33.4, 16.7, 3), ['mid']);
  assert.deepEqual(feedAt(g, 16.7, 16.7, GOVERNOR.upS + 1), []);
  assert.deepEqual(feedAt(g, 16.7, 16.7, GOVERNOR.upS), ['high']);
});

test('render: an Intel integrated GPU starts on mid; a discrete one keeps the old pick', () => {
  assert.equal(pickTier({ gpu: 'Intel(R) HD Graphics' }), 'mid');
  assert.equal(pickTier({ gpu: 'Intel(R) UHD Graphics 770' }), 'mid');
  assert.equal(pickTier({ gpu: 'Intel(R) Iris(R) Xe Graphics' }), 'mid');
  assert.equal(pickTier({ gpu: 'nvidia blackwell' }), 'high');
  assert.equal(pickTier({ gpu: 'Intel(R) Arc(TM) A770' }), 'high');
  assert.equal(pickTier({ gpu: 'Intel(R) HD Graphics', coarse: true }), 'phone');
});
