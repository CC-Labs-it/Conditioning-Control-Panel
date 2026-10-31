// The confirm card: every face renders the right buttons and lines, pending locks the buy, the card traps focus.
import test from 'node:test';
import assert from 'node:assert/strict';
import { cardModel, renderCard } from '../hud/card.js';
import { createHud } from '../hud/index.js';
import { makeT, itemDesc, LEX } from '../state/strings.js';
import { fakeDocument } from './fake-dom.mjs';

const t = makeT({});
const base = { itemId: 'rain.dress.scarlet-bell', priceSp: 730, noteKey: null, pending: false, retry: false };
const CARDS = {
  buy: { ...base, face: 'buy', action: 'buy' },
  owned: { ...base, face: 'owned', action: 'wear' },
  prime: { ...base, face: 'prime', action: 'prime', reasonKey: 'shop_prime_reason' },
  short: { ...base, face: 'short', action: null, short: 680 },
  soon: { ...base, face: 'soon', action: null },
  gone: { ...base, face: 'gone', action: null },
};
const draw = (card, sp = 1850) => {
  const sent = [], doc = fakeDocument();
  const el = renderCard(doc, cardModel(card, sp, t), (i) => sent.push(i));
  return { el, sent, buttons: el.querySelectorAll('button') };
};

test('every face shows its buttons and nothing more', () => {
  const want = { buy: ['Buy', 'Keep looking'], owned: ['Wear', 'Keep looking'], prime: ['Keep looking'], short: ['Keep looking'], soon: [], gone: [] };
  for (const [face, labels] of Object.entries(want)) {
    const { el, buttons } = draw(CARDS[face]);
    assert.deepEqual(buttons.map((b) => b.textContent), labels, face);
    assert.equal(el.getAttribute('role'), 'dialog', face);
    assert.match(el.text(), /The Flustered Frock/, face);
  }
});

test('buy shows the price and the balance after; its main button selects, keep looking goes back', () => {
  const { el, sent, buttons } = draw(CARDS.buy);
  assert.match(el.text(), /730/);
  assert.match(el.text(), /Balance after: 1120/);
  buttons.forEach((b) => b.onclick());
  assert.deepEqual(sent, [{ type: 'select' }, { type: 'back' }]);
});

test('short shows the price in red ink and the shortfall', () => {
  const { el } = draw(CARDS.short, 50);
  const price = el.querySelectorAll('.pc-card-price')[0];
  assert.ok(price.classList.contains('pc-red'));
  assert.match(el.text(), /Short by 680 Sparkles/);
});

test('owned stamps OWNED, prime shows the lock badge and the reason, soon and gone say so', () => {
  assert.ok(draw(CARDS.owned).el.querySelectorAll('.pc-stamp-owned').length);
  assert.equal(draw(CARDS.owned).el.querySelectorAll('.pc-card-price').length, 0);
  const prime = draw(CARDS.prime).el;
  assert.ok(prime.querySelectorAll('.pc-stamp-prime')[0].innerHTML.includes('pc-lock'));
  assert.match(prime.text(), /Prime members can buy this one/);
  assert.match(draw(CARDS.soon).el.text(), /On the shelf soon/);
  assert.match(draw(CARDS.gone).el.text(), /Gone from this week's shelf/);
});

test('pending: aria-busy, the buy button disabled, the note says so; a note key shows its line', () => {
  const { el, buttons } = draw({ ...CARDS.buy, pending: true });
  assert.equal(el.getAttribute('aria-busy'), 'true');
  assert.deepEqual(buttons.map((b) => b.disabled), [true, false]);
  assert.match(el.text(), /Ringing it up/);
  assert.match(draw({ ...CARDS.buy, noteKey: 'shop_note_retry' }).el.text(), /Confirm again to retry/);
});

test('the HUD opens the card with focus on the card (no default action), traps Tab, Esc keeps looking, and closes it', () => {
  const doc = fakeDocument(), root = doc.createElement('main'), sent = [];
  const hud = createHud(root, { t, send: (i) => sent.push(i), goTab: () => {}, motion: 'off' });
  const view = (card) => ({ status: 'ready', sp: 1850, week: '2026-W44', tier: 1, tabs: ['rain-clothes', 'kart'], tabId: 'kart',
    shelves: [], focus: { bay: 0, slot: 0, row: { id: base.itemId, priceSp: 730, sale: 'on', owned: null } }, card });
  hud.paint(view(CARDS.buy));
  const modal = root.querySelectorAll('.pc-modal')[0];
  assert.equal(modal.hidden, false);
  assert.ok(doc.activeElement.classList.contains('pc-card'), 'the card itself, never Buy');
  modal.dispatch('keydown', { key: 'Tab' });
  assert.equal(doc.activeElement.textContent, 'Buy');
  modal.dispatch('keydown', { key: 'Tab' });
  assert.equal(doc.activeElement.textContent, 'Keep looking');
  modal.dispatch('keydown', { key: 'Tab' });
  assert.equal(doc.activeElement.textContent, 'Buy');
  modal.dispatch('keydown', { key: 'Escape' });
  assert.deepEqual(sent, [{ type: 'back' }]);
  assert.match(root.text(), /wk 44/);
  assert.equal(root.querySelectorAll('.pc-tab').find((b) => b.getAttribute('aria-selected') === 'true').dataset.tab, 'kart');
  hud.paint(view(null));
  assert.equal(modal.hidden, true);
  assert.notEqual(doc.activeElement.textContent, 'Buy');
});

test('signed out: one Sign in to shop button, no wallet, no tabs; it asks for the sign-in and leaves when signed in', () => {
  const doc = fakeDocument(), root = doc.createElement('main');
  let asked = 0;
  const hud = createHud(root, { t, send: () => {}, goTab: () => {}, signIn: () => asked++, motion: 'off' });
  const view = (status) => ({ status, sp: 0, week: '', tier: 0, tabs: [], tabId: null, shelves: [], focus: null, card: null });
  hud.paint(view('signed_out'));
  const gate = root.querySelectorAll('.pc-gate')[0];
  assert.equal(gate.hidden, false);
  const [btn] = gate.querySelectorAll('button');
  assert.equal(btn.textContent, 'Sign in to shop');
  assert.equal(root.querySelectorAll('.pc-wallet')[0].hidden, true);
  assert.equal(root.querySelectorAll('.pc-status')[0].hidden, true, 'the button is the whole message');
  btn.onclick();
  assert.equal(asked, 1);
  hud.paint(view('ready'));
  assert.equal(gate.hidden, true);
  assert.equal(root.querySelectorAll('.pc-wallet')[0].hidden, false);
});

test('the card carries the item description: its own key, else the family + slot key, else none', () => {
  assert.match(draw(CARDS.buy).el.text(), /Scarlet bell dress for Emi\./);
  assert.equal(draw(CARDS.buy).el.querySelectorAll('.pc-card-desc').length, 1);
  assert.match(draw({ ...CARDS.owned, itemId: 'kart.body.petal' }).el.text(), /A kart body for EMI/);
  assert.equal(itemDesc(t, 'kart.body.velvet-lock'), itemDesc(t, 'kart.body.petal'));
  assert.equal(itemDesc(t, 'kart.paint.hearts'), null);
  assert.equal(draw({ ...CARDS.soon, itemId: 'kart.paint.hearts' }).el.querySelectorAll('.pc-card-desc').length, 0);
  assert.equal(itemDesc(makeT({ shop_item_rain_dress_teal_tulip_desc: 'Lexicon wins.' }), 'rain.dress.teal-tulip'), 'Lexicon wins.');
});

test('{companion} is the mod name from init.companion, Emi without one (web, phone); "Rain" never shows', () => {
  assert.equal(makeT({}, 'Bambi')('shop_tab_rain_clothes'), 'Bambi: clothes');
  assert.equal(makeT({}, 'Bimbo')('shop_item_rain_dress_scarlet_bell_desc'), 'Scarlet bell dress for Bimbo.');
  assert.equal(makeT({})('shop_tab_rain_hair'), 'Emi: hair');
  assert.equal(makeT({}, '   ')('shop_item_profile_avatar_rain_portrait_name'), 'Emi Portrait');
  assert.equal(makeT({ shop_tab_rain_hair: '{companion}: Haare' }, 'Bambi')('shop_tab_rain_hair'), 'Bambi: Haare');
  for (const key of Object.keys(LEX)) assert.doesNotMatch(makeT({}, 'Bambi')(key), /\bRain\b/, key);
});
