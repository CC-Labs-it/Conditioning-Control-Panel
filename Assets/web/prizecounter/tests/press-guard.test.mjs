// Stray keys never spend (desk bug 2026-10-30): a held or repeated key never buys, a key pressed before the counter
// had focus never buys, the confirm step ignores presses for a moment, and a deliberate press after the guard does.
import test from 'node:test';
import assert from 'node:assert/strict';
import { createPressGuard, keyName, FOCUS_MS, CARD_MS } from '../state/press-guard.js';
import { createHud } from '../hud/index.js';
import { cardModel } from '../hud/card.js';
import { makeT } from '../state/strings.js';
import { fakeDocument } from './fake-dom.mjs';

const clock = (t0 = 0) => { let t = t0; return { now: () => t, go: (ms) => { t += ms; } }; };
const tap = (g, key = 'Space') => { g.down(key); return g.up(key); };

test('a deliberate press and release after the focus wait confirms', () => {
  const c = clock(), g = createPressGuard({ now: c.now, focused: true });
  c.go(FOCUS_MS);
  assert.equal(tap(g), true);
  assert.equal(tap(g, 'Enter'), true);
  assert.equal(tap(g, 'pad:0'), true);
});

test('a press inside the focus wait never confirms, even released later', () => {
  const c = clock(), g = createPressGuard({ now: c.now, focused: true });
  c.go(FOCUS_MS - 50);
  g.down('Space');
  c.go(500);
  assert.equal(g.up('Space'), false);
  assert.equal(tap(g), true, 'the next press is fine');
});

test('a key held from before focus never confirms: its release, its repeats, the first press after focus', () => {
  const c = clock(), g = createPressGuard({ now: c.now, focused: false });
  g.down('Space');                       // typed into another app: the counter is not focused
  c.go(100); g.focus(true);
  c.go(FOCUS_MS + 200);
  assert.equal(g.up('Space'), false, 'released after focus: no press began here');
  g.down('Enter', true);                 // auto-repeat arriving in the newly focused window
  c.go(30); g.down('Enter', true);
  assert.equal(g.up('Enter'), false, 'a repeat-only press never confirms');
  assert.equal(g.up('Space'), false, 'a release with no press never confirms');
});

test('a held key that repeats never confirms, even after the wait', () => {
  const c = clock(), g = createPressGuard({ now: c.now, focused: true });
  c.go(FOCUS_MS + 10);
  g.down('Space');
  c.go(500); g.down('Space', true); g.down('Space', true);
  assert.equal(g.up('Space'), false);
});

test('a lost focus event: the first key proves focus but never confirms itself', () => {
  const c = clock(1000), g = createPressGuard({ now: c.now, focused: false });
  assert.equal(tap(g), false);
  c.go(FOCUS_MS);
  assert.equal(tap(g), true);
  const p = createPressGuard({ now: c.now, focused: false });
  c.go(FOCUS_MS); assert.equal(tap(p, 'pad:0'), false, 'a pad never proves focus');
});

test('blur drops a press in flight; refocus restarts the wait', () => {
  const c = clock(), g = createPressGuard({ now: c.now, focused: true });
  c.go(FOCUS_MS);
  g.down('Space'); g.focus(false); g.focus(true);
  assert.equal(g.up('Space'), false);
  c.go(FOCUS_MS - 1); assert.equal(tap(g), false);
  c.go(1); assert.equal(tap(g), true);
});

test('the confirm step ignores presses that began before it was up CARD_MS', () => {
  const c = clock(), g = createPressGuard({ now: c.now, focused: true });
  c.go(FOCUS_MS);
  g.down('Space'); g.card(); c.go(CARD_MS + 100);
  assert.equal(g.up('Space'), false, 'pressed before the card appeared');
  g.card(); c.go(CARD_MS - 1);
  assert.equal(g.ready(), false);
  assert.equal(tap(g), false, 'too soon after the card');
  c.go(1);
  assert.equal(g.ready(), true);
  assert.equal(tap(g), true);
  assert.equal(keyName({ key: ' ' }), 'Space');
  assert.equal(keyName({ code: 'NumpadEnter', key: 'Enter' }), 'NumpadEnter');
});

// ---- the card in the HUD, on a fake DOM and a fake clock
const t = makeT({});
const base = { itemId: 'rain.dress.scarlet-bell', priceSp: 730, noteKey: null, pending: false, retry: false };
function rig() {
  const c = clock(), doc = fakeDocument(), root = doc.createElement('main'), sent = [];
  const guard = createPressGuard({ now: c.now, focused: true });
  const hud = createHud(root, { t, send: (i) => sent.push(i), goTab: () => {}, motion: 'off', guard });
  const paint = (card) => hud.paint({ status: 'ready', sp: 1850, week: '2026-W44', tier: 1, tabs: ['rain-clothes'], tabId: 'rain-clothes',
    shelves: [], focus: { bay: 0, slot: 0, row: { id: base.itemId, priceSp: 730, sale: 'on', owned: null } }, card });
  const modal = root.querySelectorAll('.pc-modal')[0];
  const key = (k, extra = {}) => {
    const ev = { key: k === 'Space' ? ' ' : k, code: k, ...extra };
    const d = modal.dispatch('keydown', ev), u = modal.dispatch('keyup', ev);
    return d.defaultPrevented && u.defaultPrevented;
  };
  const button = (label) => root.querySelectorAll('button').find((b) => b.textContent === label);
  return { c, doc, sent, paint, modal, key, button };
}

test('HUD: Space on the freshly opened card does nothing; Tab to Buy, then a deliberate Space buys', () => {
  const r = rig();
  r.c.go(FOCUS_MS);
  r.paint({ ...base, face: 'buy', action: 'buy' });
  assert.ok(r.doc.activeElement.classList.contains('pc-card'));
  r.c.go(CARD_MS);
  assert.equal(r.key('Space'), true, 'the key is eaten');
  assert.deepEqual(r.sent, [], 'no default action on the card');
  r.modal.dispatch('keydown', { key: 'Tab' });
  assert.equal(r.doc.activeElement.textContent, 'Buy');
  r.key('Space');
  assert.deepEqual(r.sent, [{ type: 'select' }]);
});

test('HUD: a Space held on Buy, a repeat, or a press too soon after the card never buys', () => {
  const r = rig();
  r.c.go(FOCUS_MS);
  r.paint({ ...base, face: 'buy', action: 'buy' });
  r.modal.dispatch('keydown', { key: 'Tab' });
  r.key('Space');
  assert.deepEqual(r.sent, [], 'inside CARD_MS of the card');
  r.c.go(CARD_MS);
  r.modal.dispatch('keydown', { key: ' ', code: 'Space' });
  r.modal.dispatch('keydown', { key: ' ', code: 'Space', repeat: true });
  r.modal.dispatch('keyup', { key: ' ', code: 'Space' });
  assert.deepEqual(r.sent, [], 'held and repeated');
  r.modal.dispatch('keyup', { key: 'Enter', code: 'Enter' });
  assert.deepEqual(r.sent, [], 'a release with no press here');
});

test('HUD: a key-made click (detail 0) on Buy never buys; a pointer click does once the card has settled', () => {
  const r = rig();
  r.c.go(FOCUS_MS);
  r.paint({ ...base, face: 'buy', action: 'buy' });
  r.button('Buy').onclick({ detail: 1 });
  assert.deepEqual(r.sent, [], 'a click inside CARD_MS (a double click that opened the card)');
  r.c.go(CARD_MS);
  r.button('Buy').onclick({ detail: 0 });
  assert.deepEqual(r.sent, []);
  r.button('Buy').onclick({ detail: 1 });
  r.button('Keep looking').onclick({ detail: 0 });
  assert.deepEqual(r.sent, [{ type: 'select' }, { type: 'back' }], 'Keep looking is never held back');
});

test('HUD: the buy landing turns the card to Wear: focus goes back to the card and Wear waits again', () => {
  const r = rig();
  r.c.go(FOCUS_MS);
  r.paint({ ...base, face: 'buy', action: 'buy' });
  r.c.go(CARD_MS);
  r.modal.dispatch('keydown', { key: 'Tab' });
  r.key('Space');
  r.paint({ ...base, face: 'buy', action: 'buy', pending: true });
  r.paint({ ...base, face: 'owned', action: 'wear' });
  assert.ok(r.doc.activeElement.classList.contains('pc-card'), 'never lands on Wear');
  r.key('Space');
  r.modal.dispatch('keydown', { key: 'Tab' });
  assert.equal(r.doc.activeElement.textContent, 'Wear');
  r.key('Space');
  r.button('Wear').onclick({ detail: 1 });
  assert.deepEqual(r.sent, [{ type: 'select' }], 'only the buy: Wear is a new step');
  r.c.go(CARD_MS);
  r.key('Enter');
  assert.deepEqual(r.sent, [{ type: 'select' }, { type: 'select' }]);
  assert.equal(cardModel({ ...base, face: 'owned', action: 'wear' }, 0, t).buttons[0].label, 'Wear');
});
