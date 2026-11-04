import { test } from 'node:test';
import assert from 'node:assert/strict';
import { advanceBox, BOX_WINDOW, boxInterval, boxPrizeSeconds, createToyBox, dealBox, fitPiece, restoreBox, PIECES } from '../ui/unlocks/arcade/toy-box-model.js';
import { createRun, earn, sideLump } from '../sim/economy.js';

/* Toy Box paid outside the side budget until 2026-11-04 and made half a run's income (dev/measure.mjs). It is weighted now. */
test('completed boxes pay more each time, from a weighted share of the side budget', () => {
  const run = createRun();
  earn(run, 1e6);
  sideLump(run, 1e6, 'noise');
  const paid = [0, 1, 10].map((completed) => sideLump(run, boxPrizeSeconds(completed) * 100, 'toy_box'));
  assert.ok(paid[0] < paid[1] && paid[1] < paid[2], `box prizes ${paid}`);
  assert.ok(paid[0] > boxPrizeSeconds(0) * 100 * 0.2, 'the weight keeps a real part of the ask after other prizes drained the budget');
  assert.ok(run.side.paid <= run.side.budget + 1e6 * 0.05 + 1, 'the budget holds');
});

test('intro has two pieces, later boxes rise to four distinct approved pieces', () => {
  for (let round = 0; round < 10; round++) {
    const b = dealBox(round, () => .37);
    assert.equal(b.pieces.length, Math.min(4, round + 2));
    assert.equal(new Set(b.pieces).size, b.pieces.length);
    assert.ok(b.pieces.every((id) => PIECES.includes(id)));
    assert.deepEqual([...b.tray].sort(), [...b.pieces].sort());
  }
});
test('only a matching unplaced piece fits and completion occurs once', () => {
  let b = dealBox(3, () => .7), completes = 0;
  assert.equal(fitPiece(b, b.pieces[0], b.pieces[1]).fitted, false);
  for (const id of b.pieces) {
    const fit = fitPiece(b, id, id); b = fit.box; completes += Number(fit.complete);
    assert.equal(fitPiece(b, id, id).fitted, false);
  }
  assert.equal(completes, 1); assert.equal(b.phase, 'closing');
  assert.equal(b.completed, 4);
  assert.equal(fitPiece(restoreBox(b), b.pieces[0], b.pieces[0]).complete, false);
});

test('starts closed, waits 15 to 30 active seconds, then offers exactly 5.5 seconds', () => {
  assert.equal(boxInterval(() => 0), 15); assert.equal(boxInterval(() => 1), 30);
  let b = createToyBox(() => 0);
  assert.equal(b.phase, 'closed'); assert.equal(b.pieces.length, 0);
  b = advanceBox(b, 14.9, () => 0).box;
  assert.equal(b.phase, 'closed');
  const opened = advanceBox(b, .1 + 1e-9, () => 0); b = opened.box;
  assert.equal(opened.opened, true); assert.equal(b.phase, 'play');
  assert.ok(Math.abs(b.windowLeft - BOX_WINDOW) < 1e-7);
  b = advanceBox(b, 5.5, () => 0).box;
  assert.equal(b.phase, 'closing'); assert.equal(b.outcome, 'miss');
  assert.equal(b.completed, 0);
  assert.equal(fitPiece(b, 'pawn', 'pawn').fitted, false);
});

test('timeout discards the partial box and waits the remainder of the opening interval', () => {
  let b = dealBox(0, () => 0);
  b = fitPiece(b, b.pieces[0], b.pieces[0]).box;
  b = advanceBox(b, 5.5, () => 0).box;
  assert.deepEqual(b.placed, []); assert.deepEqual(b.pieces, []);
  assert.equal(b.nextIn, 9.5);
  b = advanceBox(b, 9.4, () => .9).box;
  assert.equal(b.phase, 'closed'); assert.equal(b.completed, 0);
  b = advanceBox(b, .1 + 1e-9, () => .9).box;
  assert.equal(b.phase, 'play'); assert.equal(b.pieces.length, 2); assert.deepEqual(b.placed, []);
});

test('successful packing increments difficulty once but cannot make the next opening early', () => {
  let b = dealBox(0, () => 0);
  b = advanceBox(b, 2, () => 0).box;
  for (const id of b.pieces) b = fitPiece(b, id, id).box;
  assert.equal(b.completed, 1); assert.equal(b.nextIn, 13);
  b = advanceBox(b, 12.9, () => .5).box;
  assert.equal(b.phase, 'closed');
  b = advanceBox(b, .1 + 1e-9, () => .5).box;
  assert.equal(b.phase, 'play'); assert.equal(b.pieces.length, 3);
});

test('saved phase, window, cooldown and deal restore without RNG or bonus time', () => {
  for (const seed of [createToyBox(() => .6), advanceBox(dealBox(2, () => .3), 4).box]) {
    const b = restoreBox(JSON.parse(JSON.stringify(seed)));
    assert.deepEqual(b, seed);
    assert.deepEqual(advanceBox(b, 0, () => { throw new Error('unexpected reroll'); }).box, seed);
  }
});

test('prizes rise only with completed boxes and cap at three times the starting prize', () => {
  assert.equal(boxPrizeSeconds(0), 12);
  assert.ok(Math.abs(boxPrizeSeconds(1) - 13.8) < 1e-9);
  assert.equal(boxPrizeSeconds(1000), 36);
  let b = dealBox(0, () => .4);
  b = advanceBox(b, 100, () => .4).box;
  assert.equal(b.completed, 0); assert.equal(boxPrizeSeconds(b.completed), 12);
});

test('a legacy completed box migrates to settled closing without a second completion', () => {
  const b = restoreBox({ round: 2, pieces: ['pawn', 'king'], tray: ['king', 'pawn'], placed: ['pawn', 'king'], phase: 'closing' });
  assert.equal(b.completed, 3); assert.equal(b.phase, 'closing');
  assert.equal(fitPiece(b, 'king', 'king').complete, false);
});
test('partial saves preserve dealt order and placements without rerolling', () => {
  const b = dealBox(1, () => .2), result = fitPiece(b, b.pieces[0], b.pieces[0]).box;
  assert.deepEqual(restoreBox(JSON.parse(JSON.stringify(result))), result);
  assert.equal(restoreBox({ ...b, pieces: ['pawn', 'pawn'] }), null);
  assert.equal(restoreBox({ ...b, pieces: ['pawn', 'unknown'] }), null);
});
