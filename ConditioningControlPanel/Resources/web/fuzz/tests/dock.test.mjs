import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { DOCK_MIN_W, dockIsRail, emptierEdge, railWidth, slotOrder } from '../ui/dock.js';

test('the rail width matches the sum in styles.css and leaves the stage its room', () => {
  assert.equal(railWidth(1099), 0);
  assert.equal(railWidth(1100), 200);
  assert.equal(railWidth(1280), 252);
  assert.equal(railWidth(1920), 300);
  for (const w of [1100, 1280, 1440, 1920]) assert.ok(w - 2 * railWidth(w) >= 700, `stage at ${w}`);
  const css = readFileSync(new URL('../styles.css', import.meta.url), 'utf8');
  assert.ok(css.includes('clamp(200px, calc((100vw - 776px) / 2), 300px)'));
  assert.ok(css.includes(`@media (min-width: ${DOCK_MIN_W}px)`));
  assert.ok(dockIsRail(DOCK_MIN_W) && !dockIsRail(DOCK_MIN_W - 1));
});

test('a new slot lands at the end, and a second one from an unlock sits right after its first', () => {
  assert.ok(slotOrder(3) < slotOrder(4));
  assert.ok(slotOrder(3, 1) > slotOrder(3));
  assert.ok(slotOrder(3, 99) < slotOrder(4));
  assert.ok(slotOrder(-1) > slotOrder(500));
});

test('an edge left out goes to the emptier rail', () => {
  assert.equal(emptierEdge(0, 0), 'left');
  assert.equal(emptierEdge(2, 1), 'right');
  assert.equal(emptierEdge(1, 2), 'left');
});

test('no unlock draws straight into a rail any more', () => {
  for (const f of ['basics/side.js', 'basics/trunk.js', 'extras/kit.js', 'themes-a/kit.js', 'themes-b/kit.js', 'themes-b/locked.js', 'routes/kit.js']) {
    const src = readFileSync(new URL(`../ui/unlocks/${f}`, import.meta.url), 'utf8');
    assert.ok(!/layer\('side'/.test(src), `${f} still reaches for a rail`);
  }
});
