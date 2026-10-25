import test from 'node:test';
import assert from 'node:assert/strict';
import profile from '../render/roadWordProfiles/mainframe.js';
import { STYLES } from '../render/roadWordsMath.js';

test('Mainframe keeps seven legible green terminal modes with a dark continuous outline', () => {
  assert.equal(profile.id, 'mainframe');
  assert.equal(profile.styles.length, STYLES.length);
  assert.equal(new Set(profile.styles.map(s => s.face)).size, 3);
  assert.equal(new Set(profile.styles.map(s => s.ink.join(','))).size, 7);
  for (const style of profile.styles) {
    for (const key of Object.keys(STYLES[0])) assert.ok(key in style, key);
    assert.ok(style.stroke >= 0.14 && style.stroke <= 0.2);
    assert.equal(style.edgeA, 1);
    assert.ok(Math.max(...style.edge) < 0.03);
    assert.ok(style.ink[1] >= style.ink[0] && style.ink[1] >= style.ink[2]);
    assert.ok(style.glow[1] > style.glow[0] && style.glow[1] > style.glow[2]);
    assert.equal(style.glitch, 0);
    assert.ok(style.weight >= 700);
  }
});

test('Mainframe semantic motion stays bounded, finite and repeatable at every phase', () => {
  const output = {};
  for (const text of ['scan', 'stream', 'breathe', 'locked', 'signal']) {
    for (let i = -240; i <= 240; i++) {
      const p = profile.pose(text, i / 13, i / 61, false, output);
      assert.equal(p, output);
      assert.ok(Number.isFinite(p.offsetM) && Math.abs(p.offsetM) <= 0.35);
      assert.ok(Number.isFinite(p.liftM) && p.liftM >= 0 && p.liftM <= 0.35);
      assert.ok(Number.isFinite(p.scale) && p.scale >= 0.95 && p.scale <= 1.06);
      assert.deepEqual(p, profile.pose(text, i / 13, i / 61, false));
    }
    assert.deepEqual(profile.pose(text, Infinity, NaN, false), { offsetM: 0, liftM: 0, scale: 1 });
    assert.deepEqual(profile.pose(text, 4.2, 0.5, true, output), { offsetM: 0, liftM: 0, scale: 1 });
  }
});

test('Mainframe commands have different readable actions instead of identical wobble', () => {
  assert.deepEqual(profile.pose('hold', 2, 0.5, false), { offsetM: 0, liftM: 0, scale: 1 });
  assert.notEqual(profile.pose('scan', 2, 0.5, false).offsetM, 0);
  assert.notEqual(profile.pose('flow', 2, 0.5, false).offsetM, profile.pose('scan', 2, 0.5, false).offsetM);
  assert.equal(profile.pose('breathe', 2.4, 0, false).scale, 1.035);
  assert.equal(profile.pose('signal', 2, 0.5, false).scale, 1.018);
});
