// The game shelves' emblems (sheet/section-art.js): Chess and Kart heads wear a drawn emblem in place of the little star,
// every other shelf keeps the star; the emblems are decorative, network-free SVG; the badge pictures are valid image files.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { THEMED, emblemSvg, emblemUri, sectionEmblem, lendEmblems } from '../sheet/section-art.js';
import { createView } from '../sheet/view.js';
import { fakeDocument } from './fake-dom.mjs';

const CSS = readFileSync(new URL('../sheet.css', import.meta.url), 'utf8');
const JUICE = readFileSync(new URL('../sheet-juice.css', import.meta.url), 'utf8');

test('Chess and Kart have an emblem, other shelves none', () => {
  assert.deepEqual([...THEMED], ['chess', 'kart']);
  for (const k of THEMED) assert.match(emblemSvg(k), /^<svg [^>]*aria-hidden="true"/);
  for (const k of ['effects', 'profile', 'desk', 'prime', 'backroom', 'q', 'owned', '', undefined, 'constructor', 'toString']) {
    assert.equal(emblemSvg(k), null, String(k));
    assert.equal(emblemUri(k), null, String(k));
  }
});

test('the emblems draw everything themselves: no words, no network, no script', () => {
  for (const k of THEMED) {
    const s = emblemSvg(k);
    assert.ok(!/<text|<image|href=|https?:|<script|on[a-z]+=/i.test(s), k);
    assert.ok(s.includes('class="emb-body"'), k + ' has a body for the idle life');
  }
  assert.ok(emblemSvg('kart').includes('emb-wheel') && emblemSvg('kart').includes('emb-puff'));
  assert.ok(emblemSvg('chess').includes('emb-spark'));
});

test('the badge picture is a standalone SVG file in a data: URI (namespace, no sparkle)', () => {
  for (const k of THEMED) {
    const u = emblemUri(k);
    assert.ok(u.startsWith('url("data:image/svg+xml,') && u.endsWith('")'), k);
    const file = decodeURIComponent(u.slice('url("data:image/svg+xml,'.length, -2));
    assert.ok(file.includes('xmlns="http://www.w3.org/2000/svg"'), k);
    assert.ok(!file.includes('emb-spark') && !file.includes('\n') && !file.includes('")'), k);
  }
});

test('sectionEmblem makes a decorative span, and nothing for an untouched shelf', () => {
  const doc = fakeDocument();
  const n = sectionEmblem(doc, 'chess');
  assert.equal(n.className, 'emb emb-chess');
  assert.equal(n.getAttribute('aria-hidden'), 'true');
  assert.equal(n.innerHTML, emblemSvg('chess'));
  assert.equal(sectionEmblem(doc, 'profile'), null);
  assert.equal(sectionEmblem(null, 'chess'), null);
});

test('lendEmblems sets the two badge pictures, and is quiet without styles', () => {
  const set = {};
  lendEmblems({ style: { setProperty: (k, v) => { set[k] = v; } } });
  assert.deepEqual(Object.keys(set).sort(), ['--emb-chess', '--emb-kart']);
  assert.equal(set['--emb-kart'], emblemUri('kart'));
  assert.doesNotThrow(() => { lendEmblems({}); lendEmblems(null); });
});

test('a shelf head: Chess and Kart wear the emblem where the star sits, others keep the star', () => {
  const doc = fakeDocument();
  const view = createView(doc, { t: (k) => k, sheet: false, win: null });
  for (const [key, emb] of [['chess', true], ['kart', true], ['profile', false], ['effects', false]]) {
    const h = view.sectionHead({ key, title: key, meta: null, places: null }, { pv: null });
    assert.equal(h.classList.contains('has-emb'), emb, key);
    assert.equal(h.children[0].classList.contains(emb ? 'emb' : 'sq'), true, key);
    assert.equal(h.children[1].tagName, 'H2', key + ': the sign stays second');
  }
});

test('the themes and the motion are wired in the stylesheets, and gated', () => {
  for (const k of THEMED) {
    assert.ok(CSS.includes(`.sec.fam-${k}::before`), k + ' ground');
    assert.ok(CSS.includes(`var(--emb-${k})`), k + ' badge');
  }
  // the movement is the juice's, scaled by --j-amp (Gentle 0, less and reduced motion half); Gentle stops the puffs
  assert.ok(/@keyframes j-knight[\s\S]*?var\(--j-amp\)/.test(JUICE));
  assert.ok(/@keyframes j-rumble[\s\S]*?var\(--j-amp\)/.test(JUICE));
  assert.ok(JUICE.includes('.shroot[data-gentle="on"] .emb-puff { animation: none; }'));
  assert.ok(/prefers-reduced-motion[^{]*\{\s*\.emb-chess/.test(JUICE));
  // the juice moves only: no colours in its emblem block
  const block = JUICE.slice(JUICE.indexOf("the game shelves' emblems"));
  assert.ok(!/(^|[;{\s])(color|background|border|font)\s*:/.test(block));
});
