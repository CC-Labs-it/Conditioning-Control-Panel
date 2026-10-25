import test from 'node:test';
import assert from 'node:assert/strict';
import profile from '../render/roadWordProfiles/threshold.js';
import { STYLES } from '../render/roadWordsMath.js';
import { FACES } from '../render/fonts.js';

test('Threshold has seven pearl and prism looks with a solid plum outline', () => {
  assert.equal(profile.id, 'threshold');
  assert.equal(profile.styles.length, STYLES.length);
  assert.equal(new Set(profile.styles.map(s => s.face)).size, 3);
  assert.equal(new Set(profile.styles.map(s => s.glow.join(','))).size, 7);
  for (const style of profile.styles) {
    for (const key of Object.keys(STYLES[0])) assert.ok(key in style, key);
    assert.ok(style.stroke >= 0.14 && style.stroke <= 0.2);
    assert.equal(style.inkA, 1);
    assert.equal(style.edgeA, 1);
    assert.ok(Math.max(...style.edge) < 0.05);
    assert.ok(Math.min(...style.ink) >= 0.9);
    assert.equal(style.glitch, 0);
    assert.equal(style.wear, 0);
    assert.ok(style.weight >= FACES[style.face].w[0] && style.weight <= FACES[style.face].w[1]);
  }
});

test('Threshold motion stays bounded and repeatable, with Gentle and invalid clocks planted', () => {
  const out = {};
  for (const text of ['focus', 'drift', 'rise', 'breathe', 'pearl']) {
    for (let i = -240; i <= 240; i++) {
      const p = profile.pose(text, i / 11, i / 57, false, out);
      assert.equal(p, out);
      assert.ok(Number.isFinite(p.offsetM) && Math.abs(p.offsetM) <= 0.35);
      assert.ok(Number.isFinite(p.liftM) && p.liftM >= 0 && p.liftM <= 0.35);
      assert.ok(Number.isFinite(p.scale) && p.scale >= 0.95 && p.scale <= 1.06);
      assert.deepEqual(p, profile.pose(text, i / 11, i / 57, false));
    }
    assert.deepEqual(profile.pose(text, Infinity, NaN, false), { offsetM: 0, liftM: 0, scale: 1 });
    assert.deepEqual(profile.pose(text, 4.2, 0.5, true, out), { offsetM: 0, liftM: 0, scale: 1 });
  }
});

test('Threshold focus plants, drift sways, rise lifts and breathing expands slowly', () => {
  assert.deepEqual(profile.pose('focus', 2, 0.5, false), { offsetM: 0, liftM: 0, scale: 1 });
  assert.notEqual(profile.pose('drift', 2, 0.5, false).offsetM, 0);
  assert.equal(profile.pose('rise', 2, 0.5, false).offsetM, 0);
  assert.ok(profile.pose('rise', 2, 0.5, false).liftM > profile.pose('drift', 2, 0.5, false).liftM);
  assert.equal(profile.pose('breathe', 2.8, 0, false).scale, 1.04);
  assert.equal(profile.pose('pearl', 2, 0.5, false).scale, 1.012);
});
