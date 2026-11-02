import { test } from 'node:test';
import assert from 'node:assert/strict';
import { haloSlots, haloPoseInto } from '../render/mediaMath.js';

test('haloPoseInto writes the same pose as haloSlots, in place', () => {
  const s2 = Math.SQRT1_2;
  const frame = { p: [10, 2, -4], t: [s2, 0, s2], n: [0, 1, 0], b: [s2, 0, -s2], w: 12 };
  const spec = { kind: 'landscape', heightM: 6, upM: 30, radiusM: 26, tiltDeg: 35 };
  const slots = haloSlots(spec, frame, 8, 0);
  const keep = slots.map((sl) => [sl.C, sl.X, sl.Y, sl.Z]);
  for (const orbit of [0.3, 2.1, 5.9]) {
    const want = haloSlots(spec, frame, 8, orbit);
    for (let i = 0; i < 8; i++) {
      const sl = haloPoseInto(spec, frame, 8, orbit, i, slots[i]);
      assert.equal(sl.C, keep[i][0]);
      for (const k of ['C', 'X', 'Y', 'Z']) for (let j = 0; j < 3; j++) assert.ok(Math.abs(sl[k][j] - want[i][k][j]) < 1e-9, k + j);
    }
  }
});
