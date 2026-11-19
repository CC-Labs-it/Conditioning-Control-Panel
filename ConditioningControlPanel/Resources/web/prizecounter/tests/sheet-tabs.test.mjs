// The tab bar the owner asked for (2026-11-17): Featured, Karts, Chess, Bubbles and effects, Accessories, Prime only,
// Back Room, Owned. Each of the four new tabs wears its emblem; doors and old links (games, you, effects) land right.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fakeDocument } from './fake-dom.mjs';
import { createPreviewServer } from '../mock/preview.js';
import { createMockServer } from '../mock/server.js';
import { asHost } from '../host.js';
import { mountSheet } from '../sheet/main.js';
import { TAB_EMBLEMS, tabEmblemSvg, tabEmblem } from '../sheet/tab-art.js';

const SAMPLE = JSON.parse(readFileSync(new URL('../mock/state.sample.json', import.meta.url), 'utf8'));
async function rig(server, init = {}) {
  const host = asHost({ init: { motion: 'full', ...init }, request: (op, b, i) => server.handle(op, b, i), wallet() {}, close() {}, wear() {} });
  const doc = fakeDocument(), root = doc.createElement('main');
  let T = 1e6;
  const page = await mountSheet(host, { root, doc, win: { addEventListener() {}, removeEventListener() {} }, now: () => (T += 500), frame: (f) => f(), later() {}, loadIndex: () => null });
  const tabs = () => page.view.root.all().filter((x) => x.dataset && String(x.dataset.key || '').startsWith('tab:'));
  const btn = (key) => page.view.root.all().find((x) => x.dataset && x.dataset.key === key) || null;
  return { page, tabs, btn, click: (key) => page.view.root.dispatch('click', { target: btn(key), detail: 1 }) };
}
const cls = (n) => n.className.split(' ');
const kids = (n) => n.children || [];

test('the preview tab bar reads Featured, Karts, Chess, Bubbles and effects, Accessories, Prime only, Back Room, Owned', async () => {
  const r = await rig(createPreviewServer(SAMPLE));
  assert.deepEqual(r.tabs().map((b) => b.dataset.key),
    ['tab:featured', 'tab:kart', 'tab:chess', 'tab:effects', 'tab:accessories', 'tab:prime', 'tab:backroom', 'tab:owned']);
  const words = r.tabs().map((b) => kids(b).filter((c) => c.tagName === 'SPAN' && !cls(c).includes('lbl-short') && !cls(c).includes('tab-emb') && !cls(c).includes('cnt')).map((c) => c.textContent).join(''));
  assert.deepEqual(words, ['Featured', 'Karts', 'Chess', 'Bubbles and effects', 'Accessories', 'Prime only', 'Back Room', 'Owned']);
});

test('the four new tabs wear their emblem before the word; the others wear none', async () => {
  const r = await rig(createPreviewServer(SAMPLE));
  for (const b of r.tabs()) {
    const id = b.dataset.key.slice(4), emb = kids(b).find((c) => cls(c).includes('tab-emb'));
    if (['kart', 'chess', 'effects', 'accessories'].includes(id)) {
      assert.ok(emb, id);
      assert.equal(kids(b)[0], emb, id + ' emblem first');
      assert.ok(cls(emb).includes('te-' + id), id);
      assert.equal(emb.getAttribute('aria-hidden'), 'true');
      assert.match(emb.innerHTML, /<svg/);
    } else assert.equal(emb, undefined, id);
  }
  assert.deepEqual([...TAB_EMBLEMS].sort(), ['accessories', 'chess', 'effects', 'kart']);
  assert.equal(tabEmblemSvg('owned'), null);
  assert.equal(tabEmblem(fakeDocument(), 'prime'), null);
});

test('Bubbles and effects carries its short word for a narrow bar and keeps its full name for screen readers', async () => {
  const r = await rig(createPreviewServer(SAMPLE));
  const fx = r.btn('tab:effects'), short = kids(fx).find((c) => cls(c).includes('lbl-short'));
  assert.equal(short.textContent, 'Effects');
  assert.equal(short.getAttribute('aria-hidden'), 'true');
  assert.equal(fx.getAttribute('aria-label'), 'Bubbles and effects');
  assert.equal(r.btn('tab:kart').getAttribute('aria-label'), null);
});

test('doors land on their tab: kart opens Karts, chess opens Chess, desk and profile open Accessories', async () => {
  for (const [tab, want] of [['kart', 'kart'], ['chess', 'chess'], ['desk', 'accessories'], ['profile', 'accessories'], ['backroom', 'backroom'], ['effects', 'effects']]) {
    const r = await rig(createPreviewServer(SAMPLE), { tab });
    assert.equal(r.page.ui.tab, want, tab);
  }
});

test('old tab names still work: games opens Karts, you opens Accessories', async () => {
  assert.equal((await rig(createPreviewServer(SAMPLE), { tab: 'games' })).page.ui.tab, 'kart');
  assert.equal((await rig(createPreviewServer(SAMPLE), { tab: 'you' })).page.ui.tab, 'accessories');
  assert.equal((await rig(createMockServer(SAMPLE), { tab: 'games' })).page.ui.tab, 'kart');
});

test('init.pick opens the right tab: a kart on Karts, a chess look on Chess, a bubble skin on Bubbles and effects, a frame on Accessories', async () => {
  for (const [pick, want] of [['kart.body.petal', 'kart'], ['chess.set.royal-capes', 'chess'], ['fx.bubble.heart', 'effects'], ['profile.frame.lace', 'accessories'], ['desk.toy.rubber-duck', 'accessories']]) {
    const r = await rig(createPreviewServer(SAMPLE), { pick });
    assert.equal(r.page.ui.tab, want, pick);
  }
});

test('Karts holds only kart shelves, Chess only chess shelves; a click on a tab shows its own', async () => {
  const r = await rig(createPreviewServer(SAMPLE), { tab: 'kart' });
  const secs = () => r.page.view.list.children.map((s) => s.className);
  assert.deepEqual(secs(), ['sec fam-kart']);
  r.click('tab:chess');
  assert.deepEqual(secs(), ['sec fam-chess']);
  r.click('tab:accessories');
  assert.deepEqual(secs(), ['sec fam-profile', 'sec fam-desk']);
  r.click('tab:effects');
  assert.ok(secs().every((c) => /fam-(bubble|card|behind|fog|word)/.test(c)), secs().join());
});
