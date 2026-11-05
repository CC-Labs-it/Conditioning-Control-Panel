/* tests/umbrella-tween.test.mjs - desk run 2026-10-30 (L2 caveat): the lace umbrella popped in and out. It now has
 * an IN (glides open with a small overshoot, Gentle a plain ease-out) and an OUT (folds away), in the race glide and
 * when it is picked in the garage. The scale rides the bind matrix (an attached-bind skinned mesh ignores its own). */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { umbrellaStep, umbrellaShape, UMBRELLA_TWEEN } from '../render/wardrobeMath.js';

const DT = 1 / 60;
const run = (u, on, s, gentle = false) => { for (let t = 0; t < s - 1e-9; t += DT) u = umbrellaStep(u, on, DT, gentle); return u; };

test('the umbrella opens over openS and folds over foldS, longer under Gentle, and never leaves 0..1', () => {
  assert.ok(run(0, true, UMBRELLA_TWEEN.openS - 2 * DT) < 1, 'not open before openS');
  assert.equal(run(0, true, UMBRELLA_TWEEN.openS + DT), 1, 'open after openS');
  assert.ok(run(1, false, UMBRELLA_TWEEN.foldS - 2 * DT) > 0, 'still folding before foldS');
  assert.equal(run(1, false, UMBRELLA_TWEEN.foldS + DT), 0, 'gone after foldS');
  assert.ok(UMBRELLA_TWEEN.gentleOpenS > UMBRELLA_TWEEN.openS && UMBRELLA_TWEEN.gentleFoldS > UMBRELLA_TWEEN.foldS, 'Gentle only lengthens');
  assert.ok(run(0, true, UMBRELLA_TWEEN.openS + DT, true) < 1, 'Gentle still opening at the plain open time');
  assert.equal(umbrellaStep(1, true, 5), 1); assert.equal(umbrellaStep(0, false, 5), 0);
  assert.equal(umbrellaStep(0.4, true, 0), 0.4, 'a paused frame holds');
  assert.ok(umbrellaStep(0.5, true, NaN) === 0.5, 'a bad dt holds');
});

test('the canopy shape: tiny when folded, exactly the model when open, a small overshoot only on a plain open', () => {
  for (const [opening, gentle] of [[true, false], [true, true], [false, false], [false, true]]) {
    const z = umbrellaShape(0, opening, gentle), one = umbrellaShape(1, opening, gentle);
    assert.ok(z.radial < 0.1 && z.height <= 0.5, 'folded is a closed stick');
    assert.ok(Math.abs(one.radial - 1) < 1e-12 && Math.abs(one.height - 1) < 1e-12, 'open is the identity bind');
  }
  let peak = 0;
  for (let u = 0; u <= 1; u += 0.01) peak = Math.max(peak, umbrellaShape(u, true, false).radial);
  assert.ok(peak > 1.02 && peak < 1.15, 'a plain open overshoots a touch: ' + peak.toFixed(3));
  for (let u = 0; u <= 1; u += 0.01) {
    assert.ok(umbrellaShape(u, true, true).radial <= 1 + 1e-12, 'Gentle never overshoots');
    assert.ok(umbrellaShape(u, false).radial <= 1 + 1e-12, 'folding never swells');
    assert.ok(umbrellaShape(u, false).height <= 1 + 1e-12 && umbrellaShape(u, true).height <= 1 + 1e-12, 'height never overshoots');
  }
  // folding starts slow: half way through the fold the canopy is still most of the way open
  assert.ok(umbrellaShape(0.5, false).radial > 0.7);
});

test('the race and the garage glide it; the scale rides the bind matrix about the handle foot', () => {
  const karts = readFileSync(new URL('../render/karts.js', import.meta.url), 'utf8');
  const garage = readFileSync(new URL('../render/garage.js', import.meta.url), 'utf8');
  const wardrobe = readFileSync(new URL('../render/wardrobe.js', import.meta.url), 'utf8');
  assert.match(karts, /k\.dress\.glideUmbrella\(gliding, paused \? 0 : dt, gentle\)/, 'race: glide open, fold away, hold on pause');
  assert.match(karts, /k\.dress\?\.showUmbrella\?\.\(false\)/, 'a reset folds it at once');
  assert.match(garage, /dress\?\.glideUmbrella\?\.\(true,dt,gentle\)/, 'garage: glides open when picked');
  assert.match(garage, /dress\.umbrella===hadUmbrella\)dress\.showUmbrella\(true\)/, 'garage: another re-dress keeps it open');
  assert.match(wardrobe, /m\.bindMatrix\.copy\(UM\)/, 'the scale is in the bind matrix');
  assert.match(wardrobe, /glideUmbrella\(\) \{\}/, 'an undressed kart has a no-op glide');
});
