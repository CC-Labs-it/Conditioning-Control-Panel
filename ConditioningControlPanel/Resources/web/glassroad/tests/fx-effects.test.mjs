// tests/fx-effects.test.mjs - the local player's screen effect (CONTRACT-B1.md section 3, fx/effects.js).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { makeFeel } from './sim-fake.mjs';
import { createBus } from '../core/events.js';
import { createGentle } from '../fx/gentle.js';
import { createPhotosafe } from '../fx/photosafe.js';
import { createEffects } from '../fx/effects.js';

const DT = 1 / 60;

function setup({ gentleOn = false, grant = true } = {}) {
  const feel = makeFeel(), bus = createBus(), seen = [], asked = [];
  const gentle = createGentle(feel, { search: '' });
  gentle.set({ on: gentleOn });
  const photosafe = { request: (k) => { asked.push(k); return grant; } };
  bus.on('effect-start', (p) => seen.push(['start', p.kind, p]));
  bus.on('effect-end', (p) => seen.push(['end', p.kind, p.replaced]));
  const fx = createEffects(bus, feel, { gentle, photosafe });
  const hit = (kind, durationS = 2, extra = {}) => bus.emit('item-hit', { victim: 0, by: 3, kind, verdict: 'hit', durationS, p: [0, 0, 0], ...extra });
  const run = (s) => { for (let n = Math.round(s / DT); n > 0; n--) fx.update(DT); };
  return { feel, bus, seen, asked, fx, hit, run, s: fx.screen };
}

test('envelope: IN, hold, OUT, then effect-end', () => {
  const t = setup();
  t.hit('brainDrain', 1);
  assert.deepEqual(t.seen[0].slice(0, 2), ['start', 'brainDrain']);
  assert.equal(t.seen[0][2].by, 3);
  t.run(0.1);
  assert.ok(t.s.k > 0.3 && t.s.k < 0.7, 'rising ' + t.s.k);
  t.run(0.5);
  assert.equal(t.s.k, 1);
  t.run(0.6);
  assert.ok(t.s.k > 0 && t.s.k < 1, 'falling ' + t.s.k);
  t.run(0.3);
  assert.equal(t.s.kind, 'none');
  assert.deepEqual(t.seen.at(-1), ['end', 'brainDrain', false]);
});

test('replace, never stack: one kind at a time, the old one fades out as prev', () => {
  const t = setup();
  t.hit('spiral', 2);
  t.run(0.5);
  t.hit('freeze', 2);
  assert.deepEqual(t.seen.map((e) => e.slice(0, 2)), [['start', 'spiral'], ['end', 'spiral'], ['start', 'freeze']]);
  assert.equal(t.seen[1][2], true);
  assert.equal(t.s.kind, 'freeze');
  assert.equal(t.s.prevKind, 'spiral');
  t.run(DT);
  assert.ok(t.s.prevK > 0 && t.s.prevK < 1, 'cross fade ' + t.s.prevK);
  t.run(0.3);
  assert.equal(t.s.prevKind, 'none');
  assert.equal(t.s.prevK, 0);
});

test('only the local player, only real hits on view kinds and Shatter', () => {
  const t = setup();
  t.hit('flash', 1, { victim: 2 });
  t.hit('flash', 1, { verdict: 'mercy' });
  t.hit('flash', 1, { verdict: 'glass' });
  t.hit('surge', 1);
  assert.equal(t.seen.length, 0);
  t.hit('shatter', 1.5);
  assert.equal(t.s.kind, 'shatter');
});

test('photosafe: every start asks; flash and blackout as flash; a refusal lands softer', () => {
  const t = setup();
  for (const k of ['flash', 'blackout', 'pink', 'melt']) t.hit(k, 1);
  assert.deepEqual(t.asked, ['flash', 'flash', 'palette', 'palette']);
  const u = setup({ grant: false });
  u.hit('blackout', 2);
  u.run(0.25);
  const want = 0.25 / u.feel.get('items.photosafeInS');
  assert.ok(Math.abs(u.s.k - want) < 0.05, 'soft IN ' + u.s.k);
});

test('gentle transforms: corner Flash, Spiral a soft dim, blur halved, no flicker, IN at the rise floor', () => {
  const off = setup();
  off.hit('flash', 1.2);
  assert.equal(off.s.params.corner, false);
  assert.equal(off.s.params.cover, off.feel.get('items.flashCover'));
  assert.equal(off.s.params.blurMul, 1);
  const t = setup({ gentleOn: true });
  t.hit('flash', 1.2);
  const p = t.s.params;
  assert.equal(p.gentle, true); assert.equal(p.corner, true); assert.equal(p.flicker, false);
  assert.equal(p.cover, t.feel.get('items.gentleFlashScale'));
  assert.equal(p.blurMul, t.feel.get('items.gentleBlurMul'));
  t.run(0.2);
  assert.ok(t.s.k < 0.8, 'IN held to 300 ms: ' + t.s.k);
  t.hit('spiral', 2);
  assert.equal(t.s.params.dimOnly, true);
  assert.equal(t.s.params.corner, false);
  assert.equal(t.s.strength, t.feel.get('items.gentleSpiralDim'));
  t.run(DT);
  assert.ok(t.s.prevK > 0, 'Gentle cross fade, no hard cut');
  t.run(2.0);
  assert.ok(t.s.k > 0, 'Gentle OUT lasts the fade length');
});

test('pause holds the effect; reset clears it; dispose stops listening', () => {
  const t = setup();
  t.hit('melt', 1);
  t.run(0.3);
  const k = t.s.k, tt = t.s.t;
  t.bus.emit('pause', { on: true });
  t.run(1);
  assert.equal(t.s.t, tt); assert.equal(t.s.k, k);
  t.bus.emit('pause', { on: false });
  t.fx.reset();
  assert.equal(t.s.kind, 'none'); assert.equal(t.s.k, 0);
  t.fx.dispose();
  t.hit('flash', 1);
  assert.equal(t.s.kind, 'none');
});

test('real photosafe: a burst of hits never reaches the screen faster than the budget allows', () => {
  const feel = makeFeel(), bus = createBus(), ps = createPhotosafe(feel);
  const fx = createEffects(bus, feel, { gentle: createGentle(feel, { search: '' }), photosafe: ps });
  for (let k = 0; k < 6; k++) bus.emit('item-hit', { victim: 0, by: 1, kind: 'flash', verdict: 'hit', durationS: 1 });
  assert.equal(ps.used(), feel.get('photosafe.maxTransitions'));
  fx.update(0.25);
  assert.ok(fx.screen.k <= 0.25 / feel.get('items.photosafeInS') + 1e-6);
});
