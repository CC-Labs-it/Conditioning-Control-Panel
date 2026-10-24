import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loomVariation, createLoomDisplayFrames } from '../render/loomVariety.js';
import { bandHz, screenRateMul } from '../render/spiralMath.js';

test('Mainframe gates and takeovers use six distinct layered, deforming Loom fields', () => {
  const gates = JSON.parse(readFileSync(new URL('../tracks/mainframe.spirals.json', import.meta.url))).gates;
  assert.equal(new Set(gates.map(g => g.loom.layer.style)).size, 6);
  gates.forEach((g, i) => {
    assert.deepEqual(g.loom, loomVariation(i));
    assert.ok(g.loom.layer2.enabled && g.loom.wobble.amp > 0 && g.loom.hueCycles > 0);
    assert.notEqual(g.loom.layer.style, g.loom.layer2.style);
    assert.notEqual(g.loom.layer.speedMul, g.loom.layer2.speedMul);
    assert.ok(bandHz(g.loom) * screenRateMul(g.loom) <= 2.5);
  });
});

test('landmark Loom canvases have a bounded redraw rate and Gentle halves phase advance', () => {
  const ctx = new Proxy({}, { get: (o, k) => k in o ? o[k] : /Gradient/.test(k) ? () => ({ addColorStop() {} }) : () => {} });
  const make = (width, height) => ({ width, height, getContext: kind => kind === '2d' ? ctx : null });
  const normal = createLoomDisplayFrames(make), gentle = createLoomDisplayFrames(make), phone = createLoomDisplayFrames(make, 'phone');
  assert.equal(normal.rows[0].canvas.width, 512);
  assert.equal(phone.rows[0].canvas.width, 256);
  assert.equal(normal.update(0), false);
  assert.equal(normal.update(0.03), false);
  assert.equal(normal.update(0.04), true);
  gentle.update(0.07, true);
  normal.rows.forEach((r, i) => assert.ok(Math.abs((r.phase - i / 3) / 2 - (gentle.rows[i].phase - i / 3)) < 1e-10));
  assert.equal(phone.update(0.1), false);
  assert.equal(phone.update(0.03), true);
  for (const frames of [normal, gentle, phone]) frames.dispose();
});
