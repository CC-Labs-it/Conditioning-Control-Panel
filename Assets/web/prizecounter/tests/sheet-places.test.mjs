// Place marks: on the website or the phone an item that does not show there dims, wears the paper tab in mixed lists,
// its confirm says where it shows, Wear reads "Wear on desktop", and a Works here filter appears. Buying it is allowed.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fakeDocument } from './fake-dom.mjs';
import { createPreviewServer } from '../mock/preview.js';
import { createMockServer } from '../mock/server.js';
import { asHost } from '../host.js';
import { mountSheet } from '../sheet/main.js';

const SAMPLE = JSON.parse(readFileSync(new URL('../mock/state.sample.json', import.meta.url), 'utf8'));
const settle = async () => { for (let i = 0; i < 8; i++) await new Promise((r) => setTimeout(r, 0)); };
async function rig(server, init) {
  const calls = [];
  const host = asHost({ init: { motion: 'full', ...init }, request: (op, b, i) => { calls.push({ op, b }); return server.handle(op, b, i); }, wallet() {}, close() {}, wear() {} });
  const doc = fakeDocument(), root = doc.createElement('main');
  let T = 1e6;   // every read of the clock moves it on: these tests are about places, not the press guard
  const page = await mountSheet(host, { root, doc, win: { addEventListener() {}, removeEventListener() {} }, now: () => (T += 500), frame: (f) => f(), later() {}, loadIndex: () => null });
  const btn = (key) => page.view.root.all().find((x) => x.dataset && x.dataset.key === key) || null;
  const has = (n, cls) => n.className.split(' ').includes(cls);
  return { page, calls, btn, has, click: (key) => page.view.root.dispatch('click', { target: btn(key), detail: 1 }) };
}

test('on the website: shelf heads say where, away items dim, the fitting room says where it does show, buying still works', async () => {
  const r = await rig(createPreviewServer(SAMPLE), { platform: 'web', tab: 'effects' });
  const here = r.btn('here');
  assert.equal(here.hidden, false); assert.equal(here.text(), 'Works here');
  assert.match(r.page.view.list.all().find((n) => n.className === 'sh fam-bubble').text(), /Desktop app only/);
  assert.ok(r.has(r.btn('it:fx.bubble.heart'), 'elsewhere'));
  assert.ok(!r.btn('it:fx.bubble.heart').all().some((n) => n.className === 'pl away'), 'a shelf item leaves the mark to its shelf head');
  r.click('it:fx.bubble.heart');
  const room = r.page.view.room.text();
  assert.match(room, /Works on Desktop app Website Phone/);
  assert.match(room, /Shows in the desktop app, not on the website\. Buying it here still counts\./);
  r.click('act:buy');
  assert.match(r.page.view.room.text(), /Spend 360 Sparkles on this\? It shows in the desktop app\./);
  r.click('act:yes'); await settle();
  assert.equal(r.calls.filter((c) => c.op === 'shop-buy').length, 1);
  r.click('act:takeoff'); await settle();
  r.page.pick('fx.bubble.heart'); r.page.pick('fx.bubble.heart');
  assert.equal(r.btn('act:fxwear').text(), 'Wear on desktop');
  assert.match(r.page.view.room.text(), /Your desktop app picks up what you wear next time it opens\./);
});

test('Works here keeps only what shows on this host; a mixed list tabs each away item; the desktop has no filter', async () => {
  const r = await rig(createPreviewServer(SAMPLE), { platform: 'phone', tab: 'effects' });
  r.click('here');
  assert.equal(r.page.ui.hereOnly, true);
  assert.match(r.page.view.list.text(), /Nothing on this shelf shows on the phone\. Switch off Works here to see it all\./);
  r.click('tab:featured');
  assert.match(r.page.view.list.text(), /New this week The Bossy Ringmaster/);
  assert.match(r.page.view.list.text(), /Same wallet and same shelves as the desktop app\. Anything marked Desktop app only is bought here and worn there\./);
  r.click('here');
  r.page.view.search.value = 'duck'; r.page.view.search.dispatch('input', {});
  const duck = r.btn('it:desk.toy.rubber-duck');
  assert.ok(r.has(duck, 'elsewhere') && duck.all().some((n) => n.className === 'pl away'));
  const desk = await rig(createPreviewServer(SAMPLE), { platform: 'desktop', tab: 'effects' });
  assert.equal(desk.btn('here').hidden, true);
  assert.ok(!desk.has(desk.btn('it:fx.bubble.heart'), 'elsewhere'));
});

test('today\'s server body has no places: no marks, no Works on row, no filter', async () => {
  const r = await rig(createMockServer(SAMPLE), { platform: 'web', tab: 'desk' });
  assert.equal(r.btn('here').hidden, true);
  assert.ok(!r.page.view.list.all().some((n) => n.className && n.className.startsWith('pl')));
  r.click('it:desk.hat.party');
  assert.doesNotMatch(r.page.view.room.text(), /Works on/);
});
