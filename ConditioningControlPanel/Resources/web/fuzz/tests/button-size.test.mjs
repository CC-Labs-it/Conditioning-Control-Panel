import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { BIG_MAX, BIG_PAD, SIZE_CSS, bigFit } from '../ui/button-size.js';

/* Big Button grows in place (owner 2026-11-10: "the big button moves things around"). The page is one flex column
 * (#stage then #shop, styles.css); #btn-zone is `flex: none` at a fixed height, so the shop and dock can only move if
 * something changes that height. These checks prove nothing does, without a page: no size rule names the zone or any
 * other layout box, the zone's height is the one variable a big button fills, and a big art face is zoomed to fit inside it. */

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const read = (p) => readFileSync(join(root, p), 'utf8');
const rules = (css) => [...css.replace(/\/\*[\s\S]*?\*\//g, '').matchAll(/([^{}]+)\{([^{}]*)\}/g)].map((m) => ({ sel: m[1].trim(), body: m[2] }));

test('no size rule touches the zone, the stage, the shop, the page or the dock', () => {
  for (const { sel, body } of rules(SIZE_CSS)) {
    assert.doesNotMatch(sel, /#btn-zone|#btn-wrap|#stage|#shop|#page|#dock|#counter|#l-banner|body|#world\s*\{/, sel);
    assert.match(sel, /#btn\[data-size=/, `${sel}: every rule is about the sized button`);
    /* A button that leaves the flow or pushes its neighbours with margins would move them. */
    if (!/::(before|after)/.test(sel)) assert.doesNotMatch(body, /(^|;)\s*(position|margin|top|bottom|float)\s*:/, sel);
  }
});

test('nothing else in the game grows the zone for a size', () => {
  const files = [];
  const walk = (d) => {
    for (const f of readdirSync(join(root, d))) {
      const p = join(d, f);
      if (statSync(join(root, p)).isDirectory()) { if (!/^(tests|dev|node_modules|assets)$/.test(f)) walk(p); }
      else if (/\.(js|css)$/.test(f)) files.push(p);
    }
  };
  walk('ui');
  files.push('styles.css');
  for (const f of files) assert.doesNotMatch(read(f), /#btn-zone[^{,]*data-size/, `${f} sizes the zone by the button size`);
});

test('the zone keeps one height at every breakpoint, and a plain big button fills it', () => {
  const zone = rules(read('styles.css')).filter((r) => /(^|,)\s*#btn-zone\s*$/.test(r.sel));
  assert.ok(zone.length >= 3, 'base rule plus the two breakpoints');
  const base = zone[0].body;
  assert.match(base, /height:\s*var\(--zone-h\)/);
  assert.match(base, /flex:\s*none/);
  for (const r of zone) {
    assert.match(r.body, /--zone-h:\s*\d+px/, 'each breakpoint sets the variable');
    assert.doesNotMatch(r.body.replace(/--zone-h:[^;]*;?/, ''), /(^|;)\s*height\s*:\s*\d/, 'and never a second, fixed height');
  }
  const big = rules(SIZE_CSS).find((r) => r.sel === '#btn[data-size=big]:not([data-art-face])').body;
  assert.match(big, new RegExp(`height:\\s*calc\\(var\\(--zone-h, 108px\\) - ${BIG_PAD}px\\)`));
});

test('a big art face zooms to fit its zone, never past it, never smaller than it was', () => {
  for (const zoneH of [76, 96, 108, 148]) for (const zoneW of [300, 358, 728]) for (const faceW of [40, 96, 150, 310, 700]) for (const faceH of [30, 54, 100, 140, 200]) {
    const z = bigFit({ zoneW, zoneH, faceW, faceH });
    assert.ok(z >= 1 && z <= BIG_MAX, `${z}`);
    if (z > 1) {
      assert.ok(faceH * z <= zoneH - BIG_PAD + 1e-9, `tall: ${faceW}x${faceH} in ${zoneW}x${zoneH} at ${z}`);
      assert.ok(faceW * z <= zoneW - BIG_PAD + 1e-9, `wide: ${faceW}x${faceH} in ${zoneW}x${zoneH} at ${z}`);
    }
  }
  assert.equal(bigFit({ zoneW: 728, zoneH: 108, faceW: 96, faceH: 48 }), 1.7, 'room to spare: the full growth');
  assert.equal(bigFit({ zoneW: 728, zoneH: 108, faceW: 120, faceH: 120 }), 1, 'a face that fills its zone stays');
  assert.equal(bigFit({ zoneW: 0, zoneH: 0, faceW: 0, faceH: 0 }), 1, 'nothing measured yet');
  /* Settles: refitting the same face gives the same zoom. */
  const a = bigFit({ zoneW: 500, zoneH: 108, faceW: 80, faceH: 63 });
  assert.equal(bigFit({ zoneW: 500, zoneH: 108, faceW: 80, faceH: 63 }), a);
});

test('Tiny keeps a finger-sized press area', () => {
  const r = rules(SIZE_CSS);
  assert.match(r.find((x) => x.sel === '#btn[data-size=tiny]:not([data-art-face])::before').body, /width:\s*56px;\s*height:\s*56px/);
  assert.match(r.find((x) => x.sel === '#world #btn[data-size=tiny][data-art-face]').body, /min-height:\s*48px;\s*min-width:\s*48px/);
});

test('look2 wears the shared rules and writes no layout box but the button', () => {
  const src = read('ui/unlocks/basics/look2.js');
  assert.match(src, /from '\.\.\/\.\.\/button-size\.js'/);
  assert.doesNotMatch(src, /const SIZE_CSS/);
  assert.doesNotMatch(src, /zone\.style|stage\.style|#btn-zone\s*[{:]/);
});
