// Fix wave F1: an owned card's Wear routes by family with the host's init.wears; a kart outside the garage reads
// "Wear it in the garage" and only steps back; Back Room prizes have nothing to wear; a host with no list keeps Wear.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { wearFamily, wearMode, wearHint, createShop } from '../state/shop.js';
import { cardModel, renderCard } from '../hud/card.js';
import { makeT } from '../state/strings.js';
import { fakeDocument } from './fake-dom.mjs';

const DESKTOP = ['rain', 'desk', 'chess', 'profile'];
const GARAGE = [...DESKTOP, 'kart'];

test('wearFamily: every shop family by id prefix, Back Room prizes none', () => {
  const want = {
    'rain.dress.rose-flare': 'rain', 'desk.hat.party': 'desk', 'desk.toy.rubber-duck': 'desk',
    'chess.set.royal-capes': 'chess', 'chess.insignia.star': 'chess', 'prime.banner.arena': 'chess',
    'profile.frame.lace': 'profile', 'profile.avatar.emi-pixel': 'profile', 'prime.frame.room-host': 'profile',
    'kart.outfit.pit-crew': 'kart', 'kart.umbrella.lace': 'kart', 'prime.paint.host-gold': 'kart', 'prime.trail.podium': 'kart',
    jackpot_remix: null, rt_demo: null, '': null,
  };
  for (const [id, fam] of Object.entries(want)) assert.equal(wearFamily(id), fam, id);
});

test('wearMode: the host list decides; a kart away from the garage is the garage note; no list keeps Wear', () => {
  assert.equal(wearMode('desk.hat.party', DESKTOP), 'wear');
  assert.equal(wearMode('kart.outfit.pit-crew', DESKTOP), 'garage');
  assert.equal(wearMode('kart.outfit.pit-crew', GARAGE), 'wear');
  assert.equal(wearMode('jackpot_remix', DESKTOP), null);
  assert.equal(wearMode('chess.set.royal-capes', ['rain']), null);
  assert.equal(wearMode('jackpot_remix', undefined), 'wear');
});

test('owned card buttons follow the mode: Wear + Keep looking, the garage button steps back, nothing to wear keeps looking', () => {
  const t = makeT({});
  const card = (wear) => ({ itemId: 'kart.outfit.pit-crew', priceSp: 10, face: 'owned', action: 'wear', wear, noteKey: null, pending: false });
  const labels = (wear) => {
    const sent = [];
    const el = renderCard(fakeDocument(), cardModel(card(wear), 100, t), (i) => sent.push(i));
    const buttons = el.querySelectorAll('button');
    buttons.forEach((b) => b.onclick());
    return [buttons.map((b) => b.textContent), sent.map((s) => s.type)];
  };
  assert.deepEqual(labels(undefined), [['Wear', 'Keep looking'], ['select', 'back']]);
  assert.deepEqual(labels('wear'), [['Wear', 'Keep looking'], ['select', 'back']]);
  assert.deepEqual(labels('garage'), [['Wear it in the garage'], ['back']]);
  assert.deepEqual(labels(null), [['Keep looking'], ['back']]);
});

test('an in-place Wear notes "Wearing it now."; the host refusing it (worn ok:false) turns the note into a retry', async () => {
  const owned = { at: 'T', paidSp: 10 };
  const state = { ok: true, sp: 50, tier: 1, week: '2026-W44', catalogVersion: 'cv', prizes: { revision: 1, grants: [] },
    tabs: [{ id: 'desk', shelves: [{ rows: [{ id: 'desk.hat.party', tier: 'S', priceSp: 10, grants: ['cos.desk.hat.party'], owned, sale: 'on' }] }] }] };
  const shop = createShop({ request: async () => state, newIdem: () => 'idem-000000000001', wears: DESKTOP });
  await shop.load();
  shop.intent({ type: 'pick', bay: 0, slot: 0 });
  if (!shop.card()) shop.intent({ type: 'select' });
  const ch = shop.intent({ type: 'select' });
  assert.deepEqual(ch.wear, { itemId: 'desk.hat.party' });
  assert.equal(ch.card.noteKey, 'shop_worn');
  assert.deepEqual(shop.worn('desk.hat.party', true), {});
  assert.deepEqual(shop.worn('desk.toy.other', false), {});
  assert.equal(shop.worn('desk.hat.party', false).card.noteKey, 'shop_note_try_again');
  assert.deepEqual(shop.worn('desk.hat.party', false), {}, 'once');
});

const WEB = ['chess', 'profile'];
const deskState = (owned) => ({ ok: true, sp: 50, tier: 1, week: '2026-W44', catalogVersion: 'cv', prizes: { revision: 1, grants: [] },
  tabs: [{ id: 'desk', shelves: [{ rows: [{ id: 'desk.hat.party', tier: 'S', priceSp: 10, grants: ['cos.desk.hat.party'], ...(owned ? { owned: { at: 'T', paidSp: 10 } } : {}), sale: 'on' }] }] }] });
async function openDeskCard(wears, owned = true) {
  const shop = createShop({ request: async () => deskState(owned), newIdem: () => 'idem-000000000001', wears });
  await shop.load();
  shop.intent({ type: 'pick', bay: 0, slot: 0 });
  if (!shop.card()) shop.intent({ type: 'select' });
  return shop;
}
const drawn = (card) => {
  const el = renderCard(fakeDocument(), cardModel(card, 50, makeT({})), () => {});
  return { buttons: el.querySelectorAll('button').map((b) => b.textContent), text: JSON.stringify(cardModel(card, 50, makeT({})).lines) };
};
const DESK_LINE = "Shows on EMI's desk in the desktop app";

test('wearHint: only a desk row, only on a host whose list lacks desk; no list fails closed', () => {
  assert.equal(wearHint('desk.hat.party', WEB), 'shop_desk_on_desktop');
  assert.equal(wearHint('desk.toy.rubber-duck', []), 'shop_desk_on_desktop');
  assert.equal(wearHint('desk.hat.party', DESKTOP), null);
  assert.equal(wearHint('desk.hat.party', undefined), null);
  assert.equal(wearHint('desk.hat.party', null), null);
  for (const id of ['chess.set.royal-capes', 'profile.frame.lace', 'kart.outfit.pit-crew', 'rain.dress.rose-flare', 'jackpot_remix'])
    assert.equal(wearHint(id, WEB), null, id);
});

test('an owned desk row off the desktop: the line shows, no Wear, and a select sends nothing to wear', async () => {
  const shop = await openDeskCard(WEB);
  const d = drawn(shop.card());
  assert.deepEqual(d.buttons, ['Keep looking']);
  assert.match(d.text, new RegExp(DESK_LINE));
  assert.deepEqual(shop.intent({ type: 'select' }), {});
});

test('the desktop is unchanged: list with desk or no list at all means Wear and no line', async () => {
  for (const wears of [DESKTOP, undefined]) {
    const shop = await openDeskCard(wears);
    const d = drawn(shop.card());
    assert.deepEqual(d.buttons, ['Wear', 'Keep looking']);
    assert.equal('hintKey' in shop.card(), false);
    assert.doesNotMatch(d.text, /desktop app/);
  }
});

test('a desk row not owned yet keeps its buy card with no line', async () => {
  const shop = await openDeskCard(WEB, false);
  assert.equal(shop.card().face, 'buy');
  assert.doesNotMatch(drawn(shop.card()).text, /desktop app/);
});
