// The Deep End board model (ui/unlocks/arcade/deep-end/board.js): classic 2048 rules, ported small.
import test from 'node:test';
import assert from 'node:assert/strict';
import { createBoard, restoreBoard, snapshot, slide, spawn, deal, canMove, deepest, TIER_MAX } from '../ui/unlocks/arcade/deep-end/board.js';

const boardOf = (rows) => {
  const b = createBoard(rows.length);
  rows.forEach((row, r) => row.forEach((tier, c) => { if (tier) b.tiles.push({ id: b.nextId++, tier, r, c }); }));
  return b;
};
const grid = (b) => {
  const g = Array.from({ length: b.n }, () => Array(b.n).fill(0));
  for (const t of b.tiles) g[t.r][t.c] = t.tier;
  return g;
};

test('merges toward the move, one merge per tile', () => {
  const b = boardOf([[1, 1, 1, 1], [2, 2, 0, 2], [0, 0, 0, 0], [3, 0, 3, 3]]);
  const out = slide(b, 'left');
  assert.ok(out.moved);
  assert.deepEqual(grid(b), [[2, 2, 0, 0], [3, 2, 0, 0], [0, 0, 0, 0], [4, 3, 0, 0]]);
  assert.equal(out.merges.length, 4);
});

test('right and down resolve from the far edge', () => {
  const b = boardOf([[1, 1, 1, 0], [0, 0, 0, 0], [0, 0, 0, 0], [0, 0, 0, 0]]);
  slide(b, 'right');
  assert.deepEqual(grid(b)[0], [0, 0, 1, 2]);
  const v = boardOf([[2, 0, 0, 0], [2, 0, 0, 0], [2, 0, 0, 0], [0, 0, 0, 0]]);
  slide(v, 'down');
  assert.deepEqual(grid(v).map((r) => r[0]), [0, 0, 2, 3]);
});

test('a move that slides nothing is a no-op', () => {
  const b = boardOf([[1, 2, 0, 0], [0, 0, 0, 0], [0, 0, 0, 0], [0, 0, 0, 0]]);
  assert.equal(slide(b, 'left').moved, false);
  assert.equal(slide(b, 'up').moved, false);
});

test('the ceiling tier never merges and a full mixed board is locked', () => {
  const b = boardOf([[TIER_MAX, TIER_MAX, 0, 0], [0, 0, 0, 0], [0, 0, 0, 0], [0, 0, 0, 0]]);
  slide(b, 'left');
  assert.equal(b.tiles.length, 2);
  const locked = boardOf([[1, 2, 1, 2], [2, 1, 2, 1], [1, 2, 1, 2], [2, 1, 2, 1]]);
  assert.equal(canMove(locked), false);
  locked.tiles[0].tier = 2;
  assert.equal(canMove(locked), true);
});

test('spawn fills an empty cell with tier 1 or 2, deal makes two tiles', () => {
  const b = deal(createBoard(4), () => 0.5);
  assert.equal(b.tiles.length, 2);
  for (const t of b.tiles) assert.ok(t.tier === 1 || t.tier === 2);
  const full = boardOf([[1, 2, 1, 2], [2, 1, 2, 1], [1, 2, 1, 2], [2, 1, 2, 1]]);
  assert.equal(spawn(full, Math.random), null);
});

test('a whole random dive keeps the board sound and always ends', () => {
  let seed = 7;
  const rng = () => ((seed = (seed * 16807) % 2147483647) / 2147483647);
  const b = deal(createBoard(4), rng);
  const dirs = ['up', 'left', 'down', 'right'];
  let moves = 0;
  while (canMove(b) && moves < 5000) {
    const out = slide(b, dirs[Math.floor(rng() * 4)]);
    if (out.moved) spawn(b, rng);
    moves++;
    const cells = new Set(b.tiles.map((t) => t.r * 4 + t.c));
    assert.equal(cells.size, b.tiles.length, 'no two tiles share a cell');
  }
  assert.ok(deepest(b) >= 4, 'a random dive reaches Heavy');
});

test('restore keeps a good save and refuses a broken one', () => {
  const b = boardOf([[3, 0, 0, 0], [0, 1, 0, 0], [0, 0, 0, 0], [0, 0, 0, 2]]);
  const back = restoreBoard(snapshot(b), 4);
  assert.deepEqual(grid(back), grid(b));
  assert.equal(restoreBoard({ n: 4, tiles: [{ tier: 99, r: 0, c: 0 }] }, 4).tiles.length, 0);
  assert.equal(restoreBoard({ n: 4, tiles: [{ tier: 1, r: 0, c: 0 }, { tier: 2, r: 0, c: 0 }] }, 4).tiles.length, 0);
});
