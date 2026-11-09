import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { deskHandoff } from '../ui/unlocks/themes-b/emi-kit.js';

const desk = (o = {}) => ({
  el: { isConnected: true },
  pos: { x: 120, y: 300, s: 1 },
  size: 104,
  napping: false,
  blocking: false,
  home: () => ({ x: 40, y: 500 }),
  hat: { textContent: '' },
  ...o,
});

test('no desk EMI on the page: no handoff, the takeover brings a new EMI', () => {
  assert.equal(deskHandoff(null), null);
  assert.equal(deskHandoff(desk({ el: { isConnected: false } })), null);
});

test('the main EMI takes her spot, her shown size and her hat', () => {
  assert.deepEqual(deskHandoff(desk()), { x: 120, y: 300, size: 104, hat: '' });
  assert.deepEqual(deskHandoff(desk({ hat: { textContent: '🎩' } })).hat, '🎩');
  assert.equal(deskHandoff(desk({ pos: { x: 10, y: 20, s: 0.75 } })).size, 78);
});

test('asleep on the counter or on a shop row she is away: her home spot at her normal size', () => {
  for (const away of [{ napping: true }, { blocking: true }]) {
    const h = deskHandoff(desk({ ...away, pos: { x: 600, y: 80, s: 0.44 } }));
    assert.deepEqual([h.x, h.y, h.size], [40, 500, 104]);
  }
});

test('every desk feature that would show or move her stands down while she is borrowed', () => {
  const emi = readFileSync(new URL('../ui/unlocks/extras/emi.js', import.meta.url), 'utf8');
  assert.match(emi, /say\(text, ms = 3000\) \{\s*if \(self\.borrowed\) return;/);
  assert.match(emi, /grab \|\| self\.borrowed \|\|/);
  assert.match(emi, /\.xe\[data-borrowed\]>:is\(\.xe-sprite,\.xe-c,\.xe-s,\.xe-t\)\{visibility:hidden/);
  const lane = readFileSync(new URL('../ui/unlocks/extras/emi-unlocks.js', import.meta.url), 'utf8');
  assert.match(lane, /if \(e\.borrowed\) \{\s*\/\/ the Takeover has her/);           // jealousy
  assert.match(lane, /e\.napping \|\| e\.blocking \|\| e\.borrowed \|\| cardUp\(\)\) return;/);   // nap
  assert.match(lane, /if \(e\.borrowed && e\.napping\) \{ stop\(\);/);
  assert.match(lane, /up \|\| cardUp\(\) \|\| e\.borrowed\) return false;/);      // the call card
  const swarm = readFileSync(new URL('../ui/unlocks/themes-b/emi-swarm.js', import.meta.url), 'utf8');
  assert.match(swarm, /desk\.borrow\(true\)/);
  assert.match(swarm, /desk\.borrow\(false\)/);
});
