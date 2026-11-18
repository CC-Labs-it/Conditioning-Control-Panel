// Two-tone on the page is the desktop's maths (CCP.Core Super/FogMix.cs): the same field, seam and OKLCH ramp, so the
// shop shows what the app draws, and two colours stay two clean colours (teal and amber went murky brown, 2026-11-17).
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { ramp, field, shape, toOklch, rgbOfHex, RAMP_SIZE, BLOBS, SEAM_LOW, SEAM_HIGH, WALK_LOW, WALK_HIGH, MID_SOFTEN } from '../sheet/fogmix.js';
import { FOGS } from '../sheet/looks.js';

const CORE = readFileSync(new URL('../../../../../CCP.Core/Super/FogMix.cs', import.meta.url), 'utf8');
const TEAL = [64, 200, 190];

test('the constants are the desktop constants', () => {
  assert.match(CORE, new RegExp(`RampSize = ${RAMP_SIZE};`));
  assert.match(CORE, new RegExp(`Blobs = ${BLOBS};`));
  assert.match(CORE, new RegExp(`SeamLow = ${SEAM_LOW}, SeamHigh = ${SEAM_HIGH};`));
  assert.match(CORE, new RegExp(`WalkLow = ${WALK_LOW}, WalkHigh = ${WALK_HIGH};`));
  assert.match(CORE, new RegExp(`MidSoften = ${MID_SOFTEN};`));
});

test('the ramp matches the desktop at the probe the C# test pins (FogLooksTests.RampProbe)', () => {
  assert.deepEqual(ramp(TEAL, rgbOfHex(FOGS.amber))[RAMP_SIZE / 2], [164, 202, 114]);
});

test('each pair keeps its ends, a clean stretch of each colour, and never goes dark or grey between them', () => {
  const pairs = [[TEAL, FOGS.amber], [FOGS.pink, FOGS.mint], [FOGS.lilac, FOGS.peach], [FOGS.sky, FOGS.amber]].map(([a, b]) => [Array.isArray(a) ? a : rgbOfHex(a), rgbOfHex(b)]);
  for (const [a, b] of pairs) {
    const r = ramp(a, b), [la, ca] = toOklch(a), [lb, cb] = toOklch(b);
    assert.deepEqual(r[0], a); assert.deepEqual(r[RAMP_SIZE - 1], b);
    assert.deepEqual(r[Math.trunc(WALK_LOW * (RAMP_SIZE - 1))], a);
    for (const c of r) {
      const [l, ch] = toOklch(c);
      assert.ok(l >= Math.min(la, lb) - 0.01, `${a}->${b}: ${c} darker than both ends`);
      assert.ok(ch >= 0.7 * Math.min(ca, cb), `${a}->${b}: ${c} goes grey`);
    }
  }
  for (const hex of Object.values(FOGS)) {
    const c = rgbOfHex(hex);
    for (const x of ramp(c, c)) x.forEach((v, i) => assert.ok(Math.abs(v - c[i]) <= 1, `${hex} paired with itself drifts`));
  }
});

test('both colours show and the mix moves', () => {
  let second = 0, n = 0;
  for (let y = 0; y < 27; y++) for (let x = 0; x < 48; x++, n++) if (shape(field((x + 0.5) / 48 * 16 / 9, (y + 0.5) / 27, 16 / 9, 12)) > 0.5) second++;
  assert.ok(second / n > 0.05 && second / n < 0.95);
  assert.notEqual(field(0.4, 0.6, 1.7, 12), field(0.4, 0.6, 1.7, 15));
});
