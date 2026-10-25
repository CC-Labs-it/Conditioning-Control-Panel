import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { createBus } from '../core/events.js';
import { createEffects } from '../fx/effects.js';
import { makeFeel } from './sim-fake.mjs';
const hooks = registerHooks({ resolve(s, c, next) {
  const files = { 'three/tsl': 'three.tsl.min.js', 'three/webgpu': 'three.webgpu.min.js' };
  return files[s] ? { url: new URL('../vendor/three/build/' + files[s], import.meta.url).href, shortCircuit: true } : next(s, c);
} });
const { createVeils, veilSize } = await import('../render/veil.js');
const { createFlashBurst, flashBurstLayout, VEIL_COSMETIC_ONLY } = await import('../fx/flashBurst.js');
const { Scene } = await import('three/webgpu');
hooks.deregister();

const cue = { id: 'test-veil', kind: 'VEIL', alpha: 1, params: {},
  frame: { p: [0, 0, 0], b: [1, 0, 0], n: [0, 1, 0], t: [0, 0, -1], w: 20 } };
function cards(options = {}) {
  const canvases = [], draws = [], captions = [], clears = [];
  const make = () => ({ style: {}, setAttribute() {}, remove() { this.removed = true; },
    getContext: () => ({ drawImage(pic) { draws.push(pic); }, fillRect() { clears.push(1); }, fillText(text) { captions.push(text); } }) });
  const fx = createFlashBurst({ make, parent: { appendChild: c => canvases.push(c) }, acquireLoom: () => null, ...options });
  return { fx, canvases, draws, captions, clears };
}
function frames(fx, seconds, options = {}, onFrame = () => {}) {
  for (let i = 0; i < Math.round(seconds * 120); i++) { fx.update(1 / 120, options); onFrame(i / 120); }
}

test('veils span the road plus shoulders with bounded height, prebuilt resources and pause', () => {
  // A curtain: road width plus shoulders, two to three kart heights, bounded either way.
  assert.deepEqual(veilSize(cue.frame, { height: 99 }), { width: 28, height: 9 });
  assert.equal(veilSize(cue.frame, { height: 0.5 }).height, 5);
  const scene = new Scene(), requests = [], fx = createVeils(scene, {}, { photosafe: { request: k => { requests.push(k); return true; } } });
  const resources = fx.object.children.map(m => [m.geometry, m.material]);
  fx.update([cue], .1); assert.equal(fx.stats.visible, 1);
  const mesh = fx.object.children.find(m => m.visible);
  assert.equal(mesh.scale.x, 28); assert.equal(mesh.scale.y, veilSize(cue.frame, cue.params).height);
  assert.equal(requests.length, 1); assert.equal(mesh.material.depthWrite, false);
  const state = fx.stats; fx.update([cue], 1, { paused: true }); assert.deepEqual(fx.stats, state);
  fx.update([cue], .1); assert.equal(requests.length, 1);
  assert.deepEqual(fx.object.children.map(m => [m.geometry, m.material]), resources);
  fx.clear(); assert.equal(fx.stats.visible, 0); fx.dispose(); fx.dispose(); assert.equal(scene.children.length, 0);
});

test('stacked bursts share a rolling three-card limit and have a 300ms rise and full OUT', () => {
  assert.equal(VEIL_COSMETIC_ONLY, true);
  const { fx, canvases } = cards();
  fx.fire(cue); fx.fire({ ...cue, id: 'second', params: { count: 9 } }); fx.fire(cue);
  const births = []; let total = 0;
  frames(fx, 14, {}, time => {
    if (fx.stats.total > total) { births.push(time); total = fx.stats.total; }
    assert.ok(births.filter(t => time - t < 1 - 1e-8).length <= 3);
    assert.ok(fx.stats.active <= 9);
    if (time < .15) assert.ok(Number(canvases[0].style.opacity) < .6);
  });
  assert.ok(total >= 9 && total <= 16, 'overlapping bursts may skip stale queued cards instead of arriving late');
  assert.equal(fx.stats.pending, 0); assert.equal(fx.stats.active, 0);
  assert.ok(canvases.every(c => c.style.display === 'none')); fx.dispose();
});

test('Gentle uses three still edge cards and denied photosafe requests create no cards', () => {
  const { fx, canvases } = cards({ gentle: { on: true, rise: s => Math.max(s, .8), fade: s => Math.max(s, 1.4) } });
  fx.fire(cue); frames(fx, 1.2); assert.equal(fx.stats.total, 3);
  const placements = canvases.slice(0, 3).map(c => [c.style.left, c.style.top, c.style.transform]);
  frames(fx, 1); assert.deepEqual(canvases.slice(0, 3).map(c => [c.style.left, c.style.top, c.style.transform]), placements);
  for (let i = 0; i < 9; i++) {
    const p = flashBurstLayout(i, 2, true); assert.ok(p.x + p.w < .22 || p.x > .78);
    assert.ok(p.y >= .24 && p.y + p.h <= .79);
  }
  fx.dispose(); const denied = cards({ photosafe: { request: () => false } });
  denied.fx.fire(cue); frames(denied.fx, 3); assert.equal(denied.fx.stats.total, 0); denied.fx.dispose();
});

test('pause freezes envelopes, revocation clears pixels while paused, and fallback has no false credit', () => {
  let picture = { width: 640, height: 360 }, releases = 0;
  const atlas = { width: 768, height: 512 };
  const { fx, draws, captions, clears, canvases } = cards({ source: () => picture,
    acquireLoom: () => ({ texture: { image: atlas }, dispose() { releases++; } }) });
  fx.fire(cue); frames(fx, .8); assert.ok(draws.includes(picture)); assert.ok(captions.includes('via Scrolller'));
  const state = fx.stats, opacity = canvases[0].style.opacity;
  fx.update(1, { paused: true }); assert.deepEqual(fx.stats, state); assert.equal(canvases[0].style.opacity, opacity);
  const count = captions.length, clearCount = clears.length; picture = null;
  fx.update(1, { paused: true }); assert.ok(clears.length > clearCount); assert.equal(captions.length, count);
  assert.notEqual(draws.at(-1), null);
  fx.clear(); assert.equal(fx.stats.pending, 0); fx.fire(cue); frames(fx, .5); assert.ok(fx.stats.total > 0);
  fx.dispose(); fx.dispose(); assert.equal(releases, 1); assert.ok(canvases.every(c => c.removed));
});

test('blackout and settled fog obscure card overlays', () => {
  const { fx, canvases } = cards(); fx.fire(cue); frames(fx, .8);
  fx.update(.02, { screen: { kind: 'blackout', k: 1, strength: 1 } });
  assert.ok(canvases.every(c => c.style.display === 'none'));
  const fog = { kind: 'pink', k: 1, strength: 1, params: { seed: 1 } };
  frames(fx, 4, { screen: fog }); fx.fire({ ...cue, id: 'fog' }); frames(fx, 1, { screen: fog });
  assert.ok(canvases.every(c => c.style.display === 'none' || Number(c.style.opacity) <= .051)); fx.dispose();
});
test('hidden tabs release copied pictures and queued bursts without waiting for animation', () => {
  const visibility = new EventTarget(); visibility.hidden = false;
  const { fx, canvases } = cards({ visibility, source: () => ({ width: 640, height: 360 }) });
  fx.fire(cue); frames(fx, .8); assert.ok(fx.stats.active > 0);
  visibility.hidden = true; visibility.dispatchEvent(new Event('visibilitychange'));
  assert.equal(fx.stats.active, 0); assert.equal(fx.stats.pending, 0);
  assert.ok(canvases.every(c => c.style.display === 'none')); fx.dispose();
});

test('normal flashes use Goon-sized random image centres across the driving view on desktop and phone', () => {
  for (const aspect of [16 / 9, 9 / 16]) {
    const cards = Array.from({ length: 64 }, (_, seed) => flashBurstLayout(seed % 9, 1, false, aspect, seed, 4 / 3));
    for (const p of cards) {
      assert.ok(p.x + p.w / 2 >= .14 && p.x + p.w / 2 <= .86);
      assert.ok(p.y + p.h / 2 >= .16 && p.y + p.h / 2 <= .80);
      assert.ok(Math.abs(Math.max(p.w * aspect, p.h) - .437 * Math.min(1, aspect)) < 1e-9);
      assert.ok(Math.abs(p.tilt) <= 8);
    }
    assert.ok(cards.some(p => p.x < .5 && p.x + p.w > .5 && p.y < .5 && p.y + p.h > .5), 'pictures may cover the racing line');
    assert.ok(new Set(cards.map(p => p.x.toFixed(3))).size > 30, 'layout is scattered rather than alternate side gutters');
  }
});

test('each picture holds for two seconds, then visibly fades instead of blinking away', () => {
  const { fx, canvases } = cards(); fx.fire(cue);
  frames(fx, .4); assert.equal(Number(canvases[0].style.opacity), 1);
  frames(fx, 1.8); assert.equal(Number(canvases[0].style.opacity), 1, 'still held after two seconds on screen');
  frames(fx, .5); assert.ok(Number(canvases[0].style.opacity) > .2 && Number(canvases[0].style.opacity) < .8);
  frames(fx, .5); assert.equal(canvases[0].style.display, 'none'); fx.dispose();
});

test('one received FLASH creates seven pictures once, a second hit retriggers, and restart has no stale backlog', () => {
  const bus = createBus(), effects = createEffects(bus, makeFeel()), { fx } = cards();
  const hit = () => bus.emit('item-hit', { victim: 0, by: 2, kind: 'flash', verdict: 'hit', durationS: 3 });
  const run = seconds => { for (let i = 0; i < seconds * 120; i++) {
    effects.update(1 / 120); fx.update(1 / 120, { flashes: [effects.screen] });
  } };
  hit(); run(2.5); assert.equal(fx.stats.total, 7, 'a burst rather than one picture or a burst every frame');
  run(4); assert.equal(fx.stats.total, 7); assert.equal(fx.stats.pending, 0); assert.equal(fx.stats.active, 0);
  hit(); run(.2); const paused = fx.stats;
  for (let i = 0; i < 100; i++) fx.update(.1, { paused: true, flashes: [effects.screen] });
  assert.deepEqual(fx.stats, paused, 'pause freezes queued and visible cards');
  effects.reset(); fx.clear(); run(5);
  assert.equal(fx.stats.total, 0); assert.equal(fx.stats.pending, 0); assert.equal(fx.stats.active, 0);
  hit(); run(2.5); assert.equal(fx.stats.total, 7, 'a fresh hit after restart starts one fresh burst');
  fx.clear(); run(.2); assert.equal(fx.stats.total, 0, 'clearing a still-held source never replays its old seed');
  fx.dispose(); effects.dispose();
});

test('item and payoff screens with equal seeds are separate bursts and denied queues expire', () => {
  let granted = true;
  const { fx } = cards({ photosafe: { request: () => granted } });
  const screens = [0, 1].map(() => ({ kind: 'flash', params: { seed: 1 } }));
  frames(fx, 7, { flashes: screens }); assert.ok(fx.stats.total > 7 && fx.stats.total <= 14);
  fx.clear(); granted = false; screens[0].params.seed++;
  frames(fx, 5, { flashes: screens }); assert.equal(fx.stats.pending, 0);
  granted = true; frames(fx, 2, { flashes: screens }); assert.equal(fx.stats.total, 0, 'expired denied cards cannot arrive late');
  fx.dispose();
});

test('replacing a received flash cancels its pending pictures while a short completed payoff finishes its burst', () => {
  const { fx } = cards(), screen = { kind: 'flash', params: { seed: 1 } };
  frames(fx, .2, { flashes: [screen] }); assert.equal(fx.stats.pending, 6);
  screen.params.seed++;
  frames(fx, .05, { flashes: [screen] }); assert.equal(fx.stats.pending, 7, 'new hit replaces the old six waiting cards');
  screen.kind = 'pink'; screen.prevKind = 'flash';
  frames(fx, .1, { flashes: [screen] }); assert.equal(fx.stats.pending, 0);
  frames(fx, 2, { flashes: [screen] }); assert.equal(fx.stats.active, 0);
  fx.clear(); screen.kind = 'flash'; screen.prevKind = 'none'; screen.params.seed++;
  frames(fx, .2, { flashes: [screen] }); screen.kind = 'none';
  frames(fx, 3, { flashes: [screen] }); assert.equal(fx.stats.total, 7, 'normal payoff completion leaves the visual burst intact');
  fx.dispose();
});

test('multiple replacement hits between frames retire the old flash even after prevKind is overwritten', () => {
  const bus = createBus(), effects = createEffects(bus, makeFeel()), { fx } = cards();
  const hit = kind => bus.emit('item-hit', { victim: 0, by: 2, kind, verdict: 'hit', durationS: 3 });
  hit('flash'); effects.update(.1); fx.update(.1, { flashes: [effects.screen] });
  assert.equal(fx.stats.active, 1); assert.equal(fx.stats.pending, 6);
  hit('pink'); hit('freeze'); effects.update(.01);
  assert.equal(effects.screen.kind, 'freeze'); assert.notEqual(effects.screen.prevKind, 'flash');
  fx.update(.01, { flashes: [effects.screen] }); assert.equal(fx.stats.pending, 0);
  frames(fx, 2, { flashes: [effects.screen] });
  assert.equal(fx.stats.total, 1); assert.equal(fx.stats.active, 0);
  fx.dispose(); effects.dispose();
});
