/* Themed text (owner 2026-11-10): the picked theme's lines mix into the plain lists, and the Bambi Cloud label turns. */
import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { THEME_TEXT, BAMBI_TRIGGERS } from '../data/content/theme-text.js';
import { FALLING_WORDS, GOLDEN, THEME_STRAY_WORDS, THEME_IDS } from '../data/content/index.js';
import { THEME_SHARE, runThemes, themeLines, themed } from '../ui/theme-text.js';
import { CLOUD_FIRST, CLOUD_LABELS, CLOUD_LOGINS, CLOUD_GAP_MS, cloudGap, nextCloudLabel } from '../data/cloud-lines.js';

const LISTS = ['bounce', 'big', 'edge', 'still', 'lucky', 'falling', 'missed', 'boxLabels', 'boxOpened', 'boxExpired', 'away', 'drift', 'praise', 'sweet'];
const SOURCE = 'C:/Projects/Conditioning-Control-Panel---CSharp-WPF/handoff-1110/tc-theme-text/text-v1.json';
/** A small seeded generator, so the share checks never flake. */
const seeded = (seed) => () => ((seed = (seed * 1664525 + 1013904223) >>> 0) / 2 ** 32);
const run = (f5, taste2) => ({ picks: f5 ? { f5 } : {}, owned: taste2 ? { taste2: 1 } : {}, store: taste2 ? { taste2: { theme: taste2 } } : {} });

test('every theme has every key, with no empty list or line', () => {
  assert.deepEqual(Object.keys(THEME_TEXT).sort(), [...THEME_IDS].sort());
  for (const [id, t] of Object.entries(THEME_TEXT)) {
    for (const key of LISTS) {
      assert.ok(Array.isArray(t[key]) && t[key].length, `${id}.${key}`);
      for (const s of t[key]) assert.ok(typeof s === 'string' && s.trim(), `${id}.${key}`);
    }
    assert.equal(t.falling.length, FALLING_WORDS.length, `${id}.falling`);
    assert.deepEqual(Object.keys(t.golden).sort(), GOLDEN.prizes.map((p) => p.key).sort(), `${id}.golden`);
    for (const s of Object.values(t.golden)) assert.ok(s.trim(), `${id}.golden`);
  }
});

test('the copy keeps the house voice; gendered words only in the Bimbo (Bambi) theme', () => {
  for (const [id, t] of Object.entries(THEME_TEXT)) {
    const all = Object.values(t).flatMap((v) => (Array.isArray(v) ? v : Object.values(v))).join(' / ');
    assert.doesNotMatch(all, /[\u2013\u2014!]/, id);
    if (id !== 'bimbo') assert.doesNotMatch(all, /\b(girl|boy|she|her|he|him|his)\b/i, id);
  }
});

test('before Pick a Theme every pick is the plain one', () => {
  assert.deepEqual(runThemes(run()), []);
  for (const key of LISTS) assert.equal(themed([], key, 'plain', { rand: () => 0 }), 'plain');
  assert.equal(themed(runThemes(run()), 'bounce', 'DROP'), 'DROP');
});

test('after the pick about 60% are theme lines, 40% plain', () => {
  assert.equal(THEME_SHARE, 0.6);
  const themes = runThemes(run('goon')), pool = new Set(THEME_TEXT.goon.bounce), rand = seeded(7);
  let hit = 0;
  for (let i = 0; i < 4000; i++) { const w = themed(themes, 'bounce', 'PLAIN', { rand }); if (w !== 'PLAIN') { assert.ok(pool.has(w), w); hit += 1; } }
  assert.ok(hit / 4000 > 0.56 && hit / 4000 < 0.64, `share ${hit / 4000}`);
});

test('a Second Theme joins the theme side, and is read fresh on every call', () => {
  assert.deepEqual(runThemes(run('trance', 'drone')), ['trance', 'drone']);
  assert.deepEqual(runThemes(run('trance', 'trance')), ['trance']);
  assert.deepEqual(runThemes({ picks: { f5: 'trance' }, owned: {}, store: { taste2: { theme: 'drone' } } }), ['trance'], 'taste2 not owned');
  assert.deepEqual(runThemes(run('nope')), []);
  const seen = new Set(), rand = seeded(3);
  for (let i = 0; i < 2000; i++) seen.add(themed(runThemes(run('trance', 'drone')), 'big', 'X', { rand }));
  assert.ok(THEME_TEXT.trance.big.some((w) => seen.has(w)) && THEME_TEXT.drone.big.some((w) => seen.has(w)));
  const state = run();
  assert.equal(themed(runThemes(state), 'big', 'X', { rand: () => 0 }), 'X');
  state.picks.f5 = 'censored';   // a pick mid-run: the next draw already follows it
  assert.equal(themed(runThemes(state), 'big', 'X', { rand: () => 0 }), THEME_TEXT.censored.big[0]);
});

test('Falling Words keep their effects; only the word changes, by index', () => {
  assert.deepEqual(FALLING_WORDS.map((w) => w.effect), ['crit', 'mute', 'speed']);
  assert.deepEqual(FALLING_WORDS.map((w) => w.word), ['FOCUS', 'BLANK', 'DEEPER']);
  for (let i = 0; i < 3; i++) {
    assert.deepEqual(themeLines(['infection', 'bimbo'], 'falling', i), [THEME_TEXT.infection.falling[i], THEME_TEXT.bimbo.falling[i]]);
    assert.equal(themed(['locked'], 'falling', FALLING_WORDS[i].word, { sub: i, rand: () => 0 }), THEME_TEXT.locked.falling[i]);
  }
});

test('Golden lines key by prize; fits filters theme lines like the plain list', () => {
  for (const p of GOLDEN.prizes) assert.equal(themed(['sissy'], 'golden', p.line, { sub: p.key, rand: () => 0 }), THEME_TEXT.sissy.golden[p.key]);
  assert.equal(themed(['sissy'], 'praise', 'plain', { fits: () => false, rand: () => 0 }), 'plain');
});

test('Bimbo carries the Bambi words, the triggers about half of its word banks', () => {
  const b = THEME_TEXT.bimbo;
  for (const w of ['BAMBI', 'GOOD GIRL']) assert.ok(b.bounce.includes(w) && b.big.includes(w), w);
  assert.ok(b.edge.includes('bambi') && b.edge.includes('good girl') && b.still.includes('good girl, bambi') && b.lucky.includes('bambi'));
  assert.ok(b.praise.includes('Good girl, Bambi.') && b.praise.includes('Bambi sleeps so nicely.'));
  for (const [key, cased] of [['bounce', (t) => t.toUpperCase()], ['big', (t) => t.toUpperCase()], ['edge', (t) => t.toLowerCase()], ['still', (t) => t.toLowerCase()]]) {
    const n = BAMBI_TRIGGERS.filter((t) => b[key].includes(cased(t))).length;
    assert.equal(n, BAMBI_TRIGGERS.length, key);
    assert.ok(n / b[key].length >= 0.45, `${key} ${n}/${b[key].length}`);
  }
  for (const t of BAMBI_TRIGGERS) assert.ok(THEME_STRAY_WORDS.bimbo.includes(t.toUpperCase()), t);
  for (const id of Object.keys(THEME_TEXT).filter((x) => x !== 'bimbo')) {
    assert.doesNotMatch(Object.values(THEME_TEXT[id]).flat().join(' '), /\bbambi\b/i, id);
  }
});

test('the data matches the approved JSON when it is around', { skip: !existsSync(SOURCE) && 'handoff JSON not on this machine' }, () => {
  const json = JSON.parse(readFileSync(SOURCE, 'utf8'));
  assert.deepEqual(JSON.parse(JSON.stringify(THEME_TEXT)), json.themes);
  assert.deepEqual([...BAMBI_TRIGGERS], json.bambiTriggers);
});

test('Bambi Cloud labels: logins plus every trigger, never wider than the first, never the same twice running', () => {
  assert.equal(CLOUD_LABELS[0], CLOUD_FIRST);
  assert.deepEqual(CLOUD_LABELS, [...CLOUD_LOGINS, ...BAMBI_TRIGGERS]);
  for (const l of CLOUD_LABELS) {
    assert.ok(l.length <= CLOUD_FIRST.length, l);   // measured 2026-11-10: the widest, Bambi Cum and Collapse, is 170 px of the 171.5
    assert.doesNotMatch(l, /[\u2013\u2014!]/, l);
  }
  const rand = seeded(11), set = new Set(CLOUD_LABELS);
  let cur = CLOUD_FIRST;
  for (let i = 0; i < 500; i++) { const next = nextCloudLabel(cur, rand); assert.ok(set.has(next) && next !== cur, next); cur = next; }
  assert.equal(nextCloudLabel(CLOUD_FIRST, () => 0.999999), CLOUD_LABELS.at(-1));
  for (const r of [0, 0.5, 0.999]) { const g = cloudGap(() => r); assert.ok(g >= CLOUD_GAP_MS[0] && g <= CLOUD_GAP_MS[1], g); }
});
