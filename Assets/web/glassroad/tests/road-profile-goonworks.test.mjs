import test from 'node:test';
import assert from 'node:assert/strict';
import profile from '../render/roadWordProfiles/goonworks.js';
import { STYLES } from '../render/roadWordsMath.js';
import { FACES } from '../render/fonts.js';

test('Goonworks has seven thick cream fairground modes with continuous dark outlines', () => {
  assert.equal(profile.id, 'goonworks');
  assert.equal(profile.styles.length, STYLES.length);
  assert.equal(new Set(profile.styles.map(s => s.face)).size, 3);
  assert.equal(new Set(profile.styles.map(s => s.ink.join(','))).size, 7);
  assert.ok(new Set(profile.styles.map(s => s.glow.join(','))).size >= 5);
  for (const style of profile.styles) {
    for (const key of Object.keys(STYLES[0])) assert.ok(key in style, key);
    assert.ok(style.stroke >= .14 && style.stroke <= .20);
    assert.equal(style.edgeA, 1);
    assert.ok(Math.max(...style.edge) < .04);
    assert.ok(Math.min(...style.ink) >= .75);
    assert.equal(style.glitch, 0);
    assert.equal(style.stencil, 0);
    assert.equal(style.wear, 0);
    assert.ok(style.weight >= FACES[style.face].w[0] && style.weight <= FACES[style.face].w[1]);
  }
});

test('Goonworks semantic poses stay bounded and repeatable, with Gentle and invalid clock resets', () => {
  const output = {};
  for (const text of ['spin', 'watch', 'bounce', 'hold', 'fairground']) {
    for (let i = -260; i <= 260; i++) {
      const p = profile.pose(text, i / 13, i / 61, false, output);
      assert.equal(p, output);
      assert.ok(Number.isFinite(p.offsetM) && Math.abs(p.offsetM) <= .35);
      assert.ok(Number.isFinite(p.liftM) && p.liftM >= 0 && p.liftM <= .35);
      assert.ok(Number.isFinite(p.scale) && p.scale >= .95 && p.scale <= 1.06);
      assert.deepEqual(p, profile.pose(text, i / 13, i / 61, false));
    }
    assert.deepEqual(profile.pose(text, Infinity, NaN, false), { offsetM: 0, liftM: 0, scale: 1 });
    assert.deepEqual(profile.pose(text, 2.2, .5, true, output), { offsetM: 0, liftM: 0, scale: 1 });
  }
});

test('Goonworks words orbit, magnify, bounce or hold according to meaning', () => {
  assert.deepEqual(profile.pose('hold', 2, .5, false), { offsetM: 0, liftM: 0, scale: 1 });
  assert.notEqual(profile.pose('spin', 1, .5, false).offsetM, 0);
  assert.equal(profile.pose('watch', 2, .5, false).scale, 1.035);
  assert.equal(profile.pose('watch', 2, .5, false).offsetM, 0);
  assert.equal(profile.pose('bounce', 2, .5, false).liftM, .22);
  assert.equal(profile.pose('bounce', 2, 0, false).liftM, 0);
  assert.ok(profile.pose('fairground', 2, .5, false).liftM < profile.pose('bounce', 2, .5, false).liftM);
});
