// Named llamas (owner 2026-11-21, handoff-1121/llamas/BRIEF.md): twelve llamas with names, portraits and one-liners,
// drawn in the pen as code icons that spit now and then, and the real pictures in The Panel ending.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, existsSync, statSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { LLAMAS, LLAMA_BY_ID, PANEL_SEATS, herdRoster, llamaPortrait } from '../data/content/llamas.js';
import { penModel, llamaSvg, PROPS, spitDelay, spitAllowed, SPIT_MIN_MS, SPIT_MAX_MS, PEN_MAX } from '../ui/llama-pen.js';
import { ART, OWED } from '../ui/unlocks/fate/llama/art.js';
import { STICKER_BY_ID } from '../data/content/stickers.js';
import { ENDINGS } from '../data/content/endings.js';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');

/** Width and height of a webp from its header (lossy VP8, lossless VP8L or extended VP8X). */
function webpSize(file) {
  const b = readFileSync(join(ROOT, file));
  assert.equal(b.toString('ascii', 0, 4) + b.toString('ascii', 8, 12), 'RIFFWEBP', file);
  const kind = b.toString('ascii', 12, 16);
  if (kind === 'VP8X') return [1 + b.readUIntLE(24, 3), 1 + b.readUIntLE(27, 3)];
  if (kind === 'VP8L') { const v = b.readUInt32LE(21); return [1 + (v & 0x3fff), 1 + ((v >> 14) & 0x3fff)]; }
  return [b.readUInt16LE(26) & 0x3fff, b.readUInt16LE(28) & 0x3fff];
}

test('the herd order: the panel shelters first, then the rest as the herd grows to twelve', () => {
  assert.deepEqual(LLAMAS.map((l) => l.id), ['reginald', 'margaret', 'henry', 'greg', 'bob', 'lashes', 'kevin', 'spitz', 'gary', 'doctor', 'bartholomew', 'nana']);
  assert.deepEqual(LLAMAS.slice(0, 3).map((l) => l.role), ['chair', 'panel', 'panel']);
  assert.equal(LLAMA_BY_ID.reginald.name, 'Sir Reginald the Third of Hoofelton');
  assert.equal(LLAMA_BY_ID.henry.name, 'Henry the Sex Machine');
  assert.deepEqual(herdRoster(3).map((l) => l.id), ['reginald', 'margaret', 'henry']);
  assert.deepEqual(herdRoster(5).map((l) => l.id).slice(3), ['greg', 'bob']);
  assert.equal(herdRoster(40).length, 12);
  assert.equal(herdRoster('x').length, 0);
  assert.deepEqual([...PANEL_SEATS].sort(), ['henry', 'margaret', 'reginald']);
});

test('the pen shows the named herd in order, every prop drawn', () => {
  for (const n of [3, 7, PEN_MAX]) {
    const m = penModel({ llamas: n }, n);
    assert.deepEqual(m.herd.map((l) => l.id), herdRoster(n).map((l) => l.id));
  }
  for (const l of LLAMAS) {
    if (l.prop) assert.ok(PROPS[l.prop], `${l.id} wears ${l.prop}`);
    const svg = llamaSvg(l.prop);
    assert.match(svg, /^<svg class="lp-ic"[^>]*aria-hidden="true">/);
    assert.ok(svg.includes('class="lp-head"') && svg.includes('class="lp-drop"'), 'a head to tilt and a droplet to spit');
    assert.ok(svg.endsWith('</svg>'));
  }
  assert.deepEqual(LLAMAS.map((l) => l.prop), ['monocle', 'pearls', 'chain', 'lanyard', 'hardhat', 'tiara', 'headset', 'cap', 'visor', 'stethoscope', 'sweater', 'scarf'], 'one small item each');
  assert.equal(llamaSvg(null), llamaSvg('nope'), 'an unknown prop draws a plain llama');
});

test('a spit every 7 to 13 s, never in Gentle, reduced motion, pause or a hidden tab', () => {
  assert.equal(spitDelay(0), SPIT_MIN_MS);
  assert.ok(spitDelay(0.9999) < SPIT_MAX_MS && spitDelay(0.9999) > 12900);
  assert.equal(spitDelay(-3), SPIT_MIN_MS);
  assert.equal(spitDelay(NaN), SPIT_MIN_MS);
  assert.equal(spitAllowed({}), true);
  assert.equal(spitAllowed({ gentle: true }), false);
  assert.equal(spitAllowed({ frozen: true }), false);
  assert.equal(spitAllowed({ hidden: true }), false);
  const css = readFileSync(join(ROOT, 'styles.css'), 'utf8');
  assert.ok(css.includes('body.gentle .lp-spit .lp-head, body.gentle .lp-spit .lp-drop { animation: none; }'));
  assert.ok(css.includes('@media (prefers-reduced-motion: reduce) { .lp-spit .lp-head, .lp-spit .lp-drop { animation: none; } }'));
});

test('portraits, ending pictures, sticker and chair are in the tree at their sizes', () => {
  for (const l of LLAMAS) assert.deepEqual(webpSize(llamaPortrait(l.id)), [384, 384], l.id);
  assert.deepEqual(webpSize(ART.panel), [1376, 768]);
  assert.deepEqual(webpSize(ART.chew), [640, 794]);
  assert.deepEqual(webpSize(ART.spit), [640, 794]);
  assert.deepEqual(webpSize(ART.sticker), [512, 512]);
  assert.deepEqual(webpSize(ART.chair), [160, 160]);
  for (const o of OWED) assert.ok(existsSync(join(ROOT, o.file)), o.file);
  assert.equal(STICKER_BY_ID.e_ll_end.art.src, ART.sticker);
  const bytes = [...LLAMAS.map((l) => llamaPortrait(l.id)), ...Object.values(ART)].reduce((a, f) => a + statSync(join(ROOT, f)).size, 0);
  assert.ok(bytes < 640 * 1024, `new llama pictures weigh ${bytes} bytes`);
});

test('copy voice: no dashes or exclamation marks in the names, lines and the ending credits', () => {
  for (const l of LLAMAS) for (const s of [l.name, l.line]) assert.doesNotMatch(s, /[–—!]/, s);
  for (const s of ENDINGS.ll_end.credits || []) assert.doesNotMatch(String(s), /[–—!]/, s);
});
