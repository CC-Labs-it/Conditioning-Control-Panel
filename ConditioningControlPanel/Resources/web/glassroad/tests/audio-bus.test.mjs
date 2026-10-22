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
