import { test } from 'node:test';
import assert from 'node:assert/strict';
import { PICKUP_CARD as C, pickupFaceDue } from '../render/items/pickupCardMath.js';

test('pickup cards: picture fits below the old card centre while clearing the road at lowest bob', () => {
  assert.ok(C.height >= 2.2 && C.width >= 1.5);
  assert.ok(C.hover + C.height / 2 < 1.25 + 1.45 / 2);
  assert.ok(C.hover - C.bob > .2);
  assert.ok(C.spin < .7);
  assert.equal(C.faces, 4, 'fixed shared canvas budget independent of pickup count');
});

test('pickup cards: pause retains the frame, but expired or disabled media clears immediately', () => {
  const clip = {};
  assert.equal(pickupFaceDue(clip, clip, 0, 10), false);
  assert.equal(pickupFaceDue(clip, null, 0, 0), true);
  assert.equal(pickupFaceDue(clip, {}, 0, 0), true);
  assert.equal(pickupFaceDue(null, null, 0, 0, true), true);
});

test('pickup cards: advancing playback is capped at twelve uploads per second per shared face', () => {
  const clip = {};
  assert.equal(pickupFaceDue(clip, clip, .01, .082), false);
  assert.equal(pickupFaceDue(clip, clip, .01, 1 / 12), true);
});
