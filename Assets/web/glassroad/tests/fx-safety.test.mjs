import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createFeel } from '../core/feel.js';
import { createPhotosafe } from '../fx/photosafe.js';
import { createGentle } from '../fx/gentle.js';
import { createBus } from '../core/events.js';
import { createState } from '../sim/state.js';
import { createFx } from '../fx/reactions.js';

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

// feel wave 2026-10-26 (Lane A): the render-only time effects and the Motion row
function fxRig({ gentle = false, motion } = {}) {
  const feel = createFeel(feelJson), bus = createBus(), g = createGentle(feel, { search: '' });
  if (gentle) g.set({ on: true });
  const fx = createFx(bus, feel, { gentle: g, photosafe: createPhotosafe(feel) }), kart = createState();
  if (motion) fx.setMotion(motion);
  return { feel, bus, fx, run: (s, dt = 1 / 60) => { for (let t = 0; t < s - 1e-9; t += dt) fx.update(dt, kart); } };
}

test('hit-stop: 40 ms on a hit, at most one per 400 ms, never on a bump; less halves, off removes', () => {
  const a = fxRig();
  a.bus.emit('item-hit', { victim: 0, verdict: 'hit' }); near(a.fx.state.holdS, 0.04);
  a.run(0.1); assert.equal(a.fx.state.holdS, 0, 'over by itself');
  a.bus.emit('wall', { dv: 99 }); assert.equal(a.fx.state.holdS, 0, 'inside the 400 ms gap');
  a.run(0.4); a.bus.emit('wall', { dv: 99 }); near(a.fx.state.holdS, 0.04);
  const big = fxRig(); big.bus.emit('item-hit', { victim: 0, verdict: 'hit', big: true }); near(big.fx.state.holdS, 0.06);
  const b = fxRig(); b.bus.emit('bump', { a: 0, b: 2, dv: 30 }); assert.equal(b.fx.state.holdS, 0);
  const less = fxRig({ motion: 'less' }); less.bus.emit('moving-trap-hit', { racer: 0 }); near(less.fx.state.holdS, 0.02);
  const gen = fxRig({ gentle: true }); gen.bus.emit('item-hit', { victim: 0, verdict: 'hit' }); near(gen.fx.state.holdS, 0.02, 1e-9);
  const off = fxRig({ motion: 'off' }); off.bus.emit('item-hit', { victim: 0, verdict: 'hit' }); off.run(0.02);
  assert.equal(off.fx.state.holdS, 0); assert.equal(off.fx.state.traumaOut, 0); assert.equal(off.fx.state.aberrationPx, 0);
  const bot = fxRig(); bot.bus.emit('moving-trap-hit', { racer: 3 }); assert.equal(bot.fx.state.holdS, 0);
});

test('finish slow frame: renderRate 0.4 for finishHold.s, eased back to 1 (IN and OUT); off keeps 1', () => {
  const a = fxRig();
  a.bus.emit('finish', { timeS: 60 }); a.run(0.3); near(a.fx.state.renderRate, 0.4);
  a.run(0.35); assert.ok(a.fx.state.renderRate > 0.4 && a.fx.state.renderRate < 1, 'easing ' + a.fx.state.renderRate);
  a.run(0.3); assert.equal(a.fx.state.renderRate, 1);
  const off = fxRig({ motion: 'off' }); off.bus.emit('finish', {}); off.run(0.2); assert.equal(off.fx.state.renderRate, 1);
});

test('trauma channels: border shake within 6 px, aberration within 2.5 px, both 0 under Gentle; lap and launch', () => {
  const a = fxRig(); let px = 0, ab = 0;
  for (let i = 0; i < 4; i++) { a.bus.emit('item-hit', { victim: 0, verdict: 'hit' }); for (let k = 0; k < 30; k++) { a.run(1 / 60); px = Math.max(px, Math.abs(a.fx.state.borderShakePx)); ab = Math.max(ab, a.fx.state.aberrationPx); } }
  assert.ok(px > 0.5 && px <= 6 && ab > 0.5 && ab <= 2.5, px + ' px, ' + ab + ' px');
  const l = fxRig(); l.bus.emit('lap', { lap: 1 }); l.run(1 / 60); assert.ok(l.fx.state.trauma > 0.05 && l.fx.state.trauma <= 0.08);
  const r = fxRig(); r.bus.emit('hitter-hit', { racer: 0, hitter: 2 }); r.run(1 / 60); assert.ok(r.fx.state.trauma > 0.3 && r.fx.state.trauma <= 0.35);
  const s = fxRig(); s.bus.emit('rev', { level: 1 }); s.run(0.2); assert.ok(s.fx.state.squash > 0, 'the kart crouches on the rev');
  s.bus.emit('launch', { grade: 'perfect' }); s.run(0.05); assert.ok(s.fx.state.squash < 0, 'a perfect launch stretches');
  assert.ok(s.fx.state.trauma > 0);
});
