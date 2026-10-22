import { test } from 'node:test';
import assert from 'node:assert/strict';
import { tierOf, comp, moteAt, popPhase, popMote, rollIconAt, meltEdge, createPool } from '../render/items/fxmath.js';
import { ITEM_TIERS, ITEM_LOOK as L, MOTE_TINT } from '../render/items/look.js';
import { ITEM_IDS } from '../sim/itemTable.js';

test('items: phone tier runs about half the motes and bubbles; unknown tier = high', () => {
  assert.ok(ITEM_TIERS.phone.motes <= ITEM_TIERS.high.motes / 2);
  assert.ok(ITEM_TIERS.phone.bubbles <= ITEM_TIERS.high.bubbles / 2 + 4);
  assert.equal(tierOf('nope'), ITEM_TIERS.high);
  assert.equal(ITEM_TIERS.high.motes, 10, 'ten motes per bubble');
});

test('items: every item id has a mote colour', () => {
  for (const id of ITEM_IDS) assert.match(MOTE_TINT[id], /^\d+,\d+,\d+$/, id);
});

test('items: sim vectors read as arrays or objects', () => {
  assert.equal(comp([1, 2, 3], 2), 3);
  assert.equal(comp({ x: 4, y: 5, z: 6 }, 1), 5);
  assert.equal(comp(null, 0), 0);
});

test('items: motes drift in a ring round the bubble, alpha in range, deterministic', () => {
  const o = [0, 0, 0], R = 0.5;
  for (let i = 0; i < 10; i++) {
    for (const t of [0, 1.3, 7.7]) {
      const m = moteAt(i, 10, t, R, o);
      const r = Math.hypot(o[0], o[2]);
      assert.ok(r >= R * L.moteRingMul[0] - 1e-9 && r <= R * L.moteRingMul[1] + 1e-9, 'ring ' + r);
      assert.ok(m.alpha >= 0 && m.alpha <= 1 && m.size > 0);
    }
  }
  const a = moteAt(3, 10, 2, R, [0, 0, 0]), b = moteAt(3, 10, 2, R, [0, 0, 0]);
  assert.deepEqual(a, b);
});

test('items: pops run 0..1 and their motes start at the pop', () => {
  assert.equal(popPhase(0, 0.6), 0);
  assert.equal(popPhase(0.3, 0.6), 0.5);
  assert.equal(popPhase(2, 0.6), 1);
  assert.deepEqual(popMote(4, 12, 0, [9, 9, 9]).map((x) => Math.abs(x) < 1e-9), [true, true, true]);
});

test('items: a rolling slot never swaps faster than its swap time', () => {
  let last = rollIconAt(0, ITEM_IDS, L.rollSwapS), changes = 0;
  for (let t = 0; t <= 1; t += 0.001) {
    const id = rollIconAt(t, ITEM_IDS, L.rollSwapS);
    if (id !== last) { changes++; last = id; }
  }
  assert.ok(changes <= Math.ceil(1 / L.rollSwapS));
  assert.ok(L.rollSwapGentleS > L.rollSwapS);
  assert.equal(rollIconAt(1, [], 0.1), null);
});

test('items: the Melt puddle edge wobbles and drips inside its bounds, and it moves', () => {
  let lo = 9, hi = 0;
  for (let a = 0; a < Math.PI * 2; a += 0.05) for (let t = 0; t < 10; t += 0.5) {
    const e = meltEdge(a, t); lo = Math.min(lo, e); hi = Math.max(hi, e);
  }
  assert.ok(lo >= 1 - L.meltWobble && hi <= 1 + L.meltWobble * 0.5 + L.meltDripAmp + 1e-9, lo + ' ' + hi);
  assert.ok(hi - lo > L.meltDripAmp * 0.5, 'arms reach out');
  assert.notEqual(meltEdge(1, 0), meltEdge(1, 2));
});

test('items: the pool keeps its cap, reuses objects and hides what a frame did not take', () => {
  const hidden = new Set();
  const pool = createPool(3, (i) => ({ i }), (o) => hidden.add(o.i));
  pool.begin();
  const a = pool.take(), b = pool.take(), c = pool.take();
  assert.equal(pool.take(), null);
  pool.end();
  assert.equal(hidden.size, 0);
  pool.begin();
  assert.equal(pool.take(), a);
  pool.end();
  assert.deepEqual([...hidden].sort(), [1, 2]);
  assert.ok(b && c && pool.size === 3);
});
