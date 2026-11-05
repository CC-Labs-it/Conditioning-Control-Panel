// The "How it works" card: once per account, never over a Wear, and closing it can never spend or leave focus on anything.
import test from 'node:test';
import assert from 'node:assert/strict';
import { guideDue, markGuideSeen, opensToWear, seenKey } from '../state/guide.js';
import { createPressGuard, FOCUS_MS, CARD_MS } from '../state/press-guard.js';
import { createHud } from '../hud/index.js';
import { LEX, makeT } from '../state/strings.js';
import { fakeDocument } from './fake-dom.mjs';

const store = (seed = {}) => { const m = new Map(Object.entries(seed)); return { getItem: (k) => (m.has(k) ? m.get(k) : null), setItem: (k, v) => m.set(k, v) }; };
const init = (id = 'u1', more = {}) => ({ identity: { unifiedId: id }, from: 'wallet', ...more });
const due = (o = {}) => guideDue({ init: init(), status: 'ready', card: null, storage: store(), ...o });

test('due on a first ready open, once per account', () => {
  const s = store();
  assert.equal(due({ storage: s }), true);
  markGuideSeen(s, init());
  assert.equal(due({ storage: s }), false);
  assert.equal(due({ storage: s, init: init('u2') }), true, 'another account on this PC still gets it');
});

test('never over the sign-in window, an offline counter or an open card', () => {
  for (const status of ['loading', 'signed_out', 'offline']) assert.equal(due({ status }), false, status);
  assert.equal(due({ card: { face: 'owned' } }), false);
});

test('skipped when opened to Wear something, and that open does not use it up', () => {
  assert.equal(opensToWear(init('u1', { wear: 'kart.body.petal' })), true);
  assert.equal(opensToWear(init('u1', { from: 'rain-wardrobe' })), true);
  assert.equal(due({ init: init('u1', { wear: 'kart.body.petal' }) }), false);
  const s = store();
  assert.equal(due({ storage: s, init: init('u1', { from: 'rain-wardrobe' }) }), false);
  assert.equal(due({ storage: s }), true);
});

test('a blocked or missing store never throws and never nags', () => {
  const blocked = { getItem() { throw new Error('denied'); }, setItem() { throw new Error('denied'); } };
  assert.equal(due({ storage: blocked }), false);
  assert.equal(due({ storage: null }), false);
  assert.doesNotThrow(() => markGuideSeen(blocked, init()));
  assert.equal(seenKey({}), 'pc.howto.seen.anon');
});

test('the copy stays on this release: no Rain, no other platform, the house voice', () => {
  for (const [k, v] of Object.entries(LEX)) if (k.startsWith('shop_howto')) {
    assert.doesNotMatch(v, /\brain\b|\bweb\b|phone|website|linux|mobile|[–—!]/i, k);
  }
});

function rig({ motion = 'off', card = null } = {}) {
  let at = 0;
  const doc = fakeDocument(), root = doc.createElement('main'), sent = [];
  const guard = createPressGuard({ now: () => at, focused: true });
  const hud = createHud(root, { t: makeT({}), send: (i) => sent.push(i), goTab() {}, guard, motion });
  const view = { status: 'ready', sp: 500, week: '2026-W44', tier: 1, tabs: ['kart'], tabId: 'kart', shelves: [], focus: null, card };
  hud.paint(view);
  const g = hud.guide, door = hud.el.children[0].querySelectorAll('.pc-howto')[0];
  const go = g.el.querySelectorAll('.pc-guide-go')[0], scrim = g.el.querySelectorAll('.pc-scrim')[0];
  const press = (key, code = key, more = {}) => { g.el.dispatch('keydown', { key, code, ...more }); g.el.dispatch('keyup', { key, code }); };
  return { doc, hud, g, door, go, scrim, sent, guard, press, view, wait: (ms) => { at += ms; }, ready: () => { at += FOCUS_MS + CARD_MS; } };
}

test('the door shows on a ready counter only, and opens the card focused on itself, never on its button', () => {
  const r = rig();
  assert.equal(r.door.hidden, false);
  r.hud.paint({ ...r.view, status: 'signed_out' });
  assert.equal(r.door.hidden, true);
  r.hud.paint(r.view);
  assert.equal(r.g.el.hidden, true);
  r.door.onclick();
  assert.equal(r.g.isOpen(), true);
  assert.equal(r.g.el.hidden, false);
  assert.ok(r.doc.activeElement.classList.contains('pc-guide'));
  assert.notEqual(r.g.el.querySelectorAll('.pc-gb-shot')[2].getAttribute('src'), null, 'prints load on open');
});

test('three beats with their stamps, words and captures', () => {
  const r = rig();
  const beats = r.g.el.querySelectorAll('.pc-gb');
  assert.equal(beats.length, 3);
  assert.deepEqual(r.g.el.querySelectorAll('.pc-gb-title').map((n) => n.textContent), ['Earn', 'Pick', 'Buy, wear']);
  r.door.onclick();
  assert.deepEqual(r.g.el.querySelectorAll('.pc-gb-shot').map((n) => n.getAttribute('src').split('/').pop()), ['earn.webp', 'pick.webp', 'buy-wear.webp']);
});

test('the door does nothing under a confirm card', () => {
  const r = rig({ card: { face: 'buy', itemId: 'kart.body.petal', priceSp: 100 } });
  r.door.onclick();
  assert.equal(r.g.isOpen(), false);
});

test('a deliberate Space, Enter or Esc closes it, with nothing focused and nothing sent', () => {
  for (const [key, code] of [[' ', 'Space'], ['Enter', 'Enter'], ['Escape', 'Escape'], ['Backspace', 'Backspace']]) {
    const r = rig();
    r.ready();
    r.door.onclick();
    r.wait(CARD_MS);
    r.press(key, code);
    assert.equal(r.g.isOpen(), false, key);
    assert.equal(r.g.el.hidden, true, key);
    assert.equal(r.doc.activeElement, r.doc.body, key);
    assert.deepEqual(r.sent, [], key);
  }
});

test('a stray key does not wave it away: too soon, held from before, or a repeat', () => {
  const r = rig();
  r.ready();
  r.g.el.dispatch('keydown', { key: ' ', code: 'Space' });   // held down when the card arrives
  r.door.onclick();
  r.wait(CARD_MS);
  r.g.el.dispatch('keyup', { key: ' ', code: 'Space' });
  assert.equal(r.g.isOpen(), true, 'held from before');
  r.press(' ', 'Space');
  assert.equal(r.g.isOpen(), false, 'a fresh press after the wait closes it');

  const s = rig();
  s.ready();
  s.door.onclick();
  s.press('Enter');
  assert.equal(s.g.isOpen(), true, 'inside the first moment');
  s.wait(CARD_MS);
  s.g.el.dispatch('keydown', { key: 'Enter', code: 'Enter', repeat: true });
  s.g.el.dispatch('keyup', { key: 'Enter', code: 'Enter' });
  assert.equal(s.g.isOpen(), true, 'a repeat only');
});

test('clicks and taps close it after the first moment; a key\'s synthetic click never does', () => {
  const r = rig();
  r.ready();
  r.door.onclick();
  r.go.onclick({ detail: 1 });
  assert.equal(r.g.isOpen(), true, 'too fresh');
  r.wait(CARD_MS);
  r.go.onclick({ detail: 0 });
  assert.equal(r.g.isOpen(), true, 'synthetic');
  r.go.onclick({ detail: 1 });
  assert.equal(r.g.isOpen(), false);
  r.door.onclick();
  r.wait(CARD_MS);
  r.scrim.onclick({ detail: 1 });
  assert.equal(r.g.isOpen(), false, 'the scrim too');
  assert.deepEqual(r.sent, []);
});

test('Tab walks between the card and its button, and stays in the card', () => {
  const r = rig();
  r.door.onclick();
  const e = r.g.el.dispatch('keydown', { key: 'Tab', code: 'Tab' });
  assert.equal(e.defaultPrevented, true);
  assert.equal(r.doc.activeElement, r.go);
  r.g.el.dispatch('keydown', { key: 'Tab', code: 'Tab' });
  assert.ok(r.doc.activeElement.classList.contains('pc-guide'));
});

test('a press mashed right after closing is not a shelf press', () => {
  const r = rig();
  r.ready();
  r.door.onclick();
  r.wait(CARD_MS);
  r.press('Enter');
  assert.equal(r.g.isOpen(), false);
  r.guard.down('Enter');
  assert.equal(r.guard.up('Enter'), false);
});

test('motion: an IN and an OUT when on, a cut when off', () => {
  const r = rig({ motion: 'full' });
  r.door.onclick();
  assert.ok(r.g.el.classList.contains('pc-in'));
  r.g.close();
  assert.ok(r.g.el.classList.contains('pc-out'));
  r.g.dispose();
  const off = rig({ motion: 'off' });
  off.door.onclick();
  assert.equal(off.g.el.classList.contains('pc-in'), false);
  off.g.close();
  assert.equal(off.g.el.hidden, true);
});
