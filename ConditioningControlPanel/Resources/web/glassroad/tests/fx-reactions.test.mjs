import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createFeel } from '../core/feel.js';
import { createBus } from '../core/events.js';
import { createState, S } from '../sim/state.js';
import { createFx, restState } from '../fx/reactions.js';
import { FX_CUES } from '../fx/cues.js';
import { createPhotosafe } from '../fx/photosafe.js';
import { createGentle } from '../fx/gentle.js';

const feelJson = JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));
const contract = readFileSync(new URL('../CONTRACT.md', import.meta.url), 'utf8');
const EVENTS = [...contract.slice(contract.indexOf('## 8. Events')).matchAll(/^\| `([a-z-]+)` \|/gm)].map((m) => m[1]);

function rig({ gentle = false } = {}) {
  const feel = createFeel(feelJson), bus = createBus();
  const g = createGentle(feel, { search: '' });
  if (gentle) g.set({ on: true });
  const photosafe = createPhotosafe(feel);
  const fx = createFx(bus, feel, { gentle: g, photosafe });
  const kart = createState();
  const run = (s, dt = 1 / 60) => { for (let t = 0; t < s - 1e-9; t += dt) fx.update(dt, kart); };
  return { feel, bus, fx, kart, run, photosafe };
}

test('every CONTRACT event has exactly one fx registry entry and nothing else', () => {
  assert.equal(EVENTS.length, 25);
  assert.deepEqual(Object.keys(FX_CUES).sort(), EVENTS.slice().sort());
  for (const k of Object.keys(FX_CUES)) assert.equal(typeof FX_CUES[k], 'function');
});

test('fx sources never switch on an event name and never import three', () => {
  for (const f of ['cues.js', 'reactions.js', 'shake.js', 'photosafe.js', 'gentle.js']) {
    const src = readFileSync(new URL('../fx/' + f, import.meta.url), 'utf8');
    assert.ok(!/switch\s*\(/.test(src), f + ' has a switch');
    assert.ok(!/from ['"]three/.test(src), f + ' imports three');
  }
});

test('rest state matches the CONTRACT rest values', () => {
  const { fx } = rig();
  assert.deepEqual(fx.state, restState());
  assert.equal(fx.state.bandSpeedMul, 1); assert.equal(fx.state.paletteMix.t, 1);
});

test('countdown steps bloom up per pulse, go releases it over goReleaseS', () => {
  const { bus, fx, run } = rig();
  bus.emit('countdown', { n: 3 }); run(0.62);
  const one = fx.state.bloomBoost;
  bus.emit('countdown', { n: 2 }); run(0.62);
  assert.ok(one > 0.09 && fx.state.bloomBoost > one + 0.09);
  bus.emit('go'); run(0.3);
  assert.ok(fx.state.bloomBoost > 0);
  run(0.35);
  assert.equal(fx.state.bloomBoost, 0);
});

test('boost: fov kick and band speed in, all eased back after boost-end; Gentle removes the kick', () => {
  const { bus, fx, run } = rig();
  bus.emit('boost-start', { kind: 'drift', tier: 2, amount: 0.18, holdS: 0.9 }); run(0.2);
  assert.ok(Math.abs(fx.state.fovKick - 8) < 1e-6);
  assert.ok(Math.abs(fx.state.bandSpeedMul - 1.5) < 1e-6);
  assert.equal(fx.state.speedLines, 1);
  assert.ok(Math.hypot(...fx.state.shake) > 0);
  bus.emit('boost-end', { kind: 'drift' }); run(0.2);
  assert.ok(fx.state.fovKick > 0 && fx.state.fovKick < 8);
  run(2);
  assert.equal(fx.state.fovKick, 0); assert.equal(fx.state.bandSpeedMul, 1); assert.equal(fx.state.speedLines, 0);

  const gen = rig({ gentle: true });
  gen.bus.emit('boost-start', { kind: 'drift', tier: 2 }); gen.run(0.5);
  assert.equal(gen.fx.state.fovKick, 0);
  assert.deepEqual(gen.fx.state.shake, [0, 0, 0]);
  assert.ok(Math.abs(gen.fx.state.bandSpeedMul - 1.25) < 1e-6);
});

test('drift ladder sets tier and side; release bursts and dies in sparkDieS', () => {
  const { bus, fx, run } = rig();
  bus.emit('drift-start', { dir: -1 }); bus.emit('drift-tier', { tier: 2, dir: -1 }); run(0.1);
  assert.equal(fx.state.sparkTier, 2); assert.equal(fx.state.sparkSide, -1); assert.ok(fx.state.rumble > 0);
  bus.emit('drift-release', { tier: 2 }); run(1 / 60);
  assert.equal(fx.state.sparkTier, 0); assert.ok(fx.state.sparkBurst > 0.9);
  run(0.31);
  assert.equal(fx.state.sparkBurst, 0); assert.equal(fx.state.rumble, 0);
});

test('hop squashes then stretches; a heavy landing squashes, dusts and shakes, gone in landGoneS', () => {
  const { bus, fx, run } = rig();
  bus.emit('hop', { speed: 20 }); run(0.3);
  assert.ok(fx.state.squash < 0, 'stretched in air');
  bus.emit('land', { tier: 3, airTime: 1.2 }); run(1 / 60);
  assert.ok(fx.state.squash > 0.07 && fx.state.dust > 0.9);   // the stretch eases out under it
  assert.ok(Math.abs(fx.state.shake[1]) > 0);
  run(0.26);
  assert.equal(fx.state.dust, 0); assert.ok(Math.abs(fx.state.squash) < 1e-9);
});

test('edge, drop and rescue: glow, duck, tendril and white fade, all lifted after release', () => {
  const { bus, fx, run } = rig();
  bus.emit('edge-enter', { side: 1 }); run(0.2);
  assert.equal(fx.state.edgeGlow, 1); assert.equal(fx.state.edgeSide, 1);
  bus.emit('edge-exit', { to: 'drop' }); bus.emit('drop', { seg: 3, cause: 'edge' }); run(0.35);
  assert.equal(fx.state.edgeGlow, 0); assert.equal(fx.state.duck, 1);
  bus.emit('rescue-pull'); run(0.5);
  assert.equal(fx.state.tendril, 1); assert.ok(Math.abs(fx.state.whiteFade - 0.6) < 1e-6); assert.equal(fx.state.rescueGlow, 1);
  bus.emit('rescue-hover'); run(0.6);
  bus.emit('rescue-release'); run(0.25);
  assert.ok(fx.state.duck > 0 && fx.state.duck < 1);
  run(0.3);
  assert.equal(fx.state.duck, 0); assert.equal(fx.state.whiteFade, 0); assert.equal(fx.state.tendril, 0);
});

test('pad ring runs 0..1 with the boost tail; section crossfades the palette; finish swells and orbits', () => {
  const { bus, fx, run, feel } = rig();
  bus.emit('pad', { index: 0 }); run(0.5);
  assert.ok(fx.state.padRing > 0.15 && fx.state.padRing < 0.3);
  assert.ok(fx.state.bandBright > 0);
  run(2.1);
  assert.equal(fx.state.padRing, 0);
  const beatS = 60 / feel.get('clock.bpm');
  bus.emit('section', { from: 0, to: 1 }); run(beatS - 0.02);
  assert.equal(fx.state.paletteMix.t, 0, 'the old palette lingers one beat');
  run(1.02);
  assert.equal(fx.state.paletteMix.to, 1); assert.ok(Math.abs(fx.state.paletteMix.t - 0.5) < 0.02);
  run(1.1);
  assert.equal(fx.state.paletteMix.t, 1);
  bus.emit('finish', { timeS: 90 }); run(1.5);
  assert.ok(fx.state.flashLevel > 0.95); assert.equal(fx.state.orbit, 1);
  run(1.6);
  assert.equal(fx.state.flashLevel, 0); assert.equal(fx.state.orbit, 1);
});

test('photosafe refusals skip the brightening; pause freezes; speed past top draws lines', () => {
  const { bus, fx, run, kart, photosafe, feel } = rig();
  for (let i = 0; i < 3; i++) photosafe.request('flash');
  bus.emit('finish', { timeS: 1 }); run(0.5);
  assert.equal(fx.state.flashLevel, 0);
  bus.emit('pause', { on: true });
  const before = fx.state.orbit; run(0.5);
  assert.equal(fx.state.orbit, before);
  bus.emit('pause', { on: false });
  kart[S.speedPct] = feel.get('speed.maxPct'); run(1 / 60);
  assert.ok(fx.state.speedLines > 0.999);
  fx.reset();
  assert.deepEqual(fx.state, restState());
});

test('Gentle applies the 300 ms rise floor to every IN', () => {
  const { bus, fx, run } = rig({ gentle: true });
  bus.emit('edge-enter', { side: -1 }); run(0.15);
  assert.ok(fx.state.edgeGlow > 0 && fx.state.edgeGlow < 0.9);
  run(0.2);
  assert.equal(fx.state.edgeGlow, 1);
});
