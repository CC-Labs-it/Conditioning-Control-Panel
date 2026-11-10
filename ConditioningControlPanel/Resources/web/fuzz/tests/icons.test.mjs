import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync } from 'node:fs';
import { clips } from '../assets/clipart/clips.js';
import { ITEMS } from '../data/items.js';
import { ICON_CATALOG } from '../ui/icon-catalog.js';
import { ART, BADGE_NAMES, iconMarkup } from '../ui/icon-art.js';
import { emiUpgradeArt } from '../ui/emi-upgrade-art.js';
import { CHAIN, HER_TILES } from '../sim/takeover.js';
import { iconSet, setIconSet } from '../ui/icons.js';

test('every unlock, fork, theme and ending has intentional clip art, with no stale rows', () => {
  assert.deepEqual(Object.keys(ICON_CATALOG).sort(), ITEMS.map((i) => i.id).sort());
  for (const [id, art] of Object.entries(ICON_CATALOG)) {
    assert.ok(Object.hasOwn(ART, art.shape), `${id}: missing ${art.shape}`);
    assert.ok(!art.badge || BADGE_NAMES.includes(art.badge), `${id}: missing ${art.badge}`);
  }
  assert.ok(new Set(Object.values(ICON_CATALOG).map((a) => a.shape)).size > 50);
});

test('the plain set ships a rest image and a playable sheet for every catalogue shape', () => {
  const shapes = [...new Set([...Object.values(ICON_CATALOG).map(art => art.shape), 'mixer'])].sort();  // also used in the permanent audio controls
  assert.deepEqual(Object.keys(clips).sort(), shapes);
  for (const shape of shapes) {
    const clip = clips[shape];
    assert.ok(clip.frames > 1 && clip.ms > 0 && clip.w >= 128 && clip.h >= 128, shape);
    for (const suffix of ['.webp', '.motion.webp']) {
      assert.ok(existsSync(new URL('../assets/clipart/' + shape + suffix, import.meta.url)), shape + suffix);
    }
  }
});

test("every companion tile shows the original sprite with EMI's neutral screen", () => {
  const ids = [...ITEMS.filter(item => item.lane === 'emi').map(item => item.id),
    ...CHAIN, 'go_end', 'o_emi', 'p_line', 'm_watch', ...HER_TILES.map(tile => tile.id)];
  for (const id of ids) {
    const art = emiUpgradeArt(id);
    const poses = [...art.matchAll(/href="(assets\/img\/emi\/body[^"]*\.png)"/g)];
    assert.ok(poses.length, id);
    assert.equal(art.split('assets/img/emi/face-neutral.png').length - 1, poses.length, id);
    for (const [, pose] of poses) assert.ok(existsSync(new URL('../' + pose, import.meta.url)), pose);
  }
  assert.ok(existsSync(new URL('../assets/img/emi/face-neutral.png', import.meta.url)));
});

test('the vector source is self-contained and safe to repeat in shop, guide and purchase flight', () => {
  for (const art of Object.values(ICON_CATALOG)) for (const glossy of [false, true]) {
    const svg = iconMarkup(art, glossy);
    if (art.shape === 'mascot') {
      assert.match(svg, /assets\/img\/emi\/body-idle\.png/);
      assert.match(svg, /assets\/img\/emi\/face-neutral\.png/);
      continue;
    }
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
