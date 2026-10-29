// render/roadAaMath.js - the road's widen-and-melt shapes (polish 5, V1)
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { ROAD_AA, aaLine, aaFloor, aaParity, xor01, melt } from '../render/roadAaMath.js';

const near = (a, b, e = 1e-6) => Math.abs(a - b) <= e;
// average of f over one cell [0, 1) by midpoint sampling
const avg = (f, n = 4000) => { let s = 0; for (let i = 0; i < n; i++) s += f((i + 0.5) / n); return s / n; };

test('melt is 0 while cells are many pixels wide and 1 once they pass a pixel', () => {
  assert.equal(melt(0), 0);
  assert.equal(melt(ROAD_AA.meltFrom), 0);
  assert.equal(melt(ROAD_AA.meltTo), 1);
  assert.equal(melt(5), 1);
  assert.ok(melt(0.5) > 0 && melt(0.5) < 1);
});

test('a line wider than a pixel is drawn as it was', () => {
  for (const d of [0, 0.01, 0.03, 0.05, 0.2]) assert.ok(near(aaLine(d, 0.08, 0.001), 1 - (d >= 0.08 ? 1 : (t => t * t * (3 - 2 * t))(d / 0.08))));
});

test('a line narrower than a pixel widens and dims: the light per cell stays the same', () => {
  const w = 0.02;
  const crisp = avg((f) => aaLine(Math.min(f, 1 - f), w, 0.001));
  for (const px of [0.05, 0.1, 0.2]) {
    const wide = avg((f) => aaLine(Math.min(f, 1 - f), w, px));
    assert.ok(near(wide, crisp, 2e-3), px + ': ' + wide + ' vs ' + crisp);
    assert.ok(aaLine(0, w, px) < 1, 'dimmer at its centre');
  }
});

test('a cell line melts to its average once the cells pass a pixel', () => {
  const w = 0.08;
  for (const d of [0, 0.1, 0.25, 0.5]) assert.ok(near(aaLine(d, w, 2, 1), w), 'flat at ' + d);
  // the melted value is the crisp line's average over the cell
  assert.ok(near(avg((f) => aaLine(Math.min(f, 1 - f), w, 0.001)), w, 2e-3));
});

test('aaFloor is floor away from the steps and continuous across them', () => {
  for (const x of [0.3, 1.5, 2.7, -0.4, -3.2]) assert.ok(near(aaFloor(x, 0.01), Math.floor(x)), String(x));
  // no jump bigger than the slope allows across a step
  let worst = 0;
  for (let i = 0; i < 4000; i++) { const x = 0.5 + i / 2000, dx = 1 / 2000; worst = Math.max(worst, Math.abs(aaFloor(x + dx, 0.05) - aaFloor(x, 0.05))); }
  assert.ok(worst < 0.05, 'max step ' + worst);
});

test('aaFloor melts to the ramp x - 0.5 when the steps pass a pixel', () => {
  for (const x of [0.1, 0.9, 3.3]) assert.ok(near(aaFloor(x, 3), x - 0.5));
});

test('aaParity is floor mod 2, blended, 0.5 once melted', () => {
  for (const [x, p] of [[0.5, 0], [1.5, 1], [2.5, 0], [3.5, 1], [-0.5, 1]]) assert.ok(near(aaParity(x, 0.01), p), x + ' -> ' + aaParity(x, 0.01));
  for (const x of [0.2, 1.7, 5.1]) assert.ok(near(aaParity(x, 3), 0.5));
  const mid = aaParity(1, 0.1);
  assert.ok(mid > 0.2 && mid < 0.8, 'between at a step: ' + mid);
});

test('xor01 is XOR at 0 and 1 and stays 0.5 when either side is melted', () => {
  assert.equal(xor01(0, 0), 0); assert.equal(xor01(1, 1), 0);
  assert.equal(xor01(0, 1), 1); assert.equal(xor01(1, 0), 1);
  assert.equal(xor01(0.5, 0), 0.5); assert.equal(xor01(0.5, 1), 0.5);
});
