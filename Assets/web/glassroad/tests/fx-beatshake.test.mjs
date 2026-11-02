/* Feel wave 2026-10-26 (Lane A): fx/shake.js and its beat channel are retired. What stays pinned here: music never
 * shakes anything through fx (the race camera and the trauma value only hear physical events), and the trauma
 * law itself (fx/trauma.js): clamp, linear decay, out = trauma^2 * motionMul, smooth seeded noise per axis. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, existsSync } from 'node:fs';
import { createFeel } from '../core/feel.js';
import { createBus } from '../core/events.js';
import { createState } from '../sim/state.js';
import { createFx } from '../fx/reactions.js';
import { createTrauma, noise, motionMulOf, createPoseLag } from '../fx/trauma.js';
import { createPhotosafe } from '../fx/photosafe.js';
import { createGentle } from '../fx/gentle.js';
import { setLiveMusic } from '../audio/songShape.js';

const feelJson = JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));

test('the old shake module is gone and live race music never adds trauma', () => {
  assert.ok(!existsSync(new URL('../fx/shake.js', import.meta.url)));
  const feel = createFeel(feelJson), fx = createFx(createBus(), feel, { gentle: createGentle(feel, { search: '' }), photosafe: createPhotosafe(feel) });
  const music = { playing: true, songS: 0, song: { bpm: 96, beats: [] } };
  setLiveMusic(music);
  try {
    const kart = createState();
    for (let t = 8; t < 20; t += 1 / 60) { music.songS = t; fx.update(1 / 60, kart); assert.equal(fx.state.trauma, 0); }
  } finally { setLiveMusic(null); fx.dispose(); }
});

test('trauma: adds clamp at 1, decays linearly at trauma.decayPerS, out is squared and motion-scaled', () => {
  const feel = createFeel(feelJson), t = createTrauma(feel);
  t.add(0.5); t.add(0.8); assert.equal(t.value, 1);
  t.step(0.25); assert.ok(Math.abs(t.value - (1 - 0.25 * feel.get('trauma.decayPerS'))) < 1e-9);
  const v = t.value; assert.ok(Math.abs(t.out - v * v) < 1e-12);
  t.setMotion('less'); assert.ok(Math.abs(t.out - v * v * feel.get('trauma.lessMul')) < 1e-12);
  t.setMotion('off'); assert.equal(t.out, 0);
  t.setMotion('nonsense'); assert.equal(t.motionMul, 1);
  t.step(5); assert.equal(t.value, 0);
  assert.deepEqual([motionMulOf('full'), motionMulOf('less'), motionMulOf('off')], [1, 0.45, 0]);
});

test('noise: deterministic per axis, in [-1, 1], smooth, axes differ, numeric axes 0..2 for the camera', () => {
  let maxStep = 0, peak = 0, diff = 0;
  for (let t = 0; t < 40; t += 0.01) {
    for (const a of [0, 1, 2]) { const n = noise(a, t); assert.ok(n >= -1 && n <= 1); peak = Math.max(peak, Math.abs(n)); }
    maxStep = Math.max(maxStep, Math.abs(noise(0, t + 0.01) - noise(0, t)));
    diff = Math.max(diff, Math.abs(noise(0, t) - noise(1, t)));
    assert.equal(noise(2, t), noise(2, t));
  }
  assert.ok(peak > 0.4, 'it moves ' + peak); assert.ok(diff > 0.2, 'axes are independent');
  assert.ok(maxStep < 0.1, 'smooth, never a strobe ' + maxStep);
});

test('pose lag: the finish slow frame draws the karts behind the sim, then closes; rate 1 never lags', () => {
  const lag = createPoseLag({ X: 0, catchS: 0.3 }), v = [new Float32Array(3)], dt = 1 / 60;
  let t = 0, behind = 0;
  for (; t < 0.5; t += dt) { lag.step(dt, 0.4); v[0][0] = t * 30; lag.record(t, v); behind = Math.max(behind, t * 30 - v[0][0]); }
  assert.ok(Math.abs(lag.lagS - 0.3) < 0.02, 'lag ' + lag.lagS); assert.ok(behind > 6, 'drawn behind ' + behind);
  for (; t < 2.5; t += dt) { lag.step(dt, 1); v[0][0] = t * 30; lag.record(t, v); }
  assert.ok(lag.lagS < 0.01 && Math.abs(v[0][0] - t * 30) < 1, 'caught up');
  const flat = createPoseLag({ X: 0 }); flat.step(dt, 1); v[0][0] = 7; flat.record(0, v); assert.equal(v[0][0], 7);
});
