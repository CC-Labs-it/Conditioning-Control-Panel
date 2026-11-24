/* The small edition's one locked tile (lane S, owner 2026-11-22) and the size rule for the transmission: the tile shows
 * once, last in the shop, only in small; it nudges only from a real press and changes no count; and with no size from
 * the host the download is never offered. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import * as Ed from '../ui/edition.js';
import * as E from '../sim/economy.js';
import { serialize, createLife } from '../sim/save.js';
import { LOCKED_TILE_COPY, mountLockedTile } from '../ui/shop-locked.js';

const HOSTED = { hostname: 'tc.cclabs.app', hosted: true };
const small = Ed.readEdition({ caps: { edition: 'small', platform: 'web' } }, HOSTED);

function fakeDoc() {
  const byId = new Map();
  const make = (tag) => ({ tag, children: [], listeners: {}, attrs: {}, dataset: {}, style: {}, textContent: '', className: '', innerHTML: '',
    set id(v) { this._id = v; byId.set(v, this); }, get id() { return this._id; },
    append(...cs) { this.children.push(...cs); }, setAttribute(k, v) { this.attrs[k] = v; },
    addEventListener(t, f) { (this.listeners[t] ||= []).push(f); }, click(ev = {}) { for (const f of this.listeners.click || []) f(ev); } });
  return { createElement: make, getElementById: (id) => byId.get(id) || null };
}

test('one tile, last in the shop, in the shop\'s own short look, never an engine row', () => {
  const doc = fakeDoc(), tails = [];
  const door = { tail: (el) => tails.push(el) };
  const el = mountLockedTile({ doc, win: {}, door, ed: small });
  assert.ok(el);
  assert.equal(mountLockedTile({ doc, win: {}, door, ed: small }), null, 'shown once');
  assert.deepEqual(tails, [el], 'handed to the shop as its tail');
  assert.equal(el.className, 'row short locked-tile');
  assert.equal(el.dataset.id, undefined, 'no row id: nothing that counts rows sees it');
  assert.equal(el.attrs['aria-label'], LOCKED_TILE_COPY.label);
  assert.ok(el.children.some((c) => c.textContent === 'More in the full game'));
  assert.equal(mountLockedTile({ doc: fakeDoc(), win: {}, door: null, ed: small }), null, 'no shop yet: nothing');
});

test('its press opens the door card and sends nothing; the card\'s link sends the locked-tile nudge only from a real press, and changes no count', () => {
  const doc = fakeDoc(), sent = [], cards = [];
  const run = E.createRun({ ed: small });
  E.lump(run, 1234);
  const before = serialize(run, createLife(), {});
  const open = (spec) => { cards.push(spec); return { open: true, close() {} }; };
  const el = mountLockedTile({ doc, win: { navigator: {} }, door: { tail() {} }, send: (t, f) => sent.push([t, f]), ed: small, open });
  el.click({ isTrusted: true });
  assert.deepEqual(sent, [], 'lane D: the tile itself never sends a link');
  assert.equal(cards.length, 1);
  assert.equal(cards[0].title, LOCKED_TILE_COPY.label);
  assert.equal(cards[0].go({ isTrusted: false }), false);
  assert.deepEqual(sent, []);
  assert.equal(cards[0].go({ isTrusted: true }), true);
  assert.deepEqual(sent, [['nudge', { where: 'locked-tile', want: 'account' }]]);
  assert.equal(serialize(run, createLife(), {}), before, 'the run is what it was');
});

test('small only: main.js mounts it behind isSmall, and the shop keeps it last', () => {
  const main = readFileSync(new URL('../main.js', import.meta.url), 'utf8');
  assert.match(main, /if \(isSmall\(ed\)\) mountLockedTile\(/);
  const shop = readFileSync(new URL('../ui/shop.js', import.meta.url), 'utf8');
  assert.match(shop, /tail\(el\) \{/);
  assert.match(shop, /if \(tailEl && tailDue && rowsEl\.lastElementChild !== tailEl\) rowsEl\.appendChild\(tailEl\);/);
  const tile = readFileSync(new URL('../ui/shop-locked.js', import.meta.url), 'utf8');
  assert.doesNotMatch(tile, /\.focus\(|caps\??\.edition|isSmall\(/, 'never takes focus, reads no edition');
});

test('no size from the host: the download is never offered', () => {
  const desk = (expo) => Ed.readEdition({ caps: { edition: 'full', platform: 'desktop', expo } }, HOSTED);
  assert.equal(Ed.transmissionKind(4, desk({ state: 'absent', mb: 131 })), 'fetch');
  assert.equal(Ed.transmissionKind(4, desk({ state: 'absent' })), null, 'no mb');
  assert.equal(Ed.transmissionKind(8, desk({ state: 'absent', mb: 0 })), null, 'mb 0');
  assert.equal(Ed.expoMb(desk({ state: 'absent' })), null);
});
