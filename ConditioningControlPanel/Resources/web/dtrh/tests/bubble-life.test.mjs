// Which bubbles get a living picture (game/bubbleLife.js). The drawing itself needs a canvas.
// variants.js cannot load under node (absolute page imports), so the ids are listed here.
import test from 'node:test';
import { moteTintFor } from '../game/bubbleLife.js';
import assert from 'node:assert/strict';
import { lifeKindFor, LIFE_CAP } from '../game/bubbleLife.js';

const POOL = ['flash', 'subliminal', 'pink', 'spiral', 'braindrain', 'bambifreeze', 'video', 'htlink'];
const SPECIALS = ['golden', 'heart', 'gold_droplet', 'prism', 'brittle', 'echo', 'tease', 'darter', 'plain', 'material_silk'];

test('every variant in the pool and every special has an effect', () => {
  for (const id of [...POOL, ...SPECIALS]) {
    assert.ok(lifeKindFor({ variantId: id, sprite: 'x.png' }), id);
  }
});

test('names that differ from their effect map across', () => {
  assert.equal(lifeKindFor({ variantId: 'pink', sprite: 'x' }), 'pinkfilter');
  assert.equal(lifeKindFor({ variantId: 'plain', sprite: 'x' }), 'bubble');
  assert.equal(lifeKindFor({ variantId: 'material_silk', sprite: 'x' }), 'material');
});

test('no sprite or an unknown bubble stays still', () => {
  assert.equal(lifeKindFor({ variantId: 'flash', sprite: '' }), null);
  assert.equal(lifeKindFor({ variantId: 'nope', sprite: 'x' }), null);
  assert.equal(lifeKindFor({ variantId: 'constructor', sprite: 'x' }), null);
  assert.equal(lifeKindFor(null), null);
  assert.ok(LIFE_CAP > 0);
});

test('the motes round a material bubble take its own colour', () => {
  assert.equal(moteTintFor('material_chrome'), '206,214,228');
  assert.equal(moteTintFor('material_lube'), '168,230,255');
  assert.equal(moteTintFor('material_nope'), '255,255,255');
});
