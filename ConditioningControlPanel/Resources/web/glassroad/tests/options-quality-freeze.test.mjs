// Owner report 2026-10-26 (a phone, the live web build): changing the graphics options froze the game.
// Measured on a phone-emulated Edge (844x390, DPR 3, 4x CPU throttle, WebGPU): Quality Auto to High under the
// pause card cost one 3.6 s main-thread task plus a 5.8 s frame gap; High picked in the garage and then Race
// cost one 7.0 s task on the first race frame. A Quality step mid-race now waits like a governor verdict, and a
// tier that changed in the menu is re-warmed behind the loading cover before the race starts.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { qualityLanding } from '../render/quality.js';

const src = (p) => readFileSync(new URL('../' + p, import.meta.url), 'utf8');

test('quality landing: held (race, pause card) waits, the menu applies, the tier in use clears', () => {
  assert.deepEqual(qualityLanding({ want: 'high', current: 'phone', hold: true }), { apply: null, pending: 'high' });
  assert.deepEqual(qualityLanding({ want: 'high', current: 'phone', hold: false }), { apply: 'high', pending: null });
  assert.deepEqual(qualityLanding({ want: 'phone', current: 'phone', hold: true }), { apply: null, pending: null });
  assert.deepEqual(qualityLanding({ want: 'phone', current: 'phone', hold: false }), { apply: null, pending: null });
});

test('renderer: a Quality option change goes through qualityLanding with the hold, never setTier at once', () => {
  const r = src('render/renderer.js');
  const fn = r.slice(r.indexOf('function setQuality()'), r.indexOf('function readout('));
  assert.match(fn, /qualityLanding\(\{ want: name, current: Q\.name, hold \}\)/);
  assert.match(fn, /pending = land\.pending/);
  assert.doesNotMatch(fn, /if \(name !== Q\.name\) \{ setTier\(name\)/);
  // a pinned Quality that waited lands at the next course load, before its warm
  const bc = r.slice(r.indexOf('beginCourse(){'), r.indexOf('post: postFor'));
  assert.match(bc, /const pin=pinOf\(\);if\(pin&&pin!==Q\.name\)\{setTier\(pin\);retier\(\);\}/);
});

test('boot: a tier changed since the warm re-warms behind the cover before the race, and frames wait', () => {
  const b = src('boot.js');
  const sr = b.slice(b.indexOf('function startRace() {'), b.indexOf('function toMenu() {'));
  assert.match(sr, /if \(rewarmBusy\) return;/);
  assert.match(sr, /renderer\.tier !== warmedTier && state === 'menu'\) \{ void rewarmThenRace\(\); return; \}/);
  const rw = b.slice(b.indexOf('async function rewarmThenRace()'), b.indexOf('function startRace() {'));
  assert.match(rw, /loading\.start\(\)/);
  assert.match(rw, /await renderer\.compile\(world\.scene, cam\.camera/);
  assert.match(rw, /warmedTier = renderer\.tier/);
  assert.match(b, /renderer\.beginCourse\(\); \/\/ Covered preparation[^\n]*\r?\n\s*warmedTier = renderer\.tier;/);
  const fr = b.slice(b.indexOf('function frame(now) {'), b.indexOf('renderer.frameDue(now)'));
  assert.match(fr, /if \(rewarmBusy\) return;/);
});

test('options card: opened from the pause card, the Quality hint says it changes after the race', async () => {
  const { QUALITY_LATER } = await import('../hud/cards.js');
  assert.equal(QUALITY_LATER, 'Changes after this race');
  assert.doesNotMatch(QUALITY_LATER, /[!–—]/);
  assert.match(src('hud/cards.js'), /key === 'quality' && data\.from === 'pause' \? QUALITY_LATER : hint/);
});
