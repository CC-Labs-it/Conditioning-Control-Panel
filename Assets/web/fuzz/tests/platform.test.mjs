import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readPlatform, offerable, whyNot, offeredWindow, DEVICE_ROWS, devicePlatform } from '../ui/platform.js';
import { BY_ID, ITEMS } from '../data/items.js';
import { FOLD, LEAD_OF } from '../data/folds.js';
import * as Save from '../sim/save.js';
import * as E from '../sim/economy.js';
import { worldStates, layoutWorld } from '../ui/guide-world-model.js';

const media = (fine) => (q) => ({ matches: q === '(any-pointer: fine)' ? fine : false });
const desktop = { navigator: { userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/142.0', vibrate() {} }, matchMedia: media(true) };
const android = { navigator: { userAgent: 'Mozilla/5.0 (Linux; Android 14; Pixel 7 Pro) Chrome/142.0 Mobile', vibrate() {} }, matchMedia: media(false) };
const iphone = { navigator: { userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) Safari/604.1' }, matchMedia: media(false) };

test('only rows with a device need are platform-bound, and they are the ones the brief names', () => {
  assert.deepEqual([...DEVICE_ROWS].sort(), ['buzz', 'dualwield', 'toy']);
  assert.equal(BY_ID.buzz.device, 'toys');
  assert.equal(BY_ID.toy.device, 'toys');
  assert.equal(BY_ID.dualwield.device, 'mouse');
});

test('Toy Link and Phone Buzz: only when the host said a toy is connected at run start (CCP desktop app)', () => {
  for (const win of [desktop, android, iphone]) {
    assert.equal(readPlatform(win).toys, false, 'the web and phones never offer them, Android web included');
    for (const id of ['toy', 'buzz']) assert.equal(offerable(id, readPlatform(win)), false, id);
  }
  /* Only the boolean true counts: the old haptics cap, a toy support flag or a truthy string offer nothing. */
  for (const caps of [{ haptics: true }, { toys: true }, { toysConnected: 'yes' }, { toysConnected: 1 }, { toysConnected: false }]) {
    assert.equal(readPlatform(desktop, caps).toys, false, JSON.stringify(caps));
  }
  const app = readPlatform(desktop, { toys: true, toysConnected: true });
  assert.equal(app.toys, true);
  for (const id of ['toy', 'buzz']) assert.equal(offerable(id, app), true, id);
  assert.equal(whyNot('toy', readPlatform(android)), 'Connect a toy in the CCP desktop app before you start.');
  assert.equal(whyNot('buzz', readPlatform(desktop)), 'Connect a toy in the CCP desktop app before you start.');
  assert.equal(whyNot('buzz', app), '');
});

test('no device row rides in a bundle or leads one: buying another row never hands it over', () => {
  for (const id of DEVICE_ROWS) {
    assert.equal(LEAD_OF[id], undefined, `${id} is a rider`);
    assert.equal(FOLD[id], undefined, `${id} leads a bundle`);
  }
});

test('Friend Remote is cut: no row, nothing requires it, and an old save that owns it still loads', () => {
  assert.equal(BY_ID.remote, undefined);
  for (const it of ITEMS) {
    assert.ok(!it.req.includes('remote'), `${it.id} requires remote`);
    assert.ok(!(it.any || []).includes('remote'), `${it.id} needs remote`);
  }
  const raw = JSON.stringify({ v: 2, run: { owned: { stray: 1, toy: 1, remote: 1 }, order: ['start', 'stray', 'toy', 'remote'] } });
  const { run } = Save.parse(raw);
  assert.equal(run.owned.remote, undefined, 'the cut row is dropped');
  assert.equal(run.owned.toy, 1, 'an owned toy row stays owned');
  assert.equal(run.owned.stray, 1);
});

test('Double Wielding needs a mouse: hidden on a touch-only phone, offered on a desktop', () => {
  assert.equal(offerable('dualwield', readPlatform(desktop)), true);
  assert.equal(offerable('dualwield', readPlatform(android)), false);
  assert.equal(offerable('dualwield', readPlatform(iphone)), false);
  assert.match(whyNot('dualwield', readPlatform(android)), /needs a mouse/);
  assert.equal(whyNot('dualwield', readPlatform(desktop)), '');
});

test('every other row is offered everywhere; copy is plain', () => {
  for (const it of ITEMS) if (!it.device) assert.equal(offerable(it, { toys: false, mouse: false }), true, it.id);
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
  const can = (it) => offerable(it, { toys: false, mouse: true });
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
