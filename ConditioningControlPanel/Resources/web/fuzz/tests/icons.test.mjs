import test from 'node:test';
import assert from 'node:assert/strict';
import { ITEMS } from '../data/items.js';
import { ICON_CATALOG } from '../ui/icon-catalog.js';
import { ART, BADGE_NAMES, iconMarkup } from '../ui/icon-art.js';
import { iconSet, setIconSet } from '../ui/icons.js';

test('every unlock, fork, theme and ending has intentional clip art, with no stale rows', () => {
  assert.deepEqual(Object.keys(ICON_CATALOG).sort(), ITEMS.map((i) => i.id).sort());
  for (const [id, art] of Object.entries(ICON_CATALOG)) {
    assert.ok(Object.hasOwn(ART, art.shape), `${id}: missing ${art.shape}`);
    assert.ok(!art.badge || BADGE_NAMES.includes(art.badge), `${id}: missing ${art.badge}`);
  }
  assert.ok(new Set(Object.values(ICON_CATALOG).map((a) => a.shape)).size > 50);
});

test('art is self-contained vector geometry safe to repeat in shop, guide and purchase flight', () => {
  for (const art of Object.values(ICON_CATALOG)) for (const glossy of [false, true]) {
    const svg = iconMarkup(art, glossy);
    assert.doesNotMatch(svg, /<(?:script|foreignObject|image|text)|\b(?:href|id|on\w+)=|url\(|gradient|filter/i);
    assert.match(svg, /stroke-width="3.2"/);
    assert.match(svg, /<(?:path|circle|rect)/);
  }
});

test('glossy upgrades object artwork and restores to flat, without replacing it with a tile', () => {
  const art = ICON_CATALOG.v_frame;
  assert.notEqual(iconMarkup(art), iconMarkup(art, true));
  assert.ok(iconMarkup(art, true).includes(ART.frame()));
  setIconSet('glossy'); assert.equal(iconSet(), 'glossy');
  setIconSet('bad'); assert.equal(iconSet(), 'glossy');
  setIconSet('plain'); assert.equal(iconSet(), 'plain');
  setIconSet('emoji'); assert.equal(iconSet(), 'plain');
});
