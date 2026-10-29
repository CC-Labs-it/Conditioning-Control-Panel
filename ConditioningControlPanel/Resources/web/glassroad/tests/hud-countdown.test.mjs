/* tests/hud-countdown.test.mjs - polish 5 J2: rival chips off through the count (fade back after go) and the
 * kart point for the Go burst. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { chipAlpha, kartPoint } from '../hud/countdown.js';

test('chipAlpha: hidden before go, eased back in after it, full otherwise', () => {
  assert.equal(chipAlpha('ready', 0), 0);
  assert.equal(chipAlpha('countdown', 5), 0);
  assert.equal(chipAlpha('racing', 0, 0.6), 0);
  assert.equal(chipAlpha('racing', 0.3, 0.6), 0.5);
  assert.equal(chipAlpha('racing', 0.6, 0.6), 1);
  assert.equal(chipAlpha('racing', 9, 0.6), 1);
  assert.ok(chipAlpha('racing', 0.15, 0.6) < chipAlpha('racing', 0.45, 0.6));
  assert.equal(chipAlpha('finished', 0), 1);
  assert.equal(chipAlpha(null, 0), 1);
  assert.equal(chipAlpha('racing', 0.1, 0), 1);
});

test('kartPoint: device coordinates to view fractions, null behind the camera', () => {
  assert.deepEqual(kartPoint(0, 0, 0.5), { x: 0.5, y: 0.5 });
  const p = kartPoint(0.2, -0.4, 0.9);
  assert.ok(Math.abs(p.x - 0.6) < 1e-9 && Math.abs(p.y - 0.7) < 1e-9);
  assert.deepEqual(kartPoint(-3, 3, 0.5), { x: 0.05, y: 0.05 }, 'clamped inside the frame');
  assert.equal(kartPoint(0, 0, 1.2), null);
  assert.equal(kartPoint(NaN, 0, 0.5), null);
});
