// The 2D counter sheet's pure model: tabs from the state, one status per card face, sections, art, the fitting room.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { readState, readRow } from '../state/shop.js';
import { LEX } from '../state/strings.js';
import { SHEET_LEX, sheetT } from '../sheet/strings.js';
import { sheetTabs, sections, statusOf, statusText, artOf, roomModel, locate, slotOf, tagTier, OWNED } from '../sheet/model.js';

const SAMPLE = JSON.parse(readFileSync(new URL('../mock/state.sample.json', import.meta.url), 'utf8'));
const INDEX = JSON.parse(readFileSync(new URL('../assets/preview/index.json', import.meta.url), 'utf8'));
const t = sheetT({});
const row = (extra) => readRow({ id: 'desk.hat.party', priceSp: 360, sale: 'on', owned: null, ...extra });

test('tabs come from the state in house order, Owned last; Rain only with init.rainV2', () => {
  assert.deepEqual(sheetTabs(readState(SAMPLE, { rain: false }).tabs.map((x) => x.id)), ['kart', 'chess', 'profile', 'desk', 'prime', 'backroom', OWNED]);
  assert.deepEqual(sheetTabs(readState(SAMPLE, { rain: true }).tabs.map((x) => x.id)).slice(0, 2), ['rain-clothes', 'rain-hair']);
  assert.deepEqual(sheetTabs([]), [OWNED]);
});

test('every card face wears one status', () => {
  const wearing = new Map([['desk.hat', 'desk.hat.party']]);
  assert.equal(statusOf(row({ owned: { at: 'T', paidSp: 360 } }), 1000, 1).kind, 'owned');
  assert.equal(statusOf(row({ owned: { at: 'T', paidSp: 360 } }), 1000, 1, wearing).kind, 'wearing');
  assert.equal(statusOf(row({ sale: 'soon' }), 1000, 1).kind, 'soon');
  assert.equal(statusOf(row({ needs: 'prime' }), 1000, 1).kind, 'prime');
  assert.deepEqual(statusOf(row({ needs: 'prime' }), 1000, 2), { kind: 'price', price: 360, tier: 't2', short: false });
  assert.equal(statusOf(row({ needs: 'discord' }), 1000, 2).kind, 'discord');
  assert.deepEqual(statusOf(row({}), 100, 1), { kind: 'price', price: 360, tier: 't2', short: true });
  assert.deepEqual([tagTier(120), tagTier(360), tagTier(730), tagTier(2540)], ['t1', 't2', 't3', 't3']);
  const words = ['wearing', 'owned', 'prime', 'soon', 'discord'].map((kind) => statusText({ kind }, t));
  assert.deepEqual(words, ['Wearing', 'Owned', 'Prime only', 'On the shelf soon.', 'Link Discord first']);
  assert.equal(statusText({ kind: 'price', price: 730 }, t), '730 Sparkles');
});

test('a tab is one section with a plank per server shelf; Owned groups by shelf; search finds by name', () => {
  const st = readState(SAMPLE, { rain: false });
  const kart = sections(st, { tab: 'kart', t });
  assert.equal(kart.length, 1);
  assert.equal(kart[0].title, 'Kart');
  assert.equal(kart[0].planks.length, st.tabs.find((x) => x.id === 'kart').shelves.filter((s) => s.rows.some(Boolean)).length);
  const owned = sections(st, { tab: OWNED, t });
  assert.ok(owned.length >= 2);
  assert.ok(owned.every((s) => s.planks[0].every((e) => e.row.owned)));
  const q = sections(st, { tab: 'kart', query: 'duck', t });
  assert.deepEqual(q[0].planks[0].map((e) => e.row.id), ['desk.toy.rubber-duck']);
  assert.equal(sections(st, { tab: 'kart', query: 'zzz', t })[0].empty, 'Nothing on the shelves matches that.');
  const none = readState({ ...SAMPLE, tabs: [{ id: 'kart', shelves: [{ rows: [{ id: 'kart.body.petal', priceSp: 9, sale: 'on' }] }] }] });
  assert.equal(sections(none, { tab: OWNED, t })[0].empty, 'Nothing owned yet.');
});

test('art: flat pictures where the tree has them, the family glyph for 3D-only items', () => {
  assert.deepEqual(artOf('profile.frame.lace', INDEX, 'profile'), { kind: 'card', src: './assets/preview/profile/profile.frame.lace.png' });
  assert.equal(artOf('desk.hat.party', INDEX, 'desk', '../').src, '../assets/preview/desk/desk.hat.party.png');
  assert.equal(artOf('desk.hat.party', INDEX, 'desk').base, './assets/preview/desk/emi-body-idle.png');
  assert.equal(artOf('kart.paint.pattern-bundle', INDEX, 'kart').kind, 'swatch');
  assert.deepEqual(artOf('kart.body.petal', INDEX, 'kart'), { kind: 'glyph', family: 'kart' });
  assert.deepEqual(artOf('jackpot_remix', null, 'backroom'), { kind: 'glyph', family: 'backroom' });
});

test('the fitting room: Buy asks twice, pending locks the Yes, owned rows wear by the wear mode', () => {
  const card = { itemId: 'desk.hat.party', priceSp: 360, face: 'buy', action: 'buy', noteKey: null, pending: false };
  const ids = (m) => m.actions.map((a) => a.id);
  assert.equal(roomModel(null, { sp: 0, t }), null);
  const first = roomModel(card, { sp: 1000, t });
  assert.deepEqual(ids(first), ['buy', 'back']);
  assert.equal(first.ask, null);
  assert.ok(first.lines.some((l) => l.text === 'Balance after: 640'));
  const ask = roomModel(card, { sp: 1000, t, asking: true });
  assert.deepEqual(ask.actions.map((a) => a.label), ['Yes, buy for 360', 'Cancel']);
  assert.equal(ask.ask, 'Spend 360 Sparkles on this?');
  const pending = roomModel({ ...card, pending: true }, { sp: 1000, t, asking: true });
  assert.ok(pending.actions.every((a) => a.disabled));
  assert.equal(pending.note, 'Ringing it up.');
  const owned = { ...card, face: 'owned', action: 'wear', wear: 'wear' };
  assert.deepEqual(ids(roomModel(owned, { sp: 0, t })), ['wear', 'back']);
  assert.deepEqual(ids(roomModel(owned, { sp: 0, t, wearing: new Map([[slotOf(card.itemId), card.itemId]]) })), ['back']);
  assert.deepEqual(ids(roomModel({ ...owned, itemId: 'kart.body.petal', wear: 'garage' }, { sp: 0, t })), ['garage']);
  assert.deepEqual(ids(roomModel({ ...owned, itemId: 'jackpot_remix', wear: null }, { sp: 0, t })), ['back']);
  for (const face of ['prime', 'discord', 'short', 'soon', 'gone']) assert.deepEqual(ids(roomModel({ ...card, face, action: null, short: 5 }, { sp: 0, t })), ['back'], face);
  assert.equal(locate(readState(SAMPLE), 'desk.hat.party').tab, 'desk');
});

test('the sheet adds its words beside LEX, never over it, in the house voice', () => {
  for (const [k, v] of Object.entries(SHEET_LEX)) {
    assert.ok(k.startsWith('shop_sheet_') && !(k in LEX), k);
    assert.doesNotMatch(v, /[–—!]/, k);
  }
  assert.equal(sheetT({ shop_sheet_cancel: 'Annulla' })('shop_sheet_cancel'), 'Annulla');
});
