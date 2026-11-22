/* When the self-opening cards open (lane S7a, owner asked for the transmission "midrun"): nothing before the run is
 * three minutes old by the run clock, the transmission card first, at most one per run unless five more minutes pass,
 * a herd that grows mid-run past three minutes gets its card at once, and the pen sign never waits. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as Ed from '../ui/edition.js';
import { TRANSMISSION_MIN_RUN_MS, LLAMA8_GAP_MS } from '../data/editions.js';
import { autoCardDue, penSignDue, mountTransmission, TRANSMISSION_PLACEHOLDERS } from '../ui/transmission.js';

const HOSTED = { hostname: 'app.cclabs.app', hosted: true };
const small = Ed.readEdition({ caps: { edition: 'small', platform: 'web' } }, HOSTED);
const full = Ed.readEdition({ caps: { edition: 'full', platform: 'desktop', expo: { state: 'absent', mb: 131 } } }, HOSTED);

test('the numbers', () => {
  assert.equal(TRANSMISSION_MIN_RUN_MS, 180000);
  assert.equal(LLAMA8_GAP_MS, 300000);
});

test('a herd of 8 at second 5 opens nothing; at 3:00 the transmission card; llama 8 waits for the gap', () => {
  assert.equal(autoCardDue({ clock: 5000, llamas: 8, extras: {}, ed: small }), null);
  assert.equal(autoCardDue({ clock: 179999, llamas: 8, extras: {}, ed: small }), null);
  assert.equal(autoCardDue({ clock: 180000, llamas: 8, extras: {}, ed: small }), 'transmission');
  const opened = { transmission: 180000, llama8: null }, extras = { transmissionSeen: 1 };
  assert.equal(autoCardDue({ clock: 200000, llamas: 8, extras, opened, ed: small }), null, 'not in the same run before the gap');
  assert.equal(autoCardDue({ clock: 180000 + LLAMA8_GAP_MS - 1, llamas: 8, extras, opened, ed: small }), null);
  assert.equal(autoCardDue({ clock: 180000 + LLAMA8_GAP_MS, llamas: 8, extras, opened, ed: small }), 'llama8');
  assert.equal(autoCardDue({ clock: 181000, llamas: 8, extras, opened: {}, ed: small }), 'llama8', 'a later run: at 3:00');
  assert.equal(autoCardDue({ clock: 900000, llamas: 8, extras: { transmissionSeen: 1, llama8Seen: 1 }, ed: small }), null);
  assert.equal(autoCardDue({ clock: 900000, llamas: 12, extras: {}, ed: full }), 'transmission');
  assert.equal(autoCardDue({ clock: 900000, llamas: 12, extras: { transmissionSeen: 1 }, ed: full }), null, 'full has no llama-8 card');
  assert.equal(penSignDue({ llamas: 4, extras: { transmissionSeen: 1 }, ed: full }), true, 'the sign needs no clock');
});

function fakePage() {
  const make = (tag) => {
    const el = { tagName: tag, children: [], style: {}, attrs: {}, listeners: {}, isConnected: true, parentNode: null, hidden: false, textContent: '', className: '',
      appendChild(c) { c.parentNode = el; el.children.push(c); return c; }, remove() { el.isConnected = false; },
      setAttribute(k, v) { el.attrs[k] = v; }, addEventListener(t, fn) { (el.listeners[t] ||= []).push(fn); },
      getBoundingClientRect: () => ({ left: 0, bottom: 0 }), querySelector: () => null };
    return el;
  };
  const body = make('body'), head = make('head');
  const doc = { body, head, createElement: make, getElementById: (id) => [...body.children, ...head.children].find((c) => c.id === id) || null };
  doc.defaultView = { navigator: { userActivation: { isActive: false } } };
  const opened = [];
  const cards = { open(spec) { const h = { close() { spec.onClose?.(); } }; opened.push(spec); spec.onOpen?.({ querySelector: () => null }); return h; } };
  return { doc, win: doc.defaultView, cards, opened };
}

test('mounted: nothing at second 5, the card at 3:00, and a herd that grows mid-run gets it at that moment', (t) => {
  t.after(() => Ed.setEdition(null, { noPage: true }));
  Ed.setEdition({ caps: { edition: 'small', platform: 'web' } }, HOSTED);
  const page = fakePage();
  let now = 5000, herd = 8;
  const tx = mountTransmission({ ...page, llamas: () => herd, clock: () => now, extras: {} });
  assert.equal(page.opened.length, 0);
  now = 180000; tx.tick();
  assert.equal(page.opened.length, 1);
  assert.equal(page.opened[0].title, 'INCOMING TRANSMISSION');
  now = 200000; tx.tick();
  assert.equal(page.opened.length, 1, 'no llama-8 card in the same run before the gap');
  tx.close();

  const grow = fakePage();
  now = 400000; herd = 3;
  const extras = {};
  const tx2 = mountTransmission({ ...grow, llamas: () => herd, clock: () => now, extras });
  assert.equal(grow.opened.length, 0, 'three llamas: no kind yet');
  herd = 4; now += 1000; tx2.tick();
  assert.equal(grow.opened.length, 1, 'the herd reached four past 3:00: the card at that moment');
  assert.equal(extras.transmissionSeen, 1);
  assert.notEqual(grow.opened[0].title, TRANSMISSION_PLACEHOLDERS.llama8Title);
  tx2.close();
});
