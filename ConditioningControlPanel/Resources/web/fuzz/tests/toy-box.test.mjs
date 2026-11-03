import { test } from 'node:test';
import assert from 'node:assert/strict';
import { dealBox, fitPiece, restoreBox, PIECES } from '../ui/unlocks/arcade/toy-box-model.js';

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
  assert.equal(fitPiece(restoreBox(b), b.pieces[0], b.pieces[0]).complete, false);
});
test('partial saves preserve dealt order and placements without rerolling', () => {
  const b = dealBox(1, () => .2), result = fitPiece(b, b.pieces[0], b.pieces[0]).box;
  assert.deepEqual(restoreBox(JSON.parse(JSON.stringify(result))), result);
  assert.equal(restoreBox({ ...b, pieces: ['pawn', 'pawn'] }), null);
  assert.equal(restoreBox({ ...b, pieces: ['pawn', 'unknown'] }), null);
});
