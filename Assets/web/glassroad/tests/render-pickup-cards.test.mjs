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

test('pickup boxes: the card sits inside its bubble and the bubble clears the road at the lowest bob', async () => {
  const { PICKUP_BOX: B, pickupBoxPose } = await import('../render/items/pickupCardMath.js');
  const low = pickupBoxPose(-C.bob), high = pickupBoxPose(C.bob);
  assert.ok(low.centre - B.bubbleR > .15, 'glass never cuts into the road');
  assert.ok(Math.hypot(C.width / 2, C.height / 2) * B.cardScale < B.bubbleR, 'card corners stay inside the glass');
  assert.ok(low.lift > .2 && high.centre < 1.25 + 1.45 / 2, 'lower than the old card centre');
  assert.ok(Math.abs(low.centre - (low.lift + C.height * B.cardScale / 2)) < 1e-9);
});

test('pickup boxes: neighbouring bubbles in a row never overlap on any course', async () => {
  const { readFileSync, readdirSync } = await import('node:fs');
  const { PICKUP_BOX: B } = await import('../render/items/pickupCardMath.js');
  const dir = new URL('../tracks/', import.meta.url);
  for (const f of readdirSync(dir).filter((n) => /^[a-z]+\.json$/.test(n))) {
    const rows = new Map();
    for (const it of JSON.parse(readFileSync(new URL(f, dir), 'utf8')).items || []) {
      if (it.kind !== 'box') continue;
      { const key = it.node + ':' + (it.ds || 0);   // floating boxes stand in a line along a ramp arc (ds), not a row
        (rows.get(key) || rows.set(key, []).get(key)).push(it.x); }
    }
    for (const xs of rows.values()) {
      xs.sort((a, b) => a - b);
      for (let i = 1; i < xs.length; i++) assert.ok(xs[i] - xs[i - 1] > 2 * B.bubbleR + .5, f + ' row too tight');
    }
  }
});

test('pickup boxes: the rim breathes slowly and holds still under Gentle or reduced motion', async () => {
  const { PICKUP_BOX: B, pickupBoxPulse } = await import('../render/items/pickupCardMath.js');
  assert.ok(B.pulseHz <= .5, 'slower than any flicker');
  for (let t = 0; t < 4; t += .05) {
    assert.equal(pickupBoxPulse(t, true), 1);
    const p = pickupBoxPulse(t);
    assert.ok(p <= 1 + 1e-9 && p >= 1 - B.pulse - 1e-9);
  }
});

test('pickup boxes: the bubbles are one instanced draw added once to the item root', async () => {
  const { readFileSync } = await import('node:fs');
  const cards = readFileSync(new URL('../render/items/pickupCards.js', import.meta.url), 'utf8');
  const mats = readFileSync(new URL('../render/items/materials.js', import.meta.url), 'utf8');
  const visuals = readFileSync(new URL('../render/items/visuals.js', import.meta.url), 'utf8');
  assert.match(mats, /export function boxBubbleCloud\(cap\)[^]*?new InstancedMesh\(/);
  assert.equal(cards.match(/boxBubbleCloud\(/g).length, 1, 'built once per card set');
  assert.match(visuals, /root\.add\(pickupCards\.bubbles\.mesh\)/);
  assert.match(visuals, /pickupCards\.bubbles\.end\(\)/);
});
