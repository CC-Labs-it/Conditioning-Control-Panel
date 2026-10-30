import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createFeel } from '../core/feel.js';
import { createBus } from '../core/events.js';
import { createAudio } from '../audio/bus.js';
import { MIX } from '../audio/mix.js';
import {
  HIT_VARIANTS, hitVariantRate, hitSize, gridWait, squealPlan, driftCharge01, releaseSemis, RELEASE_SEMIS,
  windPlan, isFinalLap, bearingPan, revPlan, percStep,
} from '../audio/feelVoices.js';

const feelJson = JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));
const feel = createFeel(feelJson);

test('feel wave keys: feel.json audio.* and music.* exist, MIX carries the same audio numbers', () => {
  for (const [k, v] of Object.entries(feelJson.audio)) assert.equal(MIX[k], v, k);
  assert.equal(feel.get('music.finalLapRate'), 1);
  assert.deepEqual([feel.get('audio.hitDuckWall'), feel.get('audio.hitDuckItem'), feel.get('audio.hitDuckBump')], [0.55, 0.45, 0.8]);
});

test('sub thump variants: a fixed 4-step cycle within +-4%, deterministic', () => {
  const r = [0, 1, 2, 3].map(hitVariantRate);
  assert.equal(new Set(r).size, 4);
  assert.ok(r.every((x) => x >= 0.96 && x <= 1.04));
  assert.equal(hitVariantRate(5), hitVariantRate(1));
  assert.equal(HIT_VARIANTS.length, 4);
});

test('hit size: wall scales by dv, the item on me is the deepest, bumps duck lightly and never thump', () => {
  assert.equal(hitSize('wall', { dv: 1 }, feel), null);
  const wall = hitSize('wall', { dv: 12 }, feel), item = hitSize('item-hit', { victim: 0, verdict: 'hit' }, feel);
  assert.equal(wall.thump, 1); assert.equal(wall.floor, 0.55);
  assert.ok(hitSize('wall', { dv: 6 }, feel).floor > wall.floor);
  assert.equal(item.floor, 0.45); assert.ok(item.floor < wall.floor);
  assert.equal(hitSize('item-hit', { victim: 2, by: 0 }, feel), null);
  assert.equal(hitSize('item-hit', { victim: 0, verdict: 'shield' }, feel), null);
  const bump = hitSize('bump', { dv: 10 }, feel);
  assert.equal(bump.thump, 0); assert.equal(bump.floor, 0.8);
  assert.equal(hitSize('bump', { dv: 1 }, feel), null);
  assert.equal(hitSize("bump", { a: 2, b: 3, dv: 10 }, feel), null);
  assert.ok(hitSize("bump", { a: 3, b: 0, dv: 10 }, feel));
});

test('grid: the next 8th at 96 bpm on the context clock, or on the song beat when one plays', () => {
  const step = 60 / 96 / 2;
  assert.equal(gridWait(0, 96, 2), 0);
  assert.ok(Math.abs(gridWait(step * 0.25, 96, 2) - step * 0.75) < 1e-9);
  assert.ok(Math.abs(gridWait(99, 96, 2, { bpm: 120, phase: 0.25 }) - 0.125) < 1e-9);
  assert.ok(gridWait(0.1, 96, 4) <= 60 / 96 / 4);
  assert.ok(Math.abs(gridWait(99, 96, 2, { bpm: 120, phase: 0.25 }, 1.04) - 0.125 / 1.04) < 1e-9);
});

test('drift meter: squeal gain and pitch rise with charge, silent at 0; three distinct release notes', () => {
  assert.equal(squealPlan(0, feel).gain, 0);
  const a = squealPlan(0.3, feel), b = squealPlan(1, feel);
  assert.ok(b.gain > a.gain && b.hz > a.hz && a.gain > 0);
  assert.ok(Math.abs(b.hz - 1500 * Math.pow(2, 0.75)) < 1e-6);
  assert.equal(driftCharge01({ drifting: false, driftCharge: 2 }, feel), 0);
  assert.equal(driftCharge01({ drifting: true, driftCharge: 2.1 }, feel), 1);
  assert.equal(driftCharge01({ drifting: true, driftTier: 2 }, feel), 2 / 3);
  assert.equal(driftCharge01({ drifting: true, driftTier: 3, onRoad: false }, feel), 0);
  assert.equal(new Set(RELEASE_SEMIS).size, 3);
  assert.equal(releaseSemis(0), null); assert.equal(releaseSemis(1), 12); assert.equal(releaseSemis(9), 19);
});

test('wind: silent under 30% speed, gain and cutoff rise with speed', () => {
  assert.equal(windPlan(0.29, feel).gain, 0);
  const a = windPlan(0.6, feel), b = windPlan(1, feel);
  assert.ok(b.gain > a.gain && a.gain > 0 && b.cutHz > a.cutHz);
  assert.equal(windPlan(1.3, feel).gain, b.gain);
});

test('final lap flag, bearing pan, rev plan, percussion steps', () => {
  assert.equal(isFinalLap({ lap: 1, final: true }, 3), true);
  assert.equal(isFinalLap({ lap: 2, final: false }, 3), false);
  assert.equal(isFinalLap({ lap: 2 }, 3), true);
  assert.equal(isFinalLap({ lap: 1 }, 3), false);
  assert.equal(isFinalLap({ lap: 0 }, 1), false);
  assert.equal(bearingPan(null), 0); assert.equal(bearingPan(Math.PI / 2), 1); assert.ok(bearingPan(-Math.PI / 2) < 0);
  assert.equal(revPlan(0, feel).gain, 0);
  assert.ok(revPlan(1, feel).hz > revPlan(0.2, feel).hz && revPlan(1, feel).gain > revPlan(0.2, feel).gain);
  assert.deepEqual([0, 1, 2, 3].map(percStep), ['kick', 'hat', 'kick', 'hat']);
});

function fakeCtx() {
  const log = { started: 0, sets: [], rates: [] };
  const param = (v = 0) => {
    const p = { value: v };
    for (const k of ['setValueAtTime', 'linearRampToValueAtTime', 'exponentialRampToValueAtTime', 'setTargetAtTime', 'cancelScheduledValues']) {
      p[k] = (val) => { log.sets.push([k, val]); if (typeof val === 'number' && k !== 'cancelScheduledValues') p.value = val; return p; };
    }
    return p;
  };
  const node = (extra = {}) => ({ connect() {}, disconnect() {}, start() { log.started++; }, stop() {}, ...extra });
  const ctx = {
    state: 'suspended', currentTime: 0, sampleRate: 8000, destination: node(),
    createGain: () => node({ gain: param(1) }),
    createBiquadFilter: () => node({ type: 'lowpass', frequency: param(350), Q: param(1) }),
    createOscillator: () => node({ type: 'sine', frequency: param(440), detune: param(0) }),
    createBufferSource: () => node({ buffer: null, loop: false, playbackRate: param(1) }),
    createBuffer: (_c, len) => ({ getChannelData: () => new Float32Array(len) }),
    resume: async () => { ctx.state = 'running'; },
    suspend: async () => { ctx.state = 'suspended'; },
    close() { ctx.state = 'closed'; },
  };
  return { ctx, log };
}

async function rig() {
  const bus = createBus(), { ctx, log } = fakeCtx();
  const audio = createAudio(createFeel(feelJson), bus, { win: null, makeCtx: () => ctx, fetchFn: async () => { throw Error('offline'); } });
  await audio.start();
  return { bus, ctx, log, audio };
}

test('bus: new events are harmless before the context exists and while paused', async () => {
  const bus = createBus(), { ctx, log } = fakeCtx();
  const audio = createAudio(createFeel(feelJson), bus, { win: null, makeCtx: () => ctx });
  for (const [n, p] of [['rev', { level: 1 }], ['launch', { grade: 'perfect' }], ['hitter-hit', { racer: 0, hitter: 2, from: 1 }], ['lap', { lap: 2, final: true }]]) bus.emit(n, p);
  assert.equal(log.started, 0);
  await audio.start(); audio.setPaused(true);
  const before = log.started;
  bus.emit('launch', { grade: 'perfect' }); bus.emit('wall', { dv: 12 });
  assert.equal(log.started, before);
  audio.dispose();
});

test('bus: a big hit thumps and ducks bed + music to the hit floor, a bump only ducks', async () => {
  const { bus, log, audio } = await rig();
  log.sets.length = 0;
  bus.emit('item-hit', { victim: 0, by: 3, verdict: 'hit' });
  assert.ok(log.sets.some(([k, v]) => k === 'setTargetAtTime' && v === 0.45), 'item floor');
  assert.equal(audio.feelVoices.thumps, 1);
  bus.emit('bump', { dv: 10 });
  assert.equal(audio.feelVoices.thumps, 1);
  assert.ok(log.sets.some(([k, v]) => k === 'setTargetAtTime' && v === 0.8), 'bump floor');
  audio.dispose();
});

test('bus: final lap brings the percussion in, finish and reset take it out, the song rate follows music.finalLapRate', async () => {
  const { bus, ctx, log, audio } = await rig();
  bus.emit('lap', { lap: 1, lapTimeS: 30, timeS: 30 });
  assert.equal(audio.feelVoices.finalLap, false);
  bus.emit('lap', { lap: 2, lapTimeS: 30, timeS: 60 });            // derived from the lap count (3 laps)
  assert.equal(audio.feelVoices.finalLap, true);
  const before = log.started;
  ctx.currentTime = 1; audio.update(0.8, { active: true }); ctx.currentTime = 1.2; audio.update(0.8, { active: true });
  assert.ok(log.started > before, 'percussion steps scheduled');
  bus.emit('finish', { timeS: 90 });
  assert.equal(audio.feelVoices.finalLap, false);
  bus.emit('lap', { lap: 0, final: true }); assert.equal(audio.feelVoices.finalLap, true);
  audio.reset(); assert.equal(audio.feelVoices.finalLap, false);
  assert.equal(audio.music.rate, 1);
  audio.music.setRate(1.04); assert.equal(audio.music.rate, 1.04);
  audio.music.setRate(7); assert.equal(audio.music.rate, 1);
  audio.dispose();
});

test('bus: rev follows its level and lets go, launch grades and hitter chimes play', async () => {
  const { bus, ctx, log, audio } = await rig();
  bus.emit('rev', { level: 0.8 }); assert.equal(audio.feelVoices.rev, 0.8);
  ctx.currentTime = 1; audio.update(0, { active: true }); assert.equal(audio.feelVoices.rev, 0);
  for (const grade of ['perfect', 'good', 'early']) {
    const b = log.started; bus.emit('launch', { grade, tier: 1 }); assert.ok(log.started > b, grade);
  }
  const b = log.started; bus.emit('launch', { grade: 'none' }); assert.equal(log.started, b);
  let s = log.started; bus.emit('hitter-hit', { racer: 3, hitter: 0, from: null }); assert.equal(log.started, s + 2);
  s = log.started; bus.emit('hitter-hit', { racer: 0, hitter: 4, from: 1.2 }); assert.equal(log.started, s + 1);
  s = log.started; bus.emit('hitter-hit', { racer: 3, hitter: 5, from: 0 }); assert.equal(log.started, s);
  s = log.started; bus.emit('drift-release', { tier: 2 }); assert.ok(log.started >= s + 2);
  audio.dispose();
});
