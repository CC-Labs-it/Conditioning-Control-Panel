/* The transmission card and the llama-8 card (lane S6, 2026-11-22; CONTRACT.md 6.5): which kind a herd calls for in each
 * edition and Expo state, YES sends one `expo-fetch` only from a press, the host frames move the card, NOT NOW leaves a
 * pen sign that offers it again, small never fetches, the frozen words are pinned, and nothing here touches the Expo. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import * as Ed from '../ui/edition.js';
import {
  TRANSMISSION_COPY, TRANSMISSION_PLACEHOLDERS, cardWords, sizeLine, statusLine, createTransmission,
  autoOfferDue, penSignDue, llama8Due, mountTransmission, transmissionAtWormhole,
} from '../ui/transmission.js';

const HOSTED = { hostname: 'app.cclabs.app', hosted: true };
const reading = (edition, platform, expo) => Ed.readEdition({ caps: { edition, platform, ...(expo ? { expo } : {}) } }, HOSTED);
const fullDeskAbsent = () => reading('full', 'desktop', { state: 'absent', mb: 131 });
const BASE = 'https://host.example/expo/';

test('the kind per edition, platform and Expo state at 3, 4 and 8 llamas', () => {
  const rows = [
    ['full desktop absent', fullDeskAbsent(), [null, 'fetch', 'fetch']],
    ['full desktop ready', reading('full', 'desktop', { state: 'ready', base: BASE }), [null, null, null]],
    ['full desktop, no expo caps', reading('full', 'desktop'), [null, null, null]],
    ['full web', reading('full', 'web'), [null, null, null]],
    ['small web', reading('small', 'web'), [null, 'nudge', 'nudge']],
    ['small desktop absent', reading('small', 'desktop', { state: 'absent', mb: 131 }), [null, 'nudge', 'nudge']],
  ];
  for (const [name, ed, want] of rows) assert.deepEqual([3, 4, 8].map((n) => Ed.transmissionKind(n, ed)), want, name);
  assert.equal(llama8Due(8, {}, reading('small', 'web')), true);
  assert.equal(llama8Due(7, {}, reading('small', 'web')), false);
  assert.equal(llama8Due(8, { llama8Seen: 1 }, reading('small', 'web')), false, 'once per save');
  assert.equal(llama8Due(12, {}, fullDeskAbsent()), false, 'full has the wormhole instead');
});

test('the approved words, character for character', () => {
  assert.equal(TRANSMISSION_COPY.title, 'INCOMING TRANSMISSION');
  assert.equal(TRANSMISSION_COPY.body, 'A message from the future is transmitting some data. Do you accept the transmission?');
  assert.equal(TRANSMISSION_COPY.yes, 'YES');
  assert.equal(TRANSMISSION_COPY.notNow, 'NOT NOW');
  assert.equal(sizeLine(131), '131 MB, saved to your CCP content folder.');
  assert.equal(sizeLine(140), '140 MB, saved to your CCP content folder.', 'the number is the host\'s');
  assert.equal(sizeLine(undefined), '', 'no size from the host: no size line');
  assert.equal(TRANSMISSION_COPY.smallBody + ' ' + TRANSMISSION_COPY.smallLine,
    'A message from the future is transmitting some data. It does not fit here. The full game can receive it.');
  const fetch = cardWords('fetch', 131), nudge = cardWords('nudge', 131);
  assert.deepEqual([fetch.title, fetch.body, fetch.size, fetch.yes, fetch.notNow],
    ['INCOMING TRANSMISSION', TRANSMISSION_COPY.body, '131 MB, saved to your CCP content folder.', 'YES', 'NOT NOW']);
  assert.equal(nudge.size, '', 'small: no size line');
  assert.deepEqual([nudge.title, nudge.yes, nudge.notNow], ['INCOMING TRANSMISSION', 'YES', 'NOT NOW']);
  for (const [slot, text] of Object.entries(TRANSMISSION_PLACEHOLDERS)) assert.match(text, /^\[PLACEHOLDER S6-[a-z0-9-]+\] /, slot);
  for (const text of [...Object.values(TRANSMISSION_COPY), ...Object.values(TRANSMISSION_PLACEHOLDERS)]) {
    assert.doesNotMatch(text, /[–—!]/, 'no dashes, no exclamation marks');
  }
});

test('YES sends exactly one expo-fetch, and only from a press', () => {
  const sent = [];
  let press = false;
  const tx = createTransmission({ kind: 'fetch', send: (type, f) => sent.push(type), inPress: () => press });
  assert.equal(tx.yes(), false, 'a script call is not a press');
  assert.deepEqual(sent, []);
  press = true;
  assert.equal(tx.yes(), true);
  assert.equal(tx.yes(), false, 'already transmitting');
  assert.deepEqual(sent, ['expo-fetch']);
  tx.progress(40); tx.progress(30); tx.progress(140);
  assert.equal(tx.view.pct, 100, 'never back, never past 100');
  assert.equal(statusLine(tx.view), '[PLACEHOLDER S6-progress] Receiving: 100%');
});

test('ready, failed, stop and try again', () => {
  const sent = [];
  const press = { on: true };
  const tx = createTransmission({ kind: 'fetch', send: (type) => sent.push(type), inPress: () => press.on });
  tx.yes();
  tx.failed('disk');
  assert.equal(tx.view.state, 'failed'); assert.equal(statusLine(tx.view), TRANSMISSION_PLACEHOLDERS.disk);
  tx.failed('whatever');
  assert.equal(tx.view.reason, 'refused', 'an unknown reason reads as refused');
  assert.equal(tx.yes(), true, 'offered again after a failure');
  press.on = false;
  assert.equal(tx.stop(), false, 'stop is the player\'s press too');
  press.on = true;
  assert.equal(tx.stop(), true);
  assert.equal(tx.view.state, 'stopped');
  assert.equal(tx.yes(), true, 'and again after a stop');
  tx.ready();
  assert.equal(tx.view.state, 'arrived');
  tx.failed('offline');
  assert.equal(tx.view.state, 'arrived', 'a late failure never takes it back');
  assert.equal(tx.yes(), false);
  assert.deepEqual(sent, ['expo-fetch', 'expo-fetch', 'expo-cancel', 'expo-fetch']);
});

test('small never sends expo-fetch: YES is the transmission nudge', () => {
  const sent = [];
  const ed = reading('small', 'web');
  const tx = createTransmission({ kind: 'nudge', send: (type, f) => sent.push([type, f]), inPress: () => true, ed: () => ed });
  assert.equal(tx.yes(), true);
  tx.progress(50); tx.ready(); tx.failed('offline');
  assert.equal(tx.stop(), false);
  assert.deepEqual(sent, [['nudge', { where: 'transmission', want: 'account' }]]);
  assert.ok(!sent.some(([t]) => t.startsWith('expo-')));
});

test('the card opens once per save, then the pen sign offers it again', () => {
  const ed = fullDeskAbsent();
  assert.equal(autoOfferDue(3, {}, ed), false);
  assert.equal(autoOfferDue(4, {}, ed), true);
  assert.equal(autoOfferDue(4, { transmissionSeen: 1 }, ed), false);
  assert.equal(penSignDue({ llamas: 4, extras: {}, ed }), false, 'not before the first offer');
  assert.equal(penSignDue({ llamas: 4, extras: { transmissionSeen: 1 }, ed }), true);
  assert.equal(penSignDue({ llamas: 4, extras: { transmissionSeen: 1 }, open: true, ed }), false, 'not while the card is up');
  assert.equal(penSignDue({ llamas: 4, extras: { transmissionSeen: 1 }, state: 'arrived', ed }), false);
  assert.equal(penSignDue({ llamas: 4, extras: { transmissionSeen: 1 }, ed: reading('full', 'desktop', { state: 'ready', base: BASE }) }), false);
});

/* A page small enough for the mount: elements, a card queue and the bridge. */
function fakePage() {
  const make = (tag) => {
    const el = {
      tagName: tag, children: [], className: '', textContent: '', hidden: false, style: {}, id: '', type: '', parentNode: null,
      isConnected: true, listeners: {}, attrs: {},
      appendChild(c) { c.parentNode = el; el.children.push(c); return c; },
      append(...cs) { for (const c of cs) el.appendChild(c); },
      remove() { el.isConnected = false; if (el.parentNode) el.parentNode.children = el.parentNode.children.filter((c) => c !== el); },
      setAttribute(k, v) { el.attrs[k] = v; },
      addEventListener(type, fn) { (el.listeners[type] ||= []).push(fn); },
      click() { for (const fn of el.listeners.click || []) fn({}); },
      getBoundingClientRect: () => ({ left: 10, bottom: 40 }),
      querySelector: () => null,
    };
    return el;
  };
  const body = make('body'), head = make('head');
  const doc = { body, head, createElement: make, getElementById: (id) => [...body.children, ...head.children].find((c) => c.id === id) || null };
  const win = { navigator: { userActivation: { isActive: false } } };
  doc.defaultView = win;
  const opened = [];
  const cards = {
    open(spec) {
      const handle = { closed: false, close() { if (handle.closed) return; handle.closed = true; spec.onClose?.(); } };
      const yes = make('button');
      opened.push({ spec, handle, press: (i) => { const b = spec.buttons[i]; if (b.keepOpen) b.onPress?.(); else { handle.close(); b.onPress?.(); } } });
      spec.onOpen?.({ querySelector: () => yes });
      return handle;
    },
  };
  const handlers = {};
  const on = (type, fn) => { (handlers[type] ||= new Set()).add(fn); return () => handlers[type].delete(fn); };
  const host = (type, f) => { for (const fn of handlers[type] || []) fn(f); };
  const pen = make('button'); body.appendChild(pen);
  return { doc, win, cards, opened, on, host, pen };
}

test('mounted: NOT NOW, the pen sign, YES from a real press, progress and arrival', async (t) => {
  t.after(() => Ed.setEdition(null, { noPage: true }));
  Ed.setEdition({ caps: { edition: 'full', platform: 'desktop', expo: { state: 'absent', mb: 131 } } }, HOSTED);
  const page = fakePage(), sent = [], extras = {};
  const tx = mountTransmission({ ...page, send: (type, f) => sent.push([type, f]), llamas: 4, extras });
  assert.equal(extras.transmissionSeen, 1);
  assert.equal(page.opened.length, 1, 'the card opens by itself once');
  const first = page.opened[0];
  assert.equal(first.spec.title, 'INCOMING TRANSMISSION');
  assert.deepEqual(first.spec.buttons.map((b) => b.label), ['YES', 'NOT NOW']);
  assert.ok(first.spec.node.children.some((c) => c.textContent === '131 MB, saved to your CCP content folder.'));
  first.press(0);
  assert.deepEqual(sent, [], 'a press the browser did not see sends nothing');
  first.press(1);   // NOT NOW
  const sign = page.doc.getElementById('llama-sign');
  assert.ok(sign, 'the pen wears the sign');
  sign.click();
  assert.equal(page.opened.length, 2, 'the sign offers it again');
  assert.equal(page.doc.getElementById('llama-sign'), null, 'no sign while the card is up');
  page.win.navigator.userActivation.isActive = true;
  page.opened[1].press(0);
  page.opened[1].press(0);
  assert.deepEqual(sent.map(([type]) => type), ['expo-fetch'], 'one fetch');
  page.host('expo-progress', { pct: 55 });
  assert.equal(tx.state.pct, 55);
  page.host('expo-failed', { reason: 'offline' });
  assert.equal(tx.state.state, 'failed');
  page.opened[1].press(0);
  page.host('expo-ready', { base: BASE });
  assert.equal(tx.state.state, 'fetching', 'expo-ready counts only once the edition took the base');
  Ed.expoArrived(BASE);
  page.host('expo-ready', { base: BASE });
  assert.equal(tx.state.state, 'arrived');
  assert.deepEqual(sent.map(([type]) => type), ['expo-fetch', 'expo-fetch']);
  assert.equal(await transmissionAtWormhole(), true, 'the Expo is here: the wormhole opens');
  tx.close();
});

test('mounted: at the wormhole with the Expo absent the card comes first', async (t) => {
  t.after(() => Ed.setEdition(null, { noPage: true }));
  Ed.setEdition({ caps: { edition: 'full', platform: 'desktop', expo: { state: 'absent', mb: 131 } } }, HOSTED);
  const page = fakePage();
  const tx = mountTransmission({ ...page, llamas: 8, extras: { transmissionSeen: 1 } });
  assert.equal(page.opened.length, 0, 'seen before: no card by itself');
  const gate = transmissionAtWormhole();
  assert.equal(page.opened.length, 1);
  assert.equal(page.opened[0].spec.front, true);
  page.opened[0].press(1);   // NOT NOW
  assert.equal(await gate, false, 'shut with nothing here: no visit');
  const again = transmissionAtWormhole();
  page.win.navigator.userActivation.isActive = true;
  page.opened[1].press(0);
  Ed.expoArrived(BASE);
  page.host('expo-ready', { base: BASE });
  assert.equal(await again, true, 'arrived while the card was up: on into the Expo');
  tx.close();
});

test('mounted in small: the nudge card, then the llama-8 card', (t) => {
  t.after(() => Ed.setEdition(null, { noPage: true }));
  Ed.setEdition({ caps: { edition: 'small', platform: 'web' } }, HOSTED);
  const page = fakePage(), sent = [], extras = {};
  const tx = mountTransmission({ ...page, send: (type, f) => sent.push([type, f]), llamas: 8, extras });
  assert.equal(page.opened.length, 2);
  assert.equal(page.opened[0].spec.body, TRANSMISSION_COPY.smallBody);
  assert.ok(page.opened[0].spec.node.children.some((c) => c.textContent === TRANSMISSION_COPY.smallLine));
  assert.ok(!page.opened[0].spec.node.children.some((c) => /MB/.test(c.textContent)), 'no size line in small');
  assert.equal(page.opened[1].spec.title, TRANSMISSION_PLACEHOLDERS.llama8Title);
  page.win.navigator.userActivation.isActive = true;
  page.opened[0].press(0);
  page.opened[1].press(0);
  assert.deepEqual(sent, [['nudge', { where: 'transmission', want: 'account' }], ['nudge', { where: 'llama8', want: 'account' }]]);
  assert.deepEqual([extras.transmissionSeen, extras.llama8Seen], [1, 1]);
  tx.close();
});

test('the card touches no Expo file', () => {
  const src = readFileSync(new URL('../ui/transmission.js', import.meta.url), 'utf8');
  assert.doesNotMatch(src, /from\s+['"][^'"]*\/expo\/|import\(\s*['"][^'"]*\/expo\/|assets\/expo/);
  assert.doesNotMatch(src, /caps\.edition/, 'only ui/edition.js reads the edition');
});
