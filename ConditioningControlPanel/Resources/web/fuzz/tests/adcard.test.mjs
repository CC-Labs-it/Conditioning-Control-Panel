import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, existsSync } from 'node:fs';
import { cardColours, cardModel, contrast, fillLine, timeText, barWidths, llamaLayout, tally, LLAMA_MIN, LLAMA_MAX } from '../ui/adcard/model.js';
import { PORTRAITS, SPRITES } from '../ui/adcard/portraits.js';
import { ADCARD_LINES, ADCARD_COPY } from '../data/content/adcard.js';
import { THEME_IDS } from '../ui/palette.js';
import { createLife, serialize, parse } from '../sim/save.js';

const asset = (name) => new URL('../assets/adcard/' + name, import.meta.url);

test('every theme has a living portrait with its files, and four lines', () => {
  for (const t of THEME_IDS) {
    const P = PORTRAITS[t];
    assert.ok(P, t);
    assert.ok(existsSync(asset(t + '.webp')), t + '.webp');
    assert.ok(P.layers.length >= 3, t + ' turns its eyes and screen');
    for (const L of P.layers) {
      assert.ok(existsSync(asset(`${t}-${L.name}.webp`)), `${t}-${L.name}.webp`);
      assert.ok(L.R > 0 && L.sec !== 0 && L.sy > 0, `${t} ${L.name}`);
    }
    assert.equal(P.orbit.length, 5);
    assert.equal(ADCARD_LINES[t].length, 4, t);
    assert.ok(ADCARD_COPY.themes[t], t);
  }
  for (const name of Object.keys(SPRITES)) assert.ok(existsSync(asset('sprite-' + name + '.webp')), name);
  assert.ok(existsSync(asset('logo.webp')));
});

test('lines keep the copy rules: no em or en dashes, no exclamation marks, no she/he', () => {
  const all = Object.values(ADCARD_LINES).flat(2).join('\n');
  assert.doesNotMatch(all, /[–—!]/);
  assert.doesNotMatch(all, /\b(she|her|he|him|his)\b/i);
});

test('colours: accents read on white, every pair, and ACCEPTED stays clear of both accents', () => {
  for (const f of THEME_IDS) {
    for (const s of [null, ...THEME_IDS.filter((x) => x !== f)]) {
      const c = cardColours(f, s);
      assert.ok(contrast(c.A, '#ffffff') >= 3.3, `${f}+${s} A`);
      assert.ok(contrast(c.B, '#ffffff') >= 3.3, `${f}+${s} B`);
      assert.ok(contrast(c.hot, c.ink) >= 3, `${f}+${s} ACCEPTED on its label`);
      assert.equal(c.spiral.length, 4);
    }
  }
  assert.equal(cardColours('bimbo', 'trance').hot, '#ffe14d', 'yellow like the approved card');
});

test('a line fills its tokens and escapes what it is given', () => {
  const html = fillLine('<a>{name}</a> said <hl>"one click"</hl>. That was <b>{time}</b> ago.', { name: '<img src=x>', time: '41 minutes' });
  assert.equal(html, '<span class="a">&lt;img src=x&gt;</span> said <span class="hl">"one click"</span>. That was <span class="b">41 minutes</span> ago.');
});

test('time, bars and llamas', () => {
  assert.equal(timeText(0), '1 minute');
  assert.equal(timeText(41 * 60000), '41 minutes');
  assert.equal(timeText(60 * 60000), '1 hour');
  assert.equal(timeText(72 * 60000), '1 hour 12');
  assert.equal(timeText(150 * 60000), '2 hours 30');
  const w = barWidths({ pics: 0, total: 0, golden: 0 });
  assert.deepEqual(w, { pics: 6, fuzz: 6, golden: 6 });
  assert.equal(barWidths({ pics: 1e6, total: 1e12, golden: 1e3 }).pics, 100);
  for (let n = LLAMA_MIN; n <= LLAMA_MAX; n++) {
    const L = llamaLayout(n);
    assert.ok(n * 1.37 * L.font + (n - 1) * L.gap <= 38.5, `${n} llamas fit one row`);
  }
});

test('the model reads the run: themes, tallies, name or a signee number, llamas clamped', () => {
  const run = { picks: { f5: 'sissy' }, store: { taste2: { theme: 'locked' }, adcard: { pics: 588, golden: 31, terms: 188 } }, seed: 77, clock: 27 * 60000, total: 2950400 };
  const m = cardModel({ run, life: { llamas: 40 }, name: 'sleepyfox' });
  assert.equal(m.first, 'sissy');
  assert.equal(m.second, 'locked');
  assert.deepEqual(m.labels, ['Sissy', 'Locked']);
  assert.equal(m.terms, 188);
  assert.deepEqual(m.stats.map((s) => s.n), [588, 2950400, 31]);
  assert.equal(m.llamas, LLAMA_MAX);
  assert.ok(m.head.includes('sleepyfox') || m.sub.includes('sleepyfox') || !/\{name\}/.test(ADCARD_LINES.sissy[(77 >>> 3) % 4].join('')));
  const solo = cardModel({ run: { picks: { f5: 'drone' }, store: {}, seed: 0, clock: 0, total: 0 }, life: {} });
  assert.equal(solo.second, null);
  assert.deepEqual(solo.labels, ['Drone']);
  assert.equal(solo.llamas, LLAMA_MIN);
  assert.doesNotMatch(solo.head + solo.sub, /\{\w+\}/, 'no token left unfilled');
});

test('tallies count into the run store and travel with the save; the herd travels with the life', () => {
  const run = { store: {} };
  tally(run, 'pics', 3); tally(run, 'pics'); tally(run, 'golden'); tally(run, 'terms', 0);
  assert.deepEqual(run.store.adcard, { pics: 4, golden: 1 });
  const life = createLife();
  assert.equal(life.llamas, 3);
  life.llamas = 9;
  const back = parse(serialize(null, life));
  assert.equal(back.life.llamas, 9);
  life.llamas = 99;
  assert.equal(parse(serialize(null, life)).life.llamas, 12);
});

test('the card stylesheet stays scoped under .adc', () => {
  const css = readFileSync(new URL('../ui/adcard/card.css', import.meta.url), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');
  const selectors = [...css.matchAll(/(^|\})\s*([^{}@]+)\{/g)].map((m) => m[2].trim()).filter((s) => !/^(from|to|\d+%)/.test(s));
  for (const sel of selectors) for (const part of sel.split(',')) assert.match(part.trim(), /^\.adc/, `unscoped selector: ${part.trim()}`);
});
