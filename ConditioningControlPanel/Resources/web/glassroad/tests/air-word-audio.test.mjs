import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { playAirWordHop, playAirWordLand } from '../audio/airWordHop.js';
import { AUDIO_CUES } from '../audio/cues.js';
import { sfxPlan, resolvePlan } from '../audio/sfxmap.js';
import { createAudio } from '../audio/bus.js';
import { createFeel } from '../core/feel.js';
import { createBus } from '../core/events.js';

const payload = index => ({ id: 'word-' + index, section: 'crossing', index, s: 100, p: [0, 0, 0] });
function recorder(gentle = false) {
  const sounds = [], syn = { ctx: { currentTime: 1 }, feel: { get: () => gentle },
    semis: n => 220 * 2 ** (n / 12), tone: (hz, options) => sounds.push({ hz, ...options }),
    noise: options => sounds.push({ noise: true, ...options }) };
  return { syn, sounds };
}

test('word launch has a bounded ascending spring and resets the melody at each crossing', () => {
  const { syn, sounds } = recorder(), pitches = [];
  for (let index = 0; index < 20; index++) {
    syn.ctx.currentTime += 1;
    assert.equal(playAirWordHop(syn, payload(index)), true);
    const batch = sounds.slice(-5); pitches.push(batch[0].hz);
    assert.equal(batch.length, 5);
    assert.ok(batch.every(s => s.vol <= .046 && s.dur <= .3 && (s.at || 0) <= .055));
    assert.ok(batch.filter(s => s.hz).every(s => s.hz <= 2200));
  }
  assert.ok(pitches[1] > pitches[0]);
  assert.ok(pitches.every((p, i) => !i || p >= pitches[i - 1]));
  assert.equal(pitches[7], pitches[19]);
  syn.ctx.currentTime += 1;
  playAirWordHop(syn, { ...payload(0), section: 'next-crossing' });
  assert.equal(sounds.at(-5).hz, pitches[0]);
});

test('duplicate launches stay quiet; Gentle softens each layer and the air sweep', () => {
  const normal = recorder(), gentle = recorder(true);
  playAirWordHop(normal.syn, payload(0)); playAirWordHop(gentle.syn, payload(0));
  assert.equal(playAirWordHop(normal.syn, payload(0)), false);
  assert.equal(normal.sounds.length, 5);
  assert.ok(gentle.sounds.every((s, i) => s.vol < normal.sounds[i].vol));
  assert.ok(gentle.sounds.at(-1).sweep < normal.sounds.at(-1).sweep);
  normal.syn.ctx.currentTime += .31;
  assert.equal(playAirWordHop(normal.syn, payload(0)), true);
  for (const p of [{}, payload(-1), payload(Infinity), payload(.5)]) assert.equal(playAirWordHop(normal.syn, p), false);
});

test('bank-loaded audio keeps a distinct contact tap and rebound spring', () => {
  assert.equal(AUDIO_CUES['air-word-launch'], playAirWordHop);
  assert.equal(resolvePlan(sfxPlan('air-word-launch', payload(0)), () => true), 'synth');
  assert.equal(AUDIO_CUES['air-word-hop'], playAirWordHop);
  assert.equal(resolvePlan(sfxPlan('air-word-hop', payload(0)), () => true), 'synth');
  assert.equal(resolvePlan(sfxPlan('land', { tier: 3, airWord: true }), () => true), 'synth');
  const { syn, sounds } = recorder();
  AUDIO_CUES.land(syn, { tier: 3, airWord: true }); assert.equal(sounds.length, 3);
  assert.ok(sounds.every(s => s.dur <= .12 && s.vol <= .075));
  AUDIO_CUES.land(syn, { tier: 3 }); assert.equal(sounds.length, 5);
  const soft = recorder(true); playAirWordLand(soft.syn);
  assert.ok(soft.sounds.every((s, i) => s.vol < sounds[i].vol));
});

function context() {
  const oscillators = [];
  const param = (value = 0) => {
    const p = { value };
    for (const method of ['setValueAtTime', 'linearRampToValueAtTime', 'exponentialRampToValueAtTime', 'setTargetAtTime'])
      p[method] = value => { p.value = value; return p; };
    p.cancelScheduledValues = () => p; return p;
  };
  const node = extras => ({ outputs: [], connect(n) { this.outputs.push(n); }, disconnect() {}, start() {}, stop() {}, ...extras });
  const ctx = { state: 'running', currentTime: 10, sampleRate: 8000, destination: node(),
    createGain: () => node({ gain: param(1) }),
    createBiquadFilter: () => node({ frequency: param(350), Q: param(1) }),
    createOscillator: () => { const n = node({ frequency: param(440), detune: param() }); oscillators.push(n); return n; },
    createBufferSource: () => node({ playbackRate: param(1) }),
    createBuffer: (_channels, length) => ({ getChannelData: () => new Float32Array(length) }),
    decodeAudioData: async () => ({ duration: 2 }),
    resume: async () => { ctx.state = 'running'; }, suspend: async () => { ctx.state = 'suspended'; },
    close() { ctx.state = 'closed'; } };
  return { ctx, oscillators };
}
function mutedAlongPath(node, seen = new Set()) {
  if (seen.has(node)) return false; seen.add(node);
  return node.gain?.value === 0 || node.outputs.some(n => mutedAlongPath(n, seen));
}

test('real registry routes word springs through master and SFX controls, freezes on pause and unsubscribes', async () => {
  const feel = createFeel(JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8')));
  const bus = createBus(), { ctx, oscillators } = context();
  const audio = createAudio(feel, bus, { win: null, makeCtx: () => ctx,
    fetchFn: async () => ({ ok: true, arrayBuffer: async () => new ArrayBuffer(8) }) });
  await audio.start(); await audio.samplesReady; assert.ok(audio.sfx.loaded > 0);
  const before = oscillators.length;
  bus.emit('air-word-launch', payload(0)); assert.equal(oscillators.length - before, 4);
  // Skip each local envelope, whose fake AudioParam already holds its scheduled final zero.
  const voices = oscillators.slice(before).map(n => n.outputs[0].outputs[0]);
  audio.setVolumes({ sfx: 0 }); assert.ok(voices.every(n => mutedAlongPath(n)));
  audio.setVolumes({ sfx: 1, master: 0 }); assert.ok(voices.every(n => mutedAlongPath(n)));
  audio.setVolumes({ master: 1 }); assert.ok(voices.every(n => !mutedAlongPath(n)));
  audio.setPaused(true); const paused = oscillators.length;
  bus.emit('air-word-hop', payload(1)); assert.equal(oscillators.length, paused);
  audio.setPaused(false); ctx.currentTime += 1;
  bus.emit('air-word-hop', payload(1)); assert.equal(oscillators.length - paused, 4);
  audio.dispose(); const done = oscillators.length;
  bus.emit('air-word-hop', payload(2)); assert.equal(oscillators.length, done);
});
