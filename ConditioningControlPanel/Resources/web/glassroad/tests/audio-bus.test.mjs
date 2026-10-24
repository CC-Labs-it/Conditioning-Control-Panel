import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { createFeel } from '../core/feel.js';
import { createBus } from '../core/events.js';
import { createAudio } from '../audio/bus.js';
import { AUDIO_CUES } from '../audio/cues.js';

const feelJson = JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));
const contract = readFileSync(new URL('../CONTRACT.md', import.meta.url), 'utf8');
const EVENTS = [...contract.slice(contract.indexOf('## 8. Events')).matchAll(/^\| `([a-z-]+)` \|/gm)].map((m) => m[1]);

/** A WebAudio stand-in that records every node and scheduled value. */
function fakeCtx() {
  const log = { nodes: 0, started: 0, sets: [] };
  const param = (v = 0) => {
    const p = { value: v };
    for (const k of ['setValueAtTime', 'linearRampToValueAtTime', 'exponentialRampToValueAtTime', 'setTargetAtTime', 'cancelScheduledValues']) {
      p[k] = (val) => { log.sets.push([k, val]); if (typeof val === 'number' && k !== 'cancelScheduledValues') p.value = val; return p; };
    }
    return p;
  };
  const node = (extra = {}) => { log.nodes++; return { connect() {}, disconnect() {}, start() { log.started++; }, stop() {}, ...extra }; };
  const ctx = {
    state: 'suspended', currentTime: 0, sampleRate: 8000, destination: node(),
    createGain: () => node({ gain: param(1) }),
    createBiquadFilter: () => node({ type: 'lowpass', frequency: param(350), Q: param(1) }),
    createOscillator: () => node({ type: 'sine', frequency: param(440), detune: param(0) }),
    createBufferSource: () => node({ buffer: null, loop: false }),
    createBuffer: (_c, len) => ({ getChannelData: () => new Float32Array(len) }),
    resume: async () => { ctx.state = 'running'; },
    suspend: async () => { ctx.state = 'suspended'; },
    close() { ctx.state = 'closed'; },
  };
  return { ctx, log };
}

test('every audio cue is a CONTRACT event', () => {
  for (const k of Object.keys(AUDIO_CUES)) assert.ok(EVENTS.includes(k), k);
});

test('no speech anywhere in fx, audio, hud or host', () => {
  for (const dir of ['fx', 'audio', 'hud', 'host']) {
    for (const f of readdirSync(new URL('../' + dir + '/', import.meta.url))) {
      if (!/\.(m?js|html)$/.test(f)) continue;            // audio/sfx/ holds the sound files
      const src = readFileSync(new URL('../' + dir + '/' + f, import.meta.url), 'utf8');
      assert.ok(!/speechSynthesis|SpeechSynthesisUtterance/.test(src), dir + '/' + f);
    }
  }
});

test('audio: silent until start, then every cue and the engine run on one context', async () => {
  const feel = createFeel(feelJson), bus = createBus();
  const { ctx, log } = fakeCtx();
  let made = 0;
  const audio = createAudio(feel, bus, { win: null, makeCtx: () => { made++; return ctx; } });
  bus.emit('go');
  assert.equal(made, 0);
  await audio.start();
  assert.equal(made, 1); assert.equal(ctx.state, 'running');
  const started = log.started;
  for (const name of Object.keys(AUDIO_CUES)) bus.emit(name, { n: 3, tier: 3, dv: 12, to: 1, kind: 'drift' });
  assert.ok(log.started > started + 10);
  audio.update(0, {}); const low = log.sets.length;
  audio.update(1.2, { boosting: true, drifting: true, driftTier: 3, duck: 1 });
  assert.ok(log.sets.length > low);
  audio.setPaused(true);
  await Promise.resolve();
  assert.equal(ctx.state, 'suspended');
  const n = log.started; bus.emit('pad', { index: 0 });
  assert.equal(log.started, n, 'paused plays nothing');
  audio.setPaused(false); await audio.start();
  assert.equal(made, 1, 'one context');
  audio.dispose();
});

test('platform chain ascends on distinct contacts, stays bounded and resets between fields or after a fall', () => {
  const tones = [], syn = { ctx: { currentTime: 0 }, semis: n => 220 * 2 ** (n / 12),
    tone(hz) { tones.push(hz); }, release() {}, hold() {} };
  for (let i = 0; i < 12; i++) {
    syn.ctx.currentTime = i * 1.2;
    AUDIO_CUES.stone(syn, { id: 'hex-' + i, field: 'memory' });
    const count = tones.length;
    AUDIO_CUES.stone(syn, { id: 'hex-' + i, field: 'memory' });
    assert.equal(tones.length, count, 'lingering on one island does not ring repeatedly');
  }
  const roots = tones.filter((_, i) => i % 3 === 0);
  assert.ok(roots.every((hz, i) => !i || hz > roots[i - 1]));
  for (let i = 12; i < 200; i++) {
    syn.ctx.currentTime = i * 1.2;
    AUDIO_CUES.stone(syn, { id: 'hex-' + i, field: 'memory' });
  }
  assert.ok(tones.at(-3) < 3000, 'long chains stay in the useful note range');
  AUDIO_CUES.stone(syn, { id: 'next', field: 'another-field' });
  assert.equal(tones.at(-3), roots[0]);
  AUDIO_CUES.drop(syn);
  AUDIO_CUES.stone(syn, { id: 'retry', field: 'another-field' });
  assert.equal(tones.at(-3), roots[0]);
  syn.ctx.currentTime += 9;
  AUDIO_CUES.stone(syn, { id: 'later', field: 'another-field' });
  assert.equal(tones.at(-3), roots[0]);
});

test('platform notes use the existing audio bus, stay local and honor pause and restart', async () => {
  const bus = createBus(), { ctx, log } = fakeCtx();
  const audio = createAudio(createFeel(feelJson), bus, { win: null, makeCtx: () => ctx, fetchFn: async () => { throw Error('offline'); } });
  bus.emit('stone', { id: 'first', field: 'memory' });
  assert.equal(log.started, 0);
  await audio.start();
  const before = log.started;
  bus.emit('stone', { id: 'first', field: 'memory' });
  assert.equal(log.started, before + 3);
  bus.emit('racer-event', { racer: 1, name: 'stone', p: { id: 'bot', field: 'memory' } });
  assert.equal(log.started, before + 3, 'other karts do not advance the local melody');
  audio.setPaused(true);
  bus.emit('stone', { id: 'second', field: 'memory' });
  assert.equal(log.started, before + 3);
  audio.setPaused(false); await audio.start();
  audio.reset();
  const reset = log.started, start = log.sets.length;
  bus.emit('stone', { id: 'first', field: 'memory' });
  assert.equal(log.started, reset + 3, 'restart allows the first island to ring again');
  assert.equal(log.sets.slice(start).find(([kind, n]) => kind === 'setValueAtTime' && n > 100)[1], 220);
  audio.dispose();
});
