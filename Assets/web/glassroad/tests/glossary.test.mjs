// tests/glossary.test.mjs - the Power-ups card (hud/glossary.js): one row per live item, words derived from the tables,
// and the guard that fails when the item table and the glossary drift apart.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { ITEM_IDS, ITEMS } from '../sim/itemTable.js';
import { itemUseMode } from '../sim/itemUse.js';
import { ITEM_NAMES, ICON_IDS, iconOf } from '../hud/format.js';
import { howtoHtml } from '../hud/howto.js';
import { actSound } from '../hud/sounds.js';
import { FIRE_WORDS, GLOSSARY_LINES, OPTIONAL_ROWS, glossaryHtml, glossaryKeys, glossaryProblems, glossaryRows, scrollGlossary, whoGets } from '../hud/glossary.js';

const read = (p) => readFileSync(new URL(p, import.meta.url), 'utf8');
const JSONFEEL = JSON.parse(read('../feel.json'));
/** A feel over a copy of feel.json; like core/feel.js a missing key throws. */
function feelWith(items = {}) {
  const root = { ...JSONFEEL, items: { ...JSONFEEL.items, ...items } };
  return { get(path) { const v = path.split('.').reduce((o, k) => o?.[k], root); if (v === undefined) throw new Error('no ' + path); return v; } };
}
const feel = feelWith();
const REAL = { ids: ITEM_IDS, items: ITEMS, names: ITEM_NAMES, lines: GLOSSARY_LINES };

test('guard: the glossary and the live items agree', () => {
  assert.deepEqual(glossaryProblems(feel), []);
});

test('guard: a live item with no row fails', () => {
  const { surge, ...lines } = GLOSSARY_LINES;
  assert.ok(surge);
  assert.deepEqual(glossaryProblems(feel, { ...REAL, lines }), ['surge: live with no glossary row']);
  assert.ok(glossaryProblems(feelWith({ flashWeights: [0, 1, 0] })).includes('flash: live with no glossary row'), 'a retired item coming back owes a row');
});

test('guard: a weight-0 item in the glossary fails', () => {
  assert.deepEqual(glossaryProblems(feelWith({ surgeWeights: [0, 0, 0] })), ['surge: weight 0 with a glossary row']);
});

test('guard: a row naming an id ITEM_NAMES lacks fails', () => {
  const { disc, ...names } = ITEM_NAMES;
  assert.ok(disc);
  assert.ok(glossaryProblems(feel, { ...REAL, names }).includes('disc: a row with no name in ITEM_NAMES'));
  assert.ok(glossaryProblems(feel, { ...REAL, lines: { ...GLOSSARY_LINES, nosuch: () => 'x' } }).includes('nosuch: a glossary row for an id ITEM_IDS lacks'));
});

test('rows: the nine live items, in table order, under their shown names', () => {
  assert.deepEqual(glossaryRows(feel).map((r) => r.name), ['Fog', 'Melt', 'Surge', 'Bubble Shield', 'Pendulum', 'Disc', 'Tether', 'Snap', 'Big Spiral']);
});

test('rows: every live item has a real icon of its own', () => {
  for (const r of glossaryRows(feel)) {
    assert.ok(ICON_IDS.includes(r.id), r.id + ' has its own icon, not the empty bubble');
    assert.equal(r.icon, iconOf(r.id));
    assert.ok(existsSync(new URL('../' + r.icon, import.meta.url)), r.icon);
  }
});

test('copy: dry and plain, no dashes, no exclamation marks, no empty cell, numbers filled in', () => {
  for (const r of glossaryRows(feel)) {
    for (const text of [r.line, r.fire, r.who]) {
      assert.ok(text.length > 0, r.id);
      assert.ok(!/[–—!]/.test(text), r.id + ': ' + text);
      assert.ok(!/undefined|NaN/.test(text), r.id + ': ' + text);
    }
    assert.ok(r.line.length <= 150, r.id + ' stays one short line: ' + r.line.length);
  }
  const line = (id) => glossaryRows(feel).find((r) => r.id === id).line;
  assert.ok(line('pink').includes(JSONFEEL.items.pinkRangeM + ' m'), 'numbers come from feel.json');
  assert.ok(glossaryRows(feelWith({ pinkRangeM: 77 })).find((r) => r.id === 'pink').line.includes('77 m'));
  assert.ok(!/–|—|!/.test(glossaryKeys({})));
});

test('sim: the words hold against the sim', () => {
  const fire = (id) => glossaryRows(feel).find((r) => r.id === id).fire;
  for (const r of glossaryRows(feel)) {
    const use = ITEMS[r.id].use;
    assert.equal(r.fire === 'Drop behind', itemUseMode(r.id, false) === 'drop', r.id + ' drop word follows a plain press');
    assert.equal(r.fire === 'On yourself', use === 'self', r.id + ' self word follows the table');
    // the foot line: anything not used on yourself can go behind as a trap
    assert.equal(itemUseMode(r.id, true) === 'drop', use !== 'self', r.id);
  }
  assert.equal(fire('melt'), 'Drop behind');
  assert.equal(fire('glass'), 'On yourself');
  const [front, middle, back] = JSONFEEL.items.surgeAmount;
  assert.ok(front < middle && middle < back, 'Surge: the further back the bigger');
  assert.ok(JSONFEEL.items.bigSpiralCapMul < 1 && JSONFEEL.items.tetherTowCap > 1 && JSONFEEL.items.tetherDragCap < 1, 'up and down words');
  assert.equal(ITEMS.melt.view, 'none', 'Melt never blurs the view, so its line must not say so');
  assert.ok(!/view|blur/.test(glossaryRows(feel).find((r) => r.id === 'melt').line));
});

test('who: derived from the weights as a share of each bucket', () => {
  const who = Object.fromEntries(glossaryRows(feel).map((r) => [r.id, r.who]));
  assert.deepEqual(who, { pink: 'Mostly middle and back', melt: 'Mostly leader and middle', surge: 'Mostly leader and back', glass: 'Mostly leader',
    pendulum: 'Mostly middle and back', disc: 'Mostly middle and back', tether: 'Mostly middle and back', snap: 'Mostly leader and middle', bigSpiral: 'Back only' });
  assert.equal(glossaryRows(feelWith({ glassWeights: [0, 0, 7] })).find((r) => r.id === 'glass').who, 'Back only', 'retuning the weights moves the word, no copy edit');
  assert.equal(whoGets([1, 1, 1], [3, 3, 3]), 'Anyone');
  assert.equal(whoGets([2, 0, 0], [5, 5, 5]), 'Leader only');
  assert.equal(whoGets([0, 0, 0], [5, 5, 5]), '');
});

test('tunnel: optional, listed only once the id exists', () => {
  assert.ok(OPTIONAL_ROWS.includes('tunnel'));
  assert.ok(!ITEM_IDS.includes('tunnel') ? !glossaryRows(feel).some((r) => r.id === 'tunnel') : true, 'no id, no row, and no guard failure either');
  const tables = { ids: [...ITEM_IDS, 'tunnel'], items: { ...ITEMS, tunnel: { use: 'ride' } }, names: { ...ITEM_NAMES, tunnel: 'Tunnel Vision' }, lines: GLOSSARY_LINES };
  const f = feelWith({ tunnelWeights: [0, 0, 3], tunnelS: 4, tunnelStopPlace: 2 });
  const row = glossaryRows(f, tables).find((r) => r.id === 'tunnel');
  assert.equal(row.name, 'Tunnel Vision');
  assert.equal(row.fire, 'On yourself');
  assert.equal(row.who, 'Back only');
  assert.ok(row.line.includes('4 s') && row.line.includes('2nd'), row.line);
  assert.deepEqual(glossaryProblems(f, tables), []);
  assert.ok(glossaryProblems(f, { ...tables, names: { ...ITEM_NAMES, tunnel: undefined } }).includes('tunnel: a row with no name in ITEM_NAMES'));
});

test('html: one row each, device-aware foot line, escaped', () => {
  const html = glossaryHtml(feel, { last: 'pad', pad: { family: 'ps' } });
  assert.equal((html.match(/class="gl-row"/g) || []).length, 9);
  assert.ok(html.includes('Square throw. Triangle drop behind.'), 'the pad names its own buttons');
  assert.ok(glossaryHtml(feel, { last: 'touch' }).includes('Throw uses it. Drop leaves it behind.'));
  assert.ok(glossaryHtml(feel).includes('E / Enter throw. R / Backspace behind.'));
  assert.ok(!/<button/.test(html), 'the rows hold no buttons: pad up and down scroll the list');
  assert.ok(html.includes(JSONFEEL.items.glassMaxS + ' s.'), 'a number keeps its unit on the same line');
  assert.ok(!/\u0008/.test(read('../hud/glossary.js')), 'no stray control characters in the source');
});

test('scroll: one row a step, says when it moved', () => {
  const list = { scrollTop: 0, max: 200, querySelector: () => ({ offsetHeight: 90 }) };
  Object.defineProperty(list, 'scrollTop', { get() { return this._t || 0; }, set(v) { this._t = Math.max(0, Math.min(this.max, v)); } });
  assert.equal(scrollGlossary(list, 'down'), true); assert.equal(list.scrollTop, 90);
  assert.equal(scrollGlossary(list, 'up'), true); assert.equal(list.scrollTop, 0);
  assert.equal(scrollGlossary(list, 'up'), false, 'at the top');
  assert.equal(scrollGlossary(null, 'up'), false);
});

test('doors: the how-to card and the pause card open it, Back and the top close leave it', () => {
  assert.ok(howtoHtml({}).includes('data-act="glossary"'), 'how-to door');
  assert.equal(actSound('glossary'), 'menu-ok');
  const cards = read('../hud/cards.js').replace(/\r/g, '');
  assert.ok(/k === 'pause'[^]*?btn\('glossary', 'Power-ups'\)[^]*?k === 'options'/.test(cards), 'pause door');
  assert.ok(/BACKS = \[[^\]]*'glossary'/.test(cards), 'Escape and pad B go back');
  assert.ok(/topClose\.hidden = k !== 'options' && k !== 'glossary'/.test(cards), 'the top close the owner asked Options for');
  assert.ok(read('../hud/hud.js').includes('devices, kit, feel }'), 'the hud hands the cards the feel');
});
