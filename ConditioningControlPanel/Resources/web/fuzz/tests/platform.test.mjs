import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readPlatform, offerable, whyNot, offeredWindow, DEVICE_ROWS, devicePlatform } from '../ui/platform.js';
import { BY_ID, ITEMS } from '../data/items.js';
import * as E from '../sim/economy.js';
import { worldStates, layoutWorld } from '../ui/guide-world-model.js';

const media = (fine) => (q) => ({ matches: q === '(any-pointer: fine)' ? fine : false });
const desktop = { navigator: { userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/142.0', vibrate() {} }, matchMedia: media(true) };
const android = { navigator: { userAgent: 'Mozilla/5.0 (Linux; Android 14; Pixel 7 Pro) Chrome/142.0 Mobile', vibrate() {} }, matchMedia: media(false) };
const iphone = { navigator: { userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) Safari/604.1' }, matchMedia: media(false) };

test('only rows with a device need are platform-bound, and they are the ones the brief names', () => {
  assert.deepEqual([...DEVICE_ROWS].sort(), ['buzz', 'dualwield']);
  assert.equal(BY_ID.buzz.device, 'buzz');
  assert.equal(BY_ID.dualwield.device, 'mouse');
});

test('Phone Buzz: Android browsers only, or a host that says it can buzz', () => {
  assert.equal(readPlatform(desktop).buzz, false, 'desktop Chrome has navigator.vibrate and no motor');
  assert.equal(readPlatform(iphone).buzz, false);
  assert.equal(readPlatform(android).buzz, true);
  assert.equal(readPlatform(desktop, { haptics: true }).buzz, true);
  assert.equal(offerable('buzz', readPlatform(desktop)), false);
  assert.equal(offerable('buzz', readPlatform(android)), true);
});

test('Double Wielding needs a mouse: hidden on a touch-only phone, offered on a desktop', () => {
  assert.equal(offerable('dualwield', readPlatform(desktop)), true);
  assert.equal(offerable('dualwield', readPlatform(android)), false);
  assert.equal(offerable('dualwield', readPlatform(iphone)), false);
  assert.match(whyNot('dualwield', readPlatform(android)), /needs a mouse/);
  assert.equal(whyNot('dualwield', readPlatform(desktop)), '');
});

test('every other row is offered everywhere; copy is plain', () => {
  for (const it of ITEMS) if (!it.device) assert.equal(offerable(it, { buzz: false, mouse: false }), true, it.id);
  for (const id of DEVICE_ROWS) assert.doesNotMatch(whyNot(id, {}), /[–—!]/);
});

test('the shop window drops rows this device cannot use and moves the next ones up', () => {
  const rows = ['a', 'buzz', 'b', 'c', 'd', 'e'].map((id) => BY_ID[id] || { id });
  const shop = (n) => ({ goal: { id: 'g' }, rows: rows.slice(0, n), more: Math.max(0, rows.length - n) });
  const can = (it) => it.id !== 'buzz';
  const w = offeredWindow(shop, 3, can);
  assert.deepEqual(w.rows.map((r) => r.id), ['a', 'b', 'c']);
  assert.equal(w.more, 2);
  assert.equal(w.goal.id, 'g');
  /* Nothing hidden: the window is the engine's own. */
  const plain = shop(3);
  assert.equal(offeredWindow(() => plain, 3, () => true), plain);
});

test('a Journey shelf whose first offer is a device row moves the next one up as the goal', () => {
  const shelf = [BY_ID.buzz, { id: 'x' }, { id: 'y' }];
  const shop = (n) => ({ goal: shelf[0], rows: shelf.slice(1, 1 + n), more: 0 });
  const w = offeredWindow(shop, 6, (it) => it.id !== 'buzz');
  assert.equal(w.goal.id, 'x');
  assert.deepEqual(w.rows.map((r) => r.id), ['y']);
});

test('without Phone Buzz the inbox chain still opens (Fake Inbox opens beside it, not behind it)', () => {
  assert.deepEqual(BY_ID.inbox.req, ['levels']);
  const s = E.createRun();
  s.owned.levels = 1;
  assert.equal(E.isOpen(s, BY_ID.inbox), true);
  assert.equal(E.isOpen(s, BY_ID.buzz), true);
});

test('the map marks device rows off with their reason, and an owned one stays owned', () => {
  const s = E.createRun();
  s.owned.levels = 1;
  s.total = 1e6;
  const W = layoutWorld();
  if (!W.nodes.some((n) => n.id === 'buzz')) return;   // the layout leaves it out: nothing to mark
  const can = (it) => offerable(it, { buzz: false, mouse: true });
  let v = worldStates(s, W, { canOffer: can });
  assert.equal(v.states.buzz, 'off');
  assert.equal(v.device.buzz, true);
  assert.notEqual(worldStates(s, W).states.buzz, 'off', 'no canOffer: unchanged');
  s.owned.buzz = 1;
  v = worldStates(s, W, { canOffer: can });
  assert.equal(v.states.buzz, 'own');
  assert.equal(v.device.buzz, undefined);
});

test('devicePlatform re-reads the device at most once a second', () => {
  let t = 0, fine = false;
  const win = { navigator: {}, matchMedia: (q) => ({ matches: q === '(any-pointer: fine)' && fine }), performance: { now: () => t } };
  const p = devicePlatform(win);
  assert.equal(p.canOffer('dualwield'), false);
  fine = true;
  t = 500;
  assert.equal(p.canOffer('dualwield'), false, 'cached');
  t = 1600;
  assert.equal(p.canOffer('dualwield'), true, 'a mouse plugged in shows up');
});
