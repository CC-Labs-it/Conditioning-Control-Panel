import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createFeel } from '../core/feel.js';
import { createPhotosafe } from '../fx/photosafe.js';
import { createGentle } from '../fx/gentle.js';
import { createShake, lobe } from '../fx/shake.js';

const feelJson = JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));
const near = (a, b, e = 1e-6) => assert.ok(Math.abs(a - b) <= e, a + ' vs ' + b);

test('photosafe: at most maxTransitions per rolling window', () => {
  const ps = createPhotosafe(createFeel(feelJson));
  assert.deepEqual([1, 2, 3, 4].map(() => ps.request('bloom')), [true, true, true, false]);
  assert.equal(ps.used(), 3);
  ps.update(0.5);
  assert.equal(ps.request('band'), false);
  ps.update(0.5);   // the first three are a full window old
  assert.equal(ps.used(), 0);
  assert.equal(ps.request('palette'), true);
});

test('photosafe: a spread run never exceeds the budget in any 1 s window', () => {
  const ps = createPhotosafe(createFeel(feelJson));
  const granted = [];
  for (let i = 0; i < 300; i++) { if (ps.request('flash')) granted.push(i / 60); ps.update(1 / 60); }
  for (const t of granted) assert.ok(granted.filter((u) => u >= t && u < t + 1 - 1e-9).length <= 3);
  assert.ok(granted.length >= 12);
});

test('photosafe: red swings have their own stricter budget', () => {
  const ps = createPhotosafe(createFeel(feelJson));
  assert.equal(ps.request('red'), true);
  assert.equal(ps.request('red'), false);
  assert.equal(ps.request('bloom'), true);   // the general budget still has room
  ps.update(1);
  assert.equal(ps.request('red'), true);
});

test('gentle: off is identity, on halves contrast and scroll, removes kicks, floors rise and fade', () => {
  const feel = createFeel(feelJson);
  const g = createGentle(feel, { search: '' });
  assert.equal(g.on, false);
  assert.equal(g.kick(8), 8); assert.equal(g.contrast(1), 1); assert.equal(g.scroll(2), 2);
  assert.equal(g.rise(0.05), 0.05); assert.equal(g.fade(0.3), 0.3);
  g.set({ on: true, fadeMs: 600, riseMs: 300, speed: 0.5 });
  assert.equal(feel.get('gentle.on'), true);
  assert.equal(g.kick(8), 0); near(g.contrast(1), 0.5); near(g.scroll(2), 1);
  near(g.rise(0.05), 0.3); near(g.rise(0.5), 0.5); near(g.fade(0.3), 0.6); near(g.fade(2), 2);
  assert.equal(createGentle(createFeel(feelJson), { search: '?x=1&gentle=1' }).on, true);
});

test('shake: lobe law peaks at amp mid-period and rests at the ends', () => {
  near(lobe(0.12, 0, 0.1), 0); near(lobe(0.12, 0.1, 0.1), 0); near(lobe(0.12, 0.05, 0.1), 0.12);
});

test('shake: hits are vertical, boosts forward, each lobe x0.85 until under 0.05 m', () => {
  const feel = createFeel(feelJson);
  const sh = createShake(feel);
  sh.kick('hit');
  const dt = 0.005, peaks = [];
  let peak = 0, prevLobe = 0;
  for (let t = dt; t < 2; t += dt) {
    const v = sh.update(dt);
    assert.equal(v[0], 0); assert.equal(v[2], 0);
    const k = Math.floor((t + 1e-9) / 0.1);
    if (k !== prevLobe) { peaks.push(peak); peak = 0; prevLobe = k; }
    peak = Math.max(peak, Math.abs(v[1]));
  }
  const real = peaks.filter((p) => p > 0);
  near(real[0], 0.12, 2e-3);
  for (let i = 1; i < real.length; i++) near(real[i] / real[i - 1], 0.85, 0.03);
  assert.ok(real[real.length - 1] >= 0.05 - 2e-3);
  assert.equal(real.length, 6);   // 0.12 * 0.85^6 < 0.05
  sh.kick('boost');
  const v = sh.update(0.05);
  assert.equal(v[1], 0); assert.ok(v[2] < -0.3);
});

test('shake: nothing under Gentle', () => {
  const feel = createFeel(feelJson);
  const sh = createShake(feel);
  feel.set('gentle.on', true);
  sh.kick('hit'); sh.kick('boost');
  assert.deepEqual([...sh.update(0.05)], [0, 0, 0]);
});
