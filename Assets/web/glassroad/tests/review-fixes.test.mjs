import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createFeel } from '../core/feel.js';
import { createBus } from '../core/events.js';
import { createClock } from '../core/clock.js';
import { createActionMap, createHopLatch } from '../core/actions.js';
import { createState, S, BOOST } from '../sim/state.js';
import { applyBoost } from '../sim/kart.js';
import { createFx, shownWeights } from '../fx/reactions.js';
import { createPhotosafe } from '../fx/photosafe.js';
import { createGentle } from '../fx/gentle.js';
import { resetKeepingGentle } from '../hud/feelSheet.js';
import { createAudio } from '../audio/bus.js';
import { makeFeel, fakeTrack, place, recorder, acts, run as simRun } from './sim-fake.mjs';

const feelJson = JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));

function rig({ gentle = false } = {}) {
  const feel = createFeel(feelJson), bus = createBus();
  const g = createGentle(feel, { search: '' });
  if (gentle) g.set({ on: true });
  const photosafe = createPhotosafe(feel);
  const fx = createFx(bus, feel, { gentle: g, photosafe });
  const kart = createState();
  const run = (s, dt = 1 / 60) => { for (let t = 0; t < s - 1e-9; t += dt) fx.update(dt, kart); };
  return { feel, bus, fx, run, photosafe };
}

test('hop latch: at 144 Hz every hop edge reaches a sim step (and without it most are lost)', () => {
  for (const latched of [true, false]) {
    let lost = 0;
    for (let k = 0; k < 40; k++) {
      const clock = createClock({ hz: 60, maxSubsteps: 2 });
      let frameNo = 0, seen = 0;
      const src = { poll(into) { into.hopPressed = frameNo === 10 + k; }, dispose() {} };
      const map = createActionMap([src]), latch = createHopLatch();
      for (; frameNo < 80; frameNo++) {
        const { steps } = clock.frame(frameNo * 1000 / 144);
        const a = map.sample();
        if (latched) latch.hold(a);
        for (let i = 0; i < steps; i++) {
          if (a.hopPressed) seen++;
          if (latched) latch.spent(a); else a.hopPressed = false;
        }
      }
      if (latched) assert.equal(seen, 1, 'press on frame ' + (10 + k));
      else if (seen === 0) lost++;
    }
    if (!latched) assert.ok(lost > 10, 'the unlatched loop loses presses: ' + lost);
  }
});

test('boost rise from cruise takes boost.riseS (latched start speed)', () => {
  const feel = makeFeel(), tr = fakeTrack({ w: 200 }), rec = recorder(), a = acts({ throttle: 1 });
  const top = feel.get('speed.topMps'), st = place(tr, { speed: top });
  applyBoost(st, BOOST.DRIFT1, 1, feel, rec);
  const amt = feel.get('boost.tierAmount')[0], rise = feel.get('boost.riseS');
  simRun(st, tr, feel, a, rise / 2, rec);
  const half = st[S.engineV];
  assert.ok(half > top * (1 + amt * 0.3) && half < top * (1 + amt * 0.7), 'half way up at half the rise: ' + half);
});

test('rumble: drift tiers ramp, a landing mid-drift keeps the drift rumble, Gentle zeroes it', () => {
  const { bus, fx, run, feel } = rig();
  const by = feel.get('juice.rumbleByTier');
  bus.emit('drift-start', { dir: 1 });
  bus.emit('drift-tier', { tier: 1, dir: 1 }); run(0.2);
  assert.ok(Math.abs(fx.state.rumble - by[0]) < 1e-6);
  bus.emit('drift-tier', { tier: 3, dir: 1 }); run(0.2);
  assert.ok(Math.abs(fx.state.rumble - by[2]) < 1e-6);
  bus.emit('land', { tier: 1 }); run(0.5);
  assert.ok(Math.abs(fx.state.rumble - by[2]) < 1e-6, 'drift rumble survives the landing');
  const g = rig({ gentle: true });
  g.bus.emit('drift-tier', { tier: 3, dir: 1 }); g.run(0.5);
  assert.equal(g.fx.state.rumble, 0);
});

test('pad glow: one photosafe ask, an envelope with the Gentle rise floor, nothing when refused', () => {
  const { bus, fx, run, photosafe } = rig();
  bus.emit('pad', { index: 0 });
  assert.equal(photosafe.used(), 1);
  run(1 / 60);
  assert.ok(fx.state.padGlow > 0 && fx.state.padGlow < 1, 'rises, no jump: ' + fx.state.padGlow);
  const g = rig({ gentle: true });
  g.bus.emit('pad', { index: 0 }); g.run(0.2);
  assert.ok(g.fx.state.padGlow < 0.9, 'Gentle 300 ms floor');
  const r = rig();
  for (let i = 0; i < 3; i++) r.photosafe.request('flash');
  r.bus.emit('pad', { index: 0 }); r.run(0.3);
  assert.equal(r.fx.state.padGlow, 0); assert.equal(r.fx.state.bandBright, 0); assert.equal(r.fx.state.padRing, 0);
});

test('edge glow asks photosafe; refused it only reaches edgeGlowRefused', () => {
  const { bus, fx, run, photosafe, feel } = rig();
  for (let i = 0; i < 3; i++) photosafe.request('flash');
  bus.emit('edge-enter', { side: 1 }); run(0.2);
  assert.ok(Math.abs(fx.state.edgeGlow - feel.get('juice.edgeGlowRefused')) < 1e-6);
});

test('a gate mid-fade starts from the blend on screen, never a snap', () => {
  const { bus, fx, run, feel } = rig();
  const beatS = 60 / feel.get('clock.bpm');
  bus.emit('section', { from: 0, to: 1 }); run(beatS + 1);
  const t = fx.state.paletteMix.t;
  assert.ok(t > 0.4 && t < 0.6);
  bus.emit('section', { from: 1, to: 0 });
  const w = fx.state.paletteMix.base;
  assert.ok(Math.abs(w[0] - (1 - t)) < 1e-6 && Math.abs(w[1] - t) < 1e-6, JSON.stringify(w));
  assert.equal(fx.state.paletteMix.t, 0);
  run(beatS + 2.1);
  assert.equal(fx.state.paletteMix.t, 1); assert.equal(fx.state.paletteMix.base, null);
  assert.deepEqual(shownWeights({ from: 2, to: 2, t: 1 }), { 2: 1 });
});

test('finish orbit: a partial turn from feel, halved under Gentle', () => {
  const { bus, fx, run, feel } = rig();
  bus.emit('finish', { timeS: 1 }); run(1.6);
  assert.equal(fx.state.orbit, 1);
  assert.ok(feel.get('juice.finishOrbitDeg') <= 120);
  const g = rig({ gentle: true });
  g.bus.emit('finish', { timeS: 1 }); g.run(1.6);
  assert.ok(Math.abs(g.fx.state.orbit - g.feel.get('gentle.scrollMul')) < 1e-6);
});

test('feel sheet reset keeps host Gentle on', () => {
  const feel = createFeel(feelJson);
  feel.set('gentle.on', true); feel.set('speed.topMps', 40);
  resetKeepingGentle(feel);
  assert.equal(feel.get('gentle.on'), true); assert.equal(feel.get('speed.topMps'), feelJson.speed.topMps);
});

test('audio reset releases held tones and moves the pad chord', async () => {
  const feel = createFeel(feelJson), bus = createBus();
  const stops = [];
  const param = () => ({ value: 0, setValueAtTime() {}, linearRampToValueAtTime() {}, exponentialRampToValueAtTime() {}, setTargetAtTime() {}, cancelScheduledValues() {} });
  const node = (x = {}) => ({ connect() {}, disconnect() {}, start() {}, stop(t) { stops.push(t); }, ...x });
  const ctx = {
    state: 'suspended', currentTime: 0, sampleRate: 8000, destination: node(),
    createGain: () => node({ gain: param() }), createBiquadFilter: () => node({ frequency: param(), Q: param() }),
    createOscillator: () => node({ frequency: param(), detune: param() }), createBufferSource: () => node(),
    createBuffer: (_c, len) => ({ getChannelData: () => new Float32Array(len) }),
    resume: async () => { ctx.state = 'running'; }, suspend: async () => { ctx.state = 'suspended'; }, close() {},
  };
  const audio = createAudio(feel, bus, { win: null, makeCtx: () => ctx });
  await audio.start();
  bus.emit('edge-enter', { side: 1 }); bus.emit('drop', {});
  const before = stops.length;
  audio.reset(0);
  assert.ok(stops.length >= before + 1, 'held rescue tone stopped');
  audio.dispose();
});
