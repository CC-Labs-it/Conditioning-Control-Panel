// The 2D counter sheet's pure model: tabs from the state, one status per card face, sections, art, the fitting room.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { readState, readRow } from '../state/shop.js';
import { LEX } from '../state/strings.js';
import { SHEET_LEX, sheetT } from '../sheet/strings.js';
import { sheetTabs, sections, statusOf, statusText, artOf, roomModel, locate, slotOf, tagTier, OWNED, FEATURED, EFFECTS, TAB_ALIAS, groupOf, tabLabelKey, tabShortKey } from '../sheet/model.js';
import { readPreview } from '../sheet/wire.js';

const SAMPLE = JSON.parse(readFileSync(new URL('../mock/state.sample.json', import.meta.url), 'utf8'));
const INDEX = JSON.parse(readFileSync(new URL('../assets/preview/index.json', import.meta.url), 'utf8'));
const t = sheetT({});
const row = (extra) => readRow({ id: 'desk.hat.party', priceSp: 360, sale: 'on', owned: null, ...extra });
const S = (r, sp, tier, wearing) => statusOf({ row: r, x: {}, fx: null }, { sp, tier, wearing });

test('tabs come from the state grouped as the owner asked, Owned last; Rain only with init.rainV2', () => {
  assert.deepEqual(sheetTabs(readState(SAMPLE, { rain: false }).tabs.map((x) => x.id)), ['kart', 'chess', 'accessories', 'prime', 'backroom', OWNED]);
  // Rain's tabs come after the four lead tabs, in the state's own order
  assert.deepEqual(sheetTabs(readState(SAMPLE, { rain: true }).tabs.map((x) => x.id)).slice(0, 5), ['kart', 'chess', 'accessories', 'rain-clothes', 'rain-hair']);
  assert.deepEqual(sheetTabs([]), [OWNED]);
});

test('Karts, Chess, Bubbles and effects, Accessories lead in that order whatever order the state sends', () => {
  const pv = { ...readPreview(null), front: true, fx: [{ slot: 'bubble', row: { id: 'fx.bubble.heart' } }] };
  assert.deepEqual(sheetTabs(['backroom', 'desk', 'prime', 'chess', 'profile', 'kart'], pv), [FEATURED, 'kart', 'chess', EFFECTS, 'accessories', 'backroom', 'prime', OWNED]);
});

test('the old tab names are aliases: games opens Karts, you opens Accessories, effects stays Bubbles and effects', () => {
  assert.equal(TAB_ALIAS.games, 'kart'); assert.equal(TAB_ALIAS.you, 'accessories');
  assert.equal(groupOf('games'), 'kart'); assert.equal(groupOf('you'), 'accessories'); assert.equal(groupOf('effects'), EFFECTS);
  assert.equal(groupOf('profile'), 'accessories'); assert.equal(groupOf('desk'), 'accessories');
  assert.equal(groupOf('kart'), 'kart'); assert.equal(groupOf('chess'), 'chess'); assert.equal(groupOf('backroom'), 'backroom');
  const st = readState(SAMPLE, { rain: false });
  assert.deepEqual(sections(st, { tab: 'games', t }).map((s) => s.key), sections(st, { tab: 'kart', t }).map((s) => s.key));
  assert.deepEqual(sections(st, { tab: 'you', t }).map((s) => s.key), ['profile', 'desk']);
  assert.equal(tabLabelKey('kart'), 'shop_sheet_tab_karts'); assert.equal(tabLabelKey('accessories'), 'shop_sheet_tab_accessories');
  assert.equal(t(tabLabelKey(EFFECTS)), 'Bubbles and effects'); assert.equal(t(tabShortKey(EFFECTS)), 'Effects');
  assert.equal(tabShortKey('kart'), null);
});

test('every card face wears one status', () => {
  const wearing = new Map([['desk.hat', 'desk.hat.party']]);
  assert.equal(S(row({ owned: { at: 'T', paidSp: 360 } }), 1000, 1).kind, 'owned');
  assert.equal(S(row({ owned: { at: 'T', paidSp: 360 } }), 1000, 1, wearing).kind, 'wearing');
  assert.equal(S(row({ sale: 'soon' }), 1000, 1).kind, 'soon');
  assert.equal(S(row({ needs: 'prime' }), 1000, 1).kind, 'prime');
  assert.deepEqual(S(row({ needs: 'prime' }), 1000, 2), { kind: 'price', price: 360, tier: 't2', short: false });
  assert.equal(S(row({ needs: 'discord' }), 1000, 2).kind, 'discord');
  assert.deepEqual(S(row({}), 100, 1), { kind: 'price', price: 360, tier: 't2', short: true });
  assert.deepEqual([tagTier(120), tagTier(360), tagTier(730), tagTier(2540)], ['t1', 't2', 't3', 't3']);
  const words = ['wearing', 'owned', 'prime', 'soon', 'discord', 'on', 'todo'].map((kind) => statusText({ kind }, t));
  assert.deepEqual(words, ['Wearing', 'Owned', 'Prime only', 'On the shelf soon.', 'Link Discord first', 'Switched on', 'Not out yet']);
  assert.equal(statusText({ kind: 'price', price: 730 }, t), '730 Sparkles');
});

test('a group tab is one section per state tab with a plank per server shelf; Owned groups by shelf; search finds by name', () => {
  const st = readState(SAMPLE, { rain: false });
  const karts = sections(st, { tab: 'kart', t });
  assert.deepEqual(karts.map((s) => s.title), ['Kart skins']);
  assert.equal(karts[0].meta, 'Racing Thoughts');
  assert.equal(karts[0].planks.length, st.tabs.find((x) => x.id === 'kart').shelves.filter((s) => s.rows.some(Boolean)).length);
  const chess = sections(st, { tab: 'chess', t });
  assert.deepEqual(chess.map((s) => s.key), ['chess']);
  assert.equal(chess[0].planks.length, st.tabs.find((x) => x.id === 'chess').shelves.filter((s) => s.rows.some(Boolean)).length);
  assert.deepEqual(sections(st, { tab: 'accessories', t }).map((s) => s.title), ['Profile frames', 'Desk toys']);
  const owned = sections(st, { tab: OWNED, t });
  assert.ok(owned.length >= 2);
  assert.ok(owned.every((s) => s.planks[0].every((e) => e.row.owned)));
  const q = sections(st, { tab: 'games', query: 'duck', t });
  assert.deepEqual(q[0].planks[0].map((e) => e.row.id), ['desk.toy.rubber-duck']);
  assert.equal(q[0].title, '1 found');
  assert.equal(sections(st, { tab: 'games', query: 'zzz', t })[0].empty, 'Nothing on the shelves matches that.');
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
  assert.equal(first.actions[1].label, 'Put it back');
  assert.equal(first.ask, null);
  assert.ok(first.lines.some((l) => l.text === 'Balance after: 640'));
  const ask = roomModel(card, { sp: 1000, t, asking: true });
  assert.deepEqual(ask.actions.map((a) => a.label), ['Yes, buy for 360', 'Cancel']);
  assert.equal(ask.ask, 'Spend 360 Sparkles on this?');
  assert.equal(roomModel(card, { sp: 1000, t, asking: true, away: true }).ask, 'Spend 360 Sparkles on this? It shows in the desktop app.');
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
    assert.ok((k.startsWith('shop_sheet_') || k.startsWith('shop_item_fx_')) && !(k in LEX), k);
    assert.doesNotMatch(v, /[–—!]/, k);
  }
  assert.equal(sheetT({ shop_sheet_cancel: 'Annulla' })('shop_sheet_cancel'), 'Annulla');
});
