import test from 'node:test';
import assert from 'node:assert/strict';
import profile from '../render/roadWordProfiles/boudoir.js';
import { STYLES } from '../render/roadWordsMath.js';
import { FACES } from '../render/fonts.js';

test('Boudoir has seven readable cream and rose satin modes with a blackberry outline', () => {
  assert.equal(profile.id, 'boudoir');
  assert.equal(profile.styles.length, STYLES.length);
  assert.equal(new Set(profile.styles.map(s => s.face)).size, 3);
  assert.equal(new Set(profile.styles.map(s => s.ink.join(','))).size, 7);
  for (const style of profile.styles) {
    for (const key of Object.keys(STYLES[0])) assert.ok(key in style, key);
    assert.ok(style.stroke >= 0.14 && style.stroke <= 0.2);
    assert.equal(style.edgeA, 1);
    assert.ok(Math.max(...style.edge) < 0.06);
    assert.ok(style.ink[0] > style.ink[1] && style.ink[0] > style.ink[2]);
    assert.ok(style.glow[0] > style.glow[2] && style.glow[2] > style.glow[1]);
    assert.ok(style.weight >= FACES[style.face].w[0] && style.weight <= FACES[style.face].w[1]);
    assert.notEqual(style.face, 'silk');
    assert.equal(style.glitch, 0);
    assert.equal(style.stencil, 0);
    assert.equal(style.wear, 0);
  }
});

test('Boudoir motion stays bounded and deterministic, and Gentle clears every movement', () => {
  const output = {};
  for (const text of ['float', 'kiss', 'blush', 'breathe', 'hold', 'satin', 'road']) {
    for (let i = -240; i <= 240; i++) {
      const p = profile.pose(text, i / 13, i / 61, false, output);
      assert.equal(p, output);
      assert.ok(Number.isFinite(p.offsetM) && Math.abs(p.offsetM) <= 0.35);
      assert.ok(Number.isFinite(p.liftM) && p.liftM >= 0 && p.liftM <= 0.35);
      assert.ok(Number.isFinite(p.scale) && p.scale >= 0.95 && p.scale <= 1.06);
      assert.deepEqual(p, profile.pose(text, i / 13, i / 61, false));
    }
    assert.deepEqual(profile.pose(text, Infinity, NaN, false), { offsetM: 0, liftM: 0, scale: 1 });
    assert.deepEqual(profile.pose(text, 2.6, 0.5, true, output), { offsetM: 0, liftM: 0, scale: 1 });
  }
});

test('Boudoir meanings produce satin drift, a kiss pulse, a slow breath and a planted hold', () => {
  assert.deepEqual(profile.pose('hold', 2, 0.5, false), { offsetM: 0, liftM: 0, scale: 1 });
  assert.notEqual(profile.pose('float', 2, 0.5, false).offsetM, 0);
  assert.equal(profile.pose('kiss', 2, 0.5, false).scale, 1.04);
  assert.equal(profile.pose('breathe', 2.6, 0, false).scale, 1.045);
  assert.equal(profile.pose('road', 2, 0.5, false).scale, 1.012);
  assert.equal(profile.pose('hold still and float', 2, 0.5, false).liftM, 0);
});
