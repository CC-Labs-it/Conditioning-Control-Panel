// The kart's stat tag in the garage: four rows of 1..7 pips and a type word, dark karts included, and it stays out of the
// menu's way (no button, no focus, no animation when calm). The numbers belong to sim/kartStats.js; a stub stands in
// here, and the last test holds the tag to the real module when the table is in the tree.
import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { existsSync, readFileSync } from 'node:fs';
import { barsMarkup, barsLine, paintKartBars, pipCount, PIPS, STAT_ROWS } from '../hud/kartBars.js';
import { KARTS, DEFAULT } from '../hud/garageState.js';

const read = (rel) => readFileSync(new URL(rel, import.meta.url), 'utf8').replace(/\r\n/g, '\n');
const stub = 'data:text/javascript,' + encodeURIComponent('export const createTrackPicker=()=>({refreshMedia(){},nav(){},show(){},dispose(){},open:false});');
const hooks = registerHooks({ resolve(specifier, context, next) {
  if ((context.parentURL || '').endsWith('/hud/garage.js') && specifier === './trackPicker.js') return { url: stub, shortCircuit: true };
  return next(specifier, context);
} });
const { garageUi } = await import('../hud/garage.js');
hooks.deregister();

const pips = () => Array.from({ length: PIPS }, () => {
  const classes = new Set();
  return { classes, classList: { toggle(k, on) { on ? classes.add(k) : classes.delete(k); } } };
});
function fakeTag() {
  const slots = new Map(), rows = new Map(), classes = new Set(), attrs = {};
  return { hidden: true, classes, attrs, rows, slots, innerHTML: '',
    classList: { toggle(k, on) { on ? classes.add(k) : classes.delete(k); } },
    setAttribute(k, v) { attrs[k] = v; },
    querySelector(s) { if (!slots.has(s)) slots.set(s, {}); return slots.get(s); },
    querySelectorAll(s) { if (!rows.has(s)) rows.set(s, pips()); return rows.get(s); } };
}
const lit = (tag, key) => tag.rows.get(`[data-stat=${key}] i`).filter((p) => p.classes.has('on')).length;
const BARS = { speed: 5, accel: 1, handling: 2, stability: 1, word: 'Fast' };

test('the markup is four named rows of seven pips, a type word, and nothing a pad could land on', () => {
  const html = barsMarkup();
  assert.deepEqual(STAT_ROWS.map(([, label]) => label), ['Speed', 'Accel', 'Handling', 'Stability']);
  for (const [key, label] of STAT_ROWS) assert.match(html, new RegExp(`data-stat="${key}"><span>${label}</span><span class="g-pips">(<i></i>){${PIPS}}</span>`));
  assert.match(html, /g-bars-type/);
  assert.doesNotMatch(html, /<button|<input|<a |tabindex/i);
});

test('pip counts stay whole and in range, whatever comes in', () => {
  for (const [value, want] of [[0, 1], [1, 1], [3, 3], [7, 7], [9, 7], [-2, 1], [2.6, 3], [NaN, 1], [undefined, 1]]) assert.equal(pipCount(value), want, String(value));
});

test('painting lights the right pips, says the type word, and says it all in words', () => {
  const tag = fakeTag();
  paintKartBars(tag, BARS);
  assert.equal(tag.hidden, false);
  assert.deepEqual(['speed', 'accel', 'handling', 'stability'].map((k) => lit(tag, k)), [5, 1, 2, 1]);
  assert.equal(tag.slots.get('.g-bars-type').textContent, 'Fast');
  assert.equal(tag.slots.get('.g-bars-name').textContent, '');
  assert.equal(tag.classes.has('g-bars-peek'), false);
  assert.equal(tag.attrs['aria-label'], 'Fast. Speed 5 of 7, Accel 1 of 7, Handling 2 of 7, Stability 1 of 7');
  paintKartBars(tag, { ...BARS, speed: 2 }, 'Undertow');
  assert.equal(lit(tag, 'speed'), 2, 'a repaint turns pips back off');
  assert.equal(tag.classes.has('g-bars-peek'), true);
  assert.equal(tag.slots.get('.g-bars-name').textContent, 'Undertow');
  assert.match(tag.attrs['aria-label'], /^Undertow\. Fast\. /);
  assert.equal(barsLine({ speed: 3, accel: 3, handling: 3, stability: 3 }), 'Speed 3 of 7, Accel 3 of 7, Handling 3 of 7, Stability 3 of 7');
});

function fakeGarage(barsOf) {
  const tag = fakeTag(), asked = [], dots = [];
  const element = () => {
    const slots = new Map(), classes = new Set();
    return { hidden: false, dataset: {}, style: { setProperty() {} }, children: [], listeners: {},
      classList: { add: (k) => classes.add(k), remove: (k) => classes.delete(k), contains: (k) => classes.has(k), toggle(k, on) { on ? classes.add(k) : classes.delete(k); }, replace() {} },
      setAttribute() {}, append(child) { this.children.push(child); dots.push(child); }, after() {}, remove() {}, focus() {},
      querySelector(s) { if (s === '.g-bars') return tag; if (!slots.has(s)) slots.set(s, element()); return slots.get(s); },
      querySelectorAll() { return []; },
      addEventListener(name, fn) { (this.listeners[name] ||= []).push(fn); } };
  };
  const oldDocument = globalThis.document;
  globalThis.document = { createElement: element, body: element() };
  const ui = garageUi({ change() {}, select() {}, customize() {}, action() {}, hover() {}, online: false,
    barsOf: barsOf && ((id) => { asked.push(id); return barsOf(id); }) });
  return { ui, tag, asked, dot: (id) => dots.find((d) => d.dataset.kart === id), restore() { globalThis.document = oldDocument; } };
}
const paintOpts = { custom: false, busy: false, locked: new Set(['undertow', 'petal']), owned: undefined };

test('the tag follows the chosen kart; a dark kart shows its bars on a hover or a press, then lets go', (t) => {
  t.mock.timers.enable({ apis: ['setTimeout'] });
  const g = fakeGarage((id) => ({ ...BARS, speed: id === 'undertow' ? 5 : 3, word: id === 'undertow' ? 'Fast' : 'All-rounder' }));
  t.after(() => { g.ui.dispose(); g.restore(); });
  const s = { ...DEFAULT, kart: 'lovebomb' };
  g.ui.paint(s, paintOpts);
  assert.equal(g.tag.slots.get('.g-bars-type').textContent, 'All-rounder');
  assert.equal(g.tag.slots.get('.g-bars-name').textContent, '', 'the chosen kart is named by the title already');

  const dot = g.dot('undertow');
  dot.listeners.pointerenter[0]();
  assert.equal(g.tag.slots.get('.g-bars-type').textContent, 'Fast');
  assert.equal(g.tag.slots.get('.g-bars-name').textContent, 'Undertow');
  assert.equal(lit(g.tag, 'speed'), 5);
  dot.listeners.pointerleave[0]();
  assert.equal(g.tag.slots.get('.g-bars-name').textContent, '');
  assert.equal(lit(g.tag, 'speed'), 3);

  g.ui.lockNote('undertow');   // what a press on a dark kart does (render/garage.js select)
  assert.equal(g.tag.slots.get('.g-bars-name').textContent, 'Undertow');
  t.mock.timers.tick(3200);
  assert.equal(g.tag.slots.get('.g-bars-name').textContent, '');

  g.ui.lockNote('undertow');
  g.ui.paint({ ...s, kart: 'pendulum' }, paintOpts);   // arriving at another kart drops the look at once
  assert.equal(g.tag.slots.get('.g-bars-name').textContent, '');
});

test('every kart gets its bars into the dots\' spoken labels, locked ones included', () => {
  const asked = new Set(), labels = {};
  const g = fakeGarage((id) => { asked.add(id); return BARS; });
  const root = g.ui.root;
  root.querySelectorAll = (s) => s === '[data-kart]' ? KARTS.map(([id]) => ({ dataset: { kart: id }, classList: { toggle() {} }, setAttribute(k, v) { if (k === 'aria-label') labels[id] = v; } })) : [];
  g.ui.paint({ ...DEFAULT, kart: 'lovebomb' }, paintOpts);
  assert.deepEqual([...asked].sort(), KARTS.map(([id]) => id).sort());
  assert.match(labels.undertow, /^Undertow, locked\..*Fast\. Speed 5 of 7/);
  assert.match(labels.lovebomb, /^Lovebomb\. Fast\. Speed 5 of 7/);
  g.ui.dispose(); g.restore();
});

test('without a table the garage draws no tag and still paints', () => {
  const g = fakeGarage(null);
  g.ui.paint({ ...DEFAULT, kart: 'lovebomb' }, paintOpts);
  assert.equal(g.tag.hidden, true);
  g.ui.dispose(); g.restore();
});

test('the tag sits in the selector, keeps out of pad steps, and the page passes the one feel it has', () => {
  assert.match(read('../hud/garage.js'), /<div class="g-selector"><h1><\/h1><div class="g-bars" role="img" hidden><\/div><div class="g-positions"/);
  const nav = read('../hud/garageNav.js');
  assert.doesNotMatch(nav, /g-bars|g-pips|g-positions/);
  assert.match(nav, /'\.g-menu-options button'/);
  assert.match(read('../boot.js'), /createGarage\(\{ art: shared\.garageArt, renderer, canvas, feel,/);
  assert.doesNotMatch(read('../render/garage.js'), /feel\.json/);
});

test('calm means still: no animation on the tag, the fill fade stops under Gentle and reduced motion, phones go two up', () => {
  const css = read('../hud/garage.css'), from = css.indexOf('/* Kart stats'), block = css.slice(from);
  assert.ok(from > 0);
  assert.doesNotMatch(block, /animation|@keyframes/);
  assert.match(block, /#race-garage\.g-calm \.g-pips i\{transition:none\}/);
  assert.match(block, /@media\(prefers-reduced-motion:reduce\)\{\.g-pips i\{transition:none\}/);
  assert.match(block, /@media\(max-width:699px\),\(max-height:650px\)\{[^\n]*\.g-bars-rows\{grid-template-columns:1fr 1fr/);
  assert.match(block, /pointer-events:none/);
});

test('the real table: every body reads as whole pips and a known type word, Lovebomb sits at 4 4 4 4',
  { skip: existsSync(new URL('../sim/kartStats.js', import.meta.url)) ? false : 'sim/kartStats.js lands with feat/rt-kart-stats' }, async () => {
    const { STAT_KEYS, TYPE_LABELS, statBars } = await import('../sim/kartStats.js');
    const feel = JSON.parse(read('../feel.json'));
    assert.deepEqual(STAT_ROWS.map(([key]) => key), STAT_KEYS);
    for (const [id] of KARTS) {
      const bars = statBars(feel, id);
      for (const key of STAT_KEYS) assert.ok(Number.isInteger(bars[key]) && bars[key] >= 1 && bars[key] <= PIPS, `${id} ${key} ${bars[key]}`);
      assert.ok(TYPE_LABELS[bars.type], `${id} ${bars.type}`);
    }
    const { type, ...lovebomb } = statBars(feel, 'lovebomb');
    assert.deepEqual(lovebomb, { speed: 4, accel: 4, handling: 4, stability: 4 });
    assert.equal(type, 'allround');
  });
