import test from 'node:test';
import assert from 'node:assert/strict';
import keep from '../render/roadWordProfiles/keep.js';
import { STYLES } from '../render/roadWordsMath.js';

test('Keep inscriptions have seven complete outlined stone and gilded looks', () => {
  assert.equal(keep.id, 'keep');
  assert.equal(keep.styles.length, STYLES.length);
  assert.equal(new Set(keep.styles.map(s => s.id)).size, 7);
  for (const [i, style] of keep.styles.entries()) {
    for (const key of Object.keys(STYLES[i])) assert.ok(key in style, key);
    assert.ok(style.stroke >= 0.14 && style.stroke <= 0.20);
    const luma = rgb => rgb[0] * 0.2126 + rgb[1] * 0.7152 + rgb[2] * 0.0722;
    assert.ok(luma(style.ink) - luma(style.edge) > 0.9);
    assert.equal(style.edgeA, 1);
    assert.equal(style.glitch, 0);
    assert.notEqual(style.face, 'silk');
  }
  assert.ok(new Set(keep.styles.map(s => s.face)).size >= 2);
  assert.ok(new Set(keep.styles.map(s => s.ink.join(','))).size >= 6);
});

test('Keep meaning motion stays inside the shared rail and scale budget', () => {
  const words = ['hold', 'locked', 'rise', 'open', 'breathe', 'slow', 'drift', 'follow', 'hall', '', null];
  for (const word of words) for (let n = -100; n <= 100; n++) {
    const pose = keep.pose(word, n / 3, n / 17, false);
    assert.ok(Number.isFinite(pose.offsetM) && Math.abs(pose.offsetM) <= 0.35);
    assert.ok(Number.isFinite(pose.liftM) && pose.liftM >= 0 && pose.liftM <= 0.35);
    assert.ok(Number.isFinite(pose.scale) && pose.scale >= 0.95 && pose.scale <= 1.06);
  }
  for (const time of [NaN, Infinity, -Infinity, undefined]) {
    assert.deepEqual(keep.pose('rise', time, time, false), { offsetM: 0, liftM: 0, scale: 1 });
  }
});

test('Keep semantics plant locks, raise rising text and breathe as a whole', () => {
  assert.deepEqual(keep.pose('HOLD STILL', 2.4, 0.5, false), { offsetM: 0, liftM: 0, scale: 1 });
  assert.equal(keep.pose('rise', 2.4, 0.5, false).liftM, 0.24);
  assert.ok(keep.pose('breathe', 2.4, 0.5, false).scale > keep.pose('breathe', 0, 0, false).scale);
  assert.ok(Math.abs(keep.pose('drift', 1.4, 0.5, false).offsetM) > 0.1);
  assert.ok(keep.pose('hall', 2.4, 0.5, false).scale < 1);
});

test('Keep poses are deterministic, reuse output and remove motion in Gentle', () => {
  const out = { offsetM: 99, liftM: 99, scale: 99 };
  assert.equal(keep.pose('rise', 2, 0.4, false, out), out);
  assert.deepEqual(out, keep.pose('rise', 2, 0.4, false));
  assert.equal(keep.pose('rise', 2, 0.4, true, out), out);
  assert.deepEqual(out, { offsetM: 0, liftM: 0, scale: 1 });
});
