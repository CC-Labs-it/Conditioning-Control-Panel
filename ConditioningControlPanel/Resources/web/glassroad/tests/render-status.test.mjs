// Item status looks, burst pool and throw trails (render/items/statusMath.js), music road pass 2.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { ITEMS } from '../sim/itemTable.js';
import { STATUS_LOOK as L, STATUS_IDS, SHAPE, statusStep, statusState, statusSprites, createBurstPool, createTrail } from '../render/items/statusMath.js';

test('every item that lands on another racer has a status look', () => {
  for (const [id, it] of Object.entries(ITEMS)) if (it.use !== 'self') assert.ok(STATUS_IDS.includes(id), id);
  assert.ok(!STATUS_IDS.includes('surge'));
});

test('a status pops IN, holds, then shrinks OUT after the effect ends', () => {
  const s = statusState();
  let k = statusStep(s, 'flash', 0.016);
  assert.equal(s.fresh, true);
  assert.ok(k > 0 && k < 0.3);
  statusStep(s, 'flash', 0.016);
  assert.equal(s.fresh, false);
  let peak = 0;
  for (let i = 0; i < 30; i++) peak = Math.max(peak, statusStep(s, 'flash', 0.016));
  assert.ok(peak > 1, 'a little overshoot');
  k = statusStep(s, 'flash', 0.5);
  assert.ok(Math.abs(k - 1) < 0.05, 'holds at full');
  const a = statusStep(s, null, 0.1);
  assert.ok(a > 0 && a < 1, 'shrinking');
  assert.equal(statusStep(s, null, 1), 0);
  assert.equal(s.kind, null);
});

test('Gentle: no overshoot and an IN no quicker than the rise floor', () => {
  const s = statusState();
  let peak = 0;
  for (let i = 0; i < 60; i++) peak = Math.max(peak, statusStep(s, 'pink', 0.016, { gentle: true, riseS: 0.6 }));
  assert.ok(peak <= 1 + 1e-9);
  const t = statusState();
  statusStep(t, 'pink', 0.3, { gentle: true, riseS: 0.6 });
  assert.ok(statusStep(t, 'pink', 0, { gentle: true, riseS: 0.6 }) < 0.8);
});

test('a new kind restarts the look; a self item is ignored', () => {
  const s = statusState();
  statusStep(s, 'freeze', 0.5);
  statusStep(s, 'melt', 0.016);
  assert.equal(s.kind, 'melt'); assert.equal(s.fresh, true);
  const t = statusState();
  assert.equal(statusStep(t, 'surge', 0.1), 0);
});

test('looks read at a distance: Flashes deals cards, Pink a cloud, sizes in metres', () => {
  const shapes = (kind) => { const out = []; statusSprites(kind, 1, 1, 0, (x, y, z, size, shp) => out.push({ x, y, z, size, shp })); return out; };
  const fl = shapes('flash');
  assert.ok(fl.filter((s) => s.shp === SHAPE.card).length >= 6);
  assert.ok(fl.some((s) => s.size >= 0.7));
  const pk = shapes('pink');
  assert.ok(pk.filter((s) => s.shp === SHAPE.puff).length >= 8);
  for (const s of [...fl, ...pk]) assert.ok(Math.hypot(s.x, s.z) < 2.5 && s.y > -0.5 && s.y < 4);
  assert.equal(statusSprites('flash', 1, 0, 0, () => assert.fail('k 0 draws nothing')), 0);
});

test('the burst pool moves, fades and recycles its oldest particles', () => {
  const pool = createBurstPool(8);
  let seed = 3; const rand = () => { seed = (seed * 16807) % 2147483647; return seed / 2147483647; };
  pool.burst([0, 1, 0], 5, 4, 0.5, 0.3, SHAPE.star, [1, 0, 0], [0, 0, 1], rand);
  assert.equal(pool.live(), 5);
  const before = []; pool.each((x, y, z) => before.push([x, y, z]));
  pool.step(0.1);
  const after = []; pool.each((x, y, z) => after.push([x, y, z]));
  assert.notDeepEqual(before, after);
  pool.burst([0, 0, 0], 6, 4, 0.5, 0.3, SHAPE.star, [1, 0, 0], [0, 0, 1], rand);
  assert.equal(pool.live(), 8);
  pool.step(0); pool.step(2);
  assert.equal(pool.live(), 0);
});

test('a trail stores a point per gap of travel, newest first, and caps its length', () => {
  const tr = createTrail(4, 0.5);
  assert.ok(tr.push(0, 0, 0));
  assert.ok(!tr.push(0.2, 0, 0));
  for (let i = 1; i <= 6; i++) tr.push(i, 0, 0);
  assert.equal(tr.n, 4);
  const xs = []; tr.points((x, y, z, f) => xs.push([x, f]));
  assert.deepEqual(xs.map((p) => p[0]), [6, 5, 4, 3]);
  assert.equal(xs[0][1], 0); assert.equal(xs[3][1], 1);
  assert.deepEqual(tr.last([0, 0, 0]), [6, 0, 0]);
});

test('status timings stay in the house range (IN and OUT both present)', () => {
  assert.ok(L.inS > 0.1 && L.inS < 0.5);
  assert.ok(L.outS > 0.2 && L.outS < 1);
});
