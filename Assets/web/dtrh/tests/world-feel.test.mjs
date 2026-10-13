import test from 'node:test';
import assert from 'node:assert/strict';

import {
  warmthTarget, approach, stepWarmth, breathAt, ringAt, createWorldFeel,
  WARMTH_STREAK_FULL, WARMTH_DRAIN_SEC, BREATH_PERIOD_SEC, BREATH_GLOW, WARM_GLOW, RING, SWEEP,
} from '../engine/worldFeel.js';
import { setMotionLevel, motionLevel } from '../shared/motion.js';

const near = (a, b, eps = 1e-6) => Math.abs(a - b) <= eps;

test('warmthTarget is 0 with no streak, rises, and caps at 1', () => {
  assert.equal(warmthTarget(0), 0);
  assert.equal(warmthTarget(NaN), 0);
  assert.equal(warmthTarget(-3), 0);
  assert.ok(warmthTarget(5) > 0 && warmthTarget(5) < warmthTarget(20));
  assert.equal(warmthTarget(WARMTH_STREAK_FULL), 1);
  assert.equal(warmthTarget(WARMTH_STREAK_FULL * 4), 1);
});

test('approach never overshoots and does not depend on the frame rate', () => {
  let a = 0; for (let i = 0; i < 60; i++) a = approach(a, 1, 1 / 60, 0.5);
  let b = 0; for (let i = 0; i < 30; i++) b = approach(b, 1, 1 / 30, 0.5);
  assert.ok(near(a, b, 1e-9));
  assert.ok(a > 0 && a < 1);
  assert.equal(approach(0.3, 1, 0, 0.5), 0.3);
  assert.equal(approach(0.3, 1, 0.1, 0), 1);
});

test('a detonation drains the warmth inside the drain window', () => {
  const s = { value: 1, target: 0, drainLeft: WARMTH_DRAIN_SEC };
  for (let t = 0; t < WARMTH_DRAIN_SEC; t += 1 / 60) stepWarmth(s, 1 / 60);
  assert.ok(s.value < 0.06, 'about 5% left: ' + s.value);
  assert.ok(s.value >= 0);
  assert.ok(s.drainLeft < 0.02);
});

test('warmth rises smoothly toward a held streak and cools when play stops', () => {
  const s = { value: 0, target: 1, drainLeft: 0 };
  stepWarmth(s, 1 / 60);
  assert.ok(s.value > 0 && s.value < 0.05, 'no jump on the first frame');
  for (let i = 0; i < 180; i++) { s.target = 1; stepWarmth(s, 1 / 60); }
  assert.ok(s.value > 0.8);
  for (let i = 0; i < 60 * 90; i++) stepWarmth(s, 1 / 60);
  assert.ok(s.value < 0.05, 'cools with no pops');
});

test('the breath is one slow sine on the shared clock', () => {
  assert.ok(near(breathAt(0), 0));
  assert.ok(near(breathAt(BREATH_PERIOD_SEC / 4), 1));
  assert.ok(near(breathAt(BREATH_PERIOD_SEC), 0, 1e-9));
});

test('a ring fades in, runs away from the camera, fades out, then ends', () => {
  const o = { dist: 0, amp: 0, width: 0 };
  assert.ok(ringAt(0, RING, 1, false, o));
  assert.equal(o.amp, 0);
  assert.equal(o.dist, RING.near);
  let last = o.dist, peak = 0;
  for (let k = 1; k < 100; k++) {
    assert.ok(ringAt((k / 100) * RING.dur, RING, 1, false, o));
    assert.ok(o.dist >= last, 'only ever moves away');
    assert.ok(o.amp <= RING.amp + 1e-9);
    last = o.dist; peak = Math.max(peak, o.amp);
  }
  assert.ok(peak > RING.amp * 0.8);
  assert.ok(o.amp < RING.amp * 0.05, 'nearly gone before it ends');
  assert.equal(ringAt(RING.dur, RING, 1, false, o), false);
  assert.equal(ringAt(1, null, 1, false, o), false);
});

test('reduced halves the ring and off holds it still', () => {
  const full = { dist: 0, amp: 0, width: 0 }, half = { dist: 0, amp: 0, width: 0 };
  const a = { dist: 0, amp: 0, width: 0 }, b = { dist: 0, amp: 0, width: 0 };
  ringAt(SWEEP.dur * 0.3, SWEEP, 1, false, full);
  ringAt(SWEEP.dur * 0.3, SWEEP, 0.5, false, half);
  assert.ok(near(half.amp, full.amp / 2));
  ringAt(SWEEP.dur * 0.25, SWEEP, 0, true, a);
  ringAt(SWEEP.dur * 0.6, SWEEP, 0, true, b);
  assert.equal(a.dist, b.dist);
  assert.ok(a.amp > 0 && a.amp < full.amp);
});

test('thread layers: a base everywhere, warmth adds, motion off stills the moving ones', async () => {
  const { threadTarget, THREAD_BASE, THREAD_WARM } = await import('../engine/worldFeel.js');
  assert.equal(threadTarget('breath', 0.4, 1, 1), 0.4, 'other knobs pass through');
  assert.equal(threadTarget('thHalo', 0, 0, 1), THREAD_BASE.thHalo);
  assert.equal(threadTarget('thBeads', 0, 1, 1), 0, 'a biome layer stays off outside its biome');
  assert.equal(threadTarget('thBeads', 0.6, 0, 1), 0.6);
  assert.equal(threadTarget('thHalo', 0.9, 0, 1), 0.9, 'a biome may ask for more than the base');
  assert.ok(Math.abs(threadTarget('thSparkle', 0, 1, 1) - (THREAD_BASE.thSparkle + THREAD_WARM.thSparkle)) < 1e-9);
  assert.equal(threadTarget('thSheen', 0, 0, 1), 0);
  assert.ok(threadTarget('thSheen', 0, 1, 1) > 0, 'a full streak brings sheen');
  assert.equal(threadTarget('thWeave', 0, 0, 0), 0, 'no sway at motion off');
  assert.equal(threadTarget('thStatic', 0.8, 0, 0), 0, 'no grain at motion off');
  assert.ok(threadTarget('thHalo', 0, 0, 0) > 0, 'the halo is not motion');
  assert.ok(threadTarget('thSparkle', 1, 1, 1) <= 1);
});

test('the live instance follows the seam: pop warms, detonate drains, moments ring', () => {
  const before = motionLevel();
  setMotionLevel('full');
  const wf = createWorldFeel(null);
  let f = wf.update(1 / 60);
  assert.equal(f.warmth, 0);
  assert.equal(f.ringAmp, 0);
  for (let i = 0; i < 240; i++) { wf._feel({ detail: { kind: 'pop', streak: 30 } }); f = wf.update(1 / 60); }
  const warm = f.warmth;
  assert.ok(warm > 0.5);
  assert.ok(f.glow <= 1 + BREATH_GLOW + WARM_GLOW + 1e-9, 'glow stays small');
  wf._feel({ detail: { kind: 'detonate' } });
  wf._feel({ detail: { kind: 'pop', streak: 30 } });   // a pop mid-drain does not stop the drain
  for (let i = 0; i < 60 * WARMTH_DRAIN_SEC; i++) f = wf.update(1 / 60);
  assert.ok(f.warmth < warm * 0.1);
  wf._feel({ detail: { kind: 'moment', event: 'rabbit-caught' } });
  wf.update(RING.dur * 0.3);   // clamped to 0.1 s a frame
  for (let i = 0; i < 4; i++) f = wf.update(0.1);
  assert.ok(f.ringAmp > 0);
  wf._feel({ detail: { kind: 'moment', event: 'benign-popped' } });
  wf._feel({ detail: null });
  for (let i = 0; i < 40; i++) f = wf.update(0.1);
  assert.equal(f.ringAmp, 0, 'the ring ends by itself');
  setMotionLevel('off');
  f = wf.update(BREATH_PERIOD_SEC / 4);
  assert.ok(f.breath === 0, 'no swell with motion off');
  setMotionLevel(before);
  wf.dispose();
});
