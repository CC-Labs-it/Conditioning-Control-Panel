// Kart bodies show what they do: the same bars as the Glassroad garage, on the shelf peek and the confirm card, from a
// copied table (a page never fetches across web trees). The parity test fails on any drift and prints the rows to paste.
import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { KART_BARS, TYPE_WORDS, STAT_WORDS, PIPS, kartBarsOf, barsLine } from '../state/kart-bars.js';
import { cardModel, renderCard } from '../hud/card.js';
import { renderBars } from '../hud/bars.js';
import { makeT } from '../state/strings.js';
import { fakeDocument } from './fake-dom.mjs';

const read = (rel) => readFileSync(new URL(rel, import.meta.url), 'utf8').replace(/\r\n/g, '\n');
const t = makeT({});
const STATS = () => STAT_WORDS.map(([key]) => key);
const lit = (tag, key) => tag.querySelectorAll('.pc-bar').find((r) => r.dataset.stat === key).querySelectorAll('.pc-on').length;
const face = (itemId) => ({ face: 'buy', action: 'buy', itemId, priceSp: 900, noteKey: null, pending: false, retry: false });

test('a kart body row finds its bars; anything else finds none', () => {
  assert.deepEqual(kartBarsOf('kart.body.undertow'), { ...KART_BARS.undertow, word: 'Fast' });
  assert.equal(kartBarsOf('kart.body.house-edge').word, 'Heavy');
  for (const id of ['kart.body.nope', 'kart.body.constructor', 'kart.outfit.pit-crew', 'rain.dress.scarlet-bell', 'undertow', '', null, undefined]) assert.equal(kartBarsOf(id), null, String(id));
});

test('every kart body on sale has a row, and every row stays in 1..7 with a known type word', () => {
  const items = JSON.parse(read('../assets/manifest.json')).items.map((i) => i.id).filter((id) => id.startsWith('kart.body.'));
  assert.ok(items.length >= 9, `found ${items.length}`);
  assert.deepEqual(items.filter((id) => !kartBarsOf(id)), []);
  for (const [id, row] of Object.entries(KART_BARS)) {
    for (const key of STATS()) assert.ok(Number.isInteger(row[key]) && row[key] >= 1 && row[key] <= PIPS, `${id} ${key}`);
    assert.ok(TYPE_WORDS[row.type], `${id} ${row.type}`);
  }
});

test('the bars draw a type word and four rows of seven pips, with no control in them', () => {
  const tag = renderBars(fakeDocument(), kartBarsOf('kart.body.checkmate'));
  assert.equal(tag.querySelectorAll('.pc-bar').length, 4);
  assert.equal(tag.querySelectorAll('.pc-pip').length, 4 * PIPS);
  assert.deepEqual(STATS().map((key) => lit(tag, key)), [5, 1, 3, 7]);
  assert.match(tag.text(), /Heavy Speed Accel Handling Stability/);
  assert.equal(tag.querySelectorAll('button').length, 0);
  assert.equal(tag.getAttribute('aria-label'), barsLine(kartBarsOf('kart.body.checkmate')));
  assert.match(tag.getAttribute('aria-label'), /^Heavy\. Speed 5 of 7, Accel 1 of 7, Handling 3 of 7, Stability 7 of 7$/);
});

test('the confirm card shows the bars on a kart body only, on every face a buyer can reach', () => {
  for (const f of ['buy', 'owned', 'prime', 'short', 'soon', 'gone']) {
    const m = cardModel({ ...face('kart.body.undertow'), face: f, short: 10 }, 100, t);
    const card = renderCard(fakeDocument(), m, () => {});
    assert.equal(card.querySelectorAll('.pc-bars').length, 1, f);
    assert.equal(lit(card.querySelectorAll('.pc-bars')[0], 'speed'), 7, f);
  }
  for (const id of ['kart.outfit.pit-crew', 'rain.dress.scarlet-bell']) {
    const m = cardModel(face(id), 1000, t);
    assert.equal(m.bars, null, id);
    assert.equal(renderCard(fakeDocument(), m, () => {}).querySelectorAll('.pc-bars').length, 0, id);
  }
});

test('the shelf peek draws the focused body\'s bars and drops them on the next item', () => {
  const src = read('../hud/index.js');
  assert.match(src, /const bars = kartBarsOf\(row\.id\);/);
  assert.match(src, /peekBars\.replaceChildren\(\.\.\.\(bars \? \[renderBars\(doc, bars\)\] : \[\]\)\);/);
});

test('still by design: no animation or transition, and two up on a phone', () => {
  const css = read('../page.css'), from = css.indexOf('/* kart bars'), to = css.indexOf('/* end kart bars');
  assert.ok(from > 0 && to > from);
  assert.doesNotMatch(css.slice(from, to), /animation|transition|@keyframes/);
  assert.match(css.slice(from, to), /@media \(max-width: 760px\) \{[^}]*\.pc-peek-bars[^}]*flex-basis: 100%/);
  assert.match(css.slice(from, to), /\.pc-peek-bars \.pc-bars \{[^}]*grid-template-columns: 1fr 1fr/);
});

test('the page never reaches into another web tree for them', () => {
  for (const f of ['../state/kart-bars.js', '../hud/bars.js']) assert.doesNotMatch(read(f), /fetch\(|from '[^']*glassroad|import\('/, f);
});

const SIM = new URL('../../glassroad/sim/kartStats.js', import.meta.url);
test('parity: the copied table is what Glassroad computes', { skip: existsSync(SIM) ? false : 'glassroad/sim/kartStats.js reaches this trunk with the Glassroad catch-up' }, async () => {
  const { STAT_KEYS, TYPE_LABELS, statBars } = await import(SIM.href);
  const feel = JSON.parse(read('../../glassroad/feel.json'));
  const ids = Object.keys(feel.karts.bodies);
  const want = Object.fromEntries(ids.map((id) => {
    const { speed, accel, handling, stability, type } = statBars(feel, id);
    return [id, { speed, accel, handling, stability, type }];
  }));
  assert.deepEqual(STATS(), STAT_KEYS);
  assert.deepEqual({ ...TYPE_WORDS }, { ...TYPE_LABELS });
  assert.deepEqual(KART_BARS, want, 'state/kart-bars.js KART_BARS has drifted from Glassroad. Paste:\n' + JSON.stringify(want));
});
