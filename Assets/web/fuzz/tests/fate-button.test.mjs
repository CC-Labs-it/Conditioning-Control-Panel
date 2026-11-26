// Living Button fate cards (handoff-1110/TC-FATE-CARDS-BRIEF.md): ft_depth, ft_cursor, ft_pressed, ft_glass.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  LEAN, normPointer, depthOf, leanFor, drift, approach, leanCss,
  CURSOR, nextVisitMs, pressCount, edgePoint, bendPoint, curve, smooth, aimOffset, cursorSvg,
  FLINCH, mayFlinch, GLASS, nextSheenMs,
} from '../ui/unlocks/fate/button/model.js';
import { BY_ID } from '../data/items.js';

const tree = join(dirname(fileURLToPath(import.meta.url)), '..');
const seq = (...v) => { let i = 0; return () => v[i++ % v.length]; };

test('the four cards are the button set, in order, and each has an entry', async () => {
  const { default: group } = await import('../ui/unlocks/fate/button.js');
  const ids = ['ft_depth', 'ft_cursor', 'ft_pressed', 'ft_glass'];
  ids.forEach((id, i) => {
    assert.equal(BY_ID[id].fate, 'button');
    assert.equal(BY_ID[id].step, i + 1);
    assert.equal(typeof group[id]?.mount, 'function', id);
  });
  assert.deepEqual(Object.keys(group).sort(), [...ids].sort());
});

test('a lean is a few pixels and under a degree, away from the pointer, scaled by depth', () => {
  const corner = leanFor(1, 1, 1);
  assert.ok(Math.abs(corner.x) <= 4 && Math.abs(corner.y) <= 3 && Math.abs(corner.r) <= 1);
  assert.ok(corner.x < 0 && corner.y < 0 && corner.r > 0);
  const half = leanFor(1, 1, 0.5);
  assert.ok(Math.abs(half.x - corner.x / 2) < 1e-9);
  assert.deepEqual(leanFor(9, -9, 3), leanFor(1, -1, 1));   // clamped
  const rest = leanCss(leanFor(0, 0, 1));
  assert.equal(rest.translate, '0px 0px');
  assert.equal(rest.rotate, '0deg');
});

test('pointer normalises to -1..1 and an empty page reads centre', () => {
  assert.deepEqual(normPointer(0, 0, 200, 100), { nx: -1, ny: -1 });
  assert.deepEqual(normPointer(100, 50, 200, 100), { nx: 0, ny: 0 });
  assert.deepEqual(normPointer(500, -5, 200, 100), { nx: 1, ny: -1 });
  assert.deepEqual(normPointer(5, 5, 0, 0), { nx: 0, ny: 0 });
});

test('neighbouring objects sit at different depths, all between 0.45 and 1', () => {
  const d = Array.from({ length: LEAN.maxObjects }, (_, i) => depthOf(i));
  for (const v of d) assert.ok(v >= 0.45 && v <= 1);
  for (let i = 1; i < d.length; i++) assert.ok(Math.abs(d[i] - d[i - 1]) > 0.05);
});

test('the touch drift is very slow and small', () => {
  let maxStep = 0, prev = drift(0);
  for (let t = 0.1; t < 60; t += 0.1) {
    const p = drift(t);
    assert.ok(Math.abs(p.nx) <= 0.55 && Math.abs(p.ny) <= 0.45);
    maxStep = Math.max(maxStep, Math.abs(p.nx - prev.nx), Math.abs(p.ny - prev.ny));
    prev = p;
  }
  assert.ok(maxStep < 0.02, `drift moves ${maxStep} per 100 ms`);
});

test('easing is frame-rate independent', () => {
  let a = 0;
  for (let i = 0; i < 60; i++) a = approach(a, 1, 1 / 60);
  const b = approach(0, 1, 1);
  assert.ok(Math.abs(a - b) < 1e-9);
  assert.equal(approach(0.3, 1, 0), 0.3);
});

test('the second cursor comes now and then: tens of seconds apart, irregular, first soon after the buy', () => {
  assert.equal(nextVisitMs(() => 0, { first: true }), CURSOR.firstMs[0]);
  assert.equal(nextVisitMs(() => 1, { first: true, restored: true }), CURSOR.restoredMs[1]);
  assert.ok(nextVisitMs(() => 0) >= 20000);
  assert.ok(nextVisitMs(() => 0.999) <= 65000);
  assert.notEqual(nextVisitMs(() => 0.1), nextVisitMs(() => 0.9));
});

test('it presses one to three times, never faster than 3 Hz', () => {
  assert.equal(pressCount(() => 0), 1);
  assert.equal(pressCount(() => 0.5), 2);
  assert.equal(pressCount(() => 0.99), 3);
  assert.ok(CURSOR.pressGapMs[0] > 1000 / 3);
  assert.ok(CURSOR.calmPressGapMs > 1000 / 3);
});

test('it enters and leaves off screen, by different edges', () => {
  const w = 800, h = 600;
  for (let side = 0; side < 4; side++) {
    const r = seq(side / 4 + 0.01, 0.5);
    const p = edgePoint(r, w, h);
    assert.equal(p.side, side);
    assert.ok(p.x < 0 || p.x > w || p.y < 0 || p.y > h);
    const out = edgePoint(seq(side / 4 + 0.01, 0.4, 0.5), w, h, side);
    assert.notEqual(out.side, side);
  }
});

test('its path is a gentle bend that starts and ends where it should', () => {
  const a = { x: 0, y: 0 }, b = { x: 100, y: 0 };
  const c = bendPoint(() => 1, a, b);
  assert.ok(Math.abs(c.y) <= 30);
  assert.deepEqual(curve(a, c, b, 0), a);
  assert.deepEqual(curve(a, c, b, 1), b);
  assert.equal(smooth(0), 0); assert.equal(smooth(1), 1); assert.equal(smooth(0.5), 0.5);
  const aim = aimOffset(() => 1);
  assert.ok(Math.abs(aim.fx) <= 0.25 && Math.abs(aim.fy) <= 0.2);
});

test('the cursor is drawn in code, with a shadow that does not turn with it', () => {
  const svg = cursorSvg({ edge: '#123456' });
  assert.match(svg, /^<svg /);
  assert.match(svg, /#123456/);
  assert.equal((svg.match(/<path /g) || []).length, 2);
  assert.ok(!/<image|href=/.test(svg));
});

test('Pressed: Gentle and reduced motion flinch at most once every 520 ms (under 2 Hz)', () => {
  assert.equal(mayFlinch(0, 400, { calm: true }), false);
  assert.equal(mayFlinch(0, FLINCH.calmGapMs, { calm: true }), true);
  assert.ok(1000 / FLINCH.calmGapMs < 3);
  assert.ok(FLINCH.calmMs >= 250);
  assert.equal(mayFlinch(0, 100), true);
  assert.equal(mayFlinch(0, 50), false);
  assert.equal(mayFlinch(0, 150, { lite: true }), false);
  assert.ok(FLINCH.dipPx <= 2 && FLINCH.litePx <= 2);
});

test('Glass: a sheen every 20 to 30 seconds', () => {
  assert.equal(nextSheenMs(() => 0), GLASS.sheenMs[0]);
  assert.equal(nextSheenMs(() => 1), GLASS.sheenMs[1]);
  assert.ok(GLASS.sheenMs[0] >= 20000 && GLASS.sheenMs[1] <= 30000);
  assert.ok(nextSheenMs(() => 1, true) < 5000);
});

test('the widgets keep to the house rules: ctx clocks, cosmetic Math.random, no ctx.rng, no images, no #page', () => {
  const src = readFileSync(join(tree, 'ui', 'unlocks', 'fate', 'button.js'), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');
  for (const bad of ['ctx.rng', 'setTimeout', 'setInterval', 'requestAnimationFrame', 'getBoundingClientRect', 'new Audio', "'#page'", 'querySelector(\'#shop', '.png', '.webp']) {
    assert.ok(!src.includes(bad), `button.js uses ${bad}`);
  }
  assert.ok(src.includes('Math.random'));
  assert.ok(src.includes('pointer-events:none'));
  assert.ok(src.includes('ctx.button.press()'));
  assert.ok(src.includes('ctx.gentle || ctx.reduced'));
});
