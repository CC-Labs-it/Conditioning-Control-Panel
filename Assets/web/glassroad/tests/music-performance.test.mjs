import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { createMusicPerformance, stepMusicPerformance, PERFORMANCE_FIELDS, MUSIC_PERFORMANCE } from '../render/musicPerformance.js';
import { normalizeChart } from '../audio/chart.js';
import { createMusicReactivity } from '../audio/reactivity.js';

const signal = () => ({ active: true, songS: 0, rawEnergy: .08, rawBass: .06, rawMid: .04, rawTreble: .02,
  spectrum: new Float32Array(64) });
function run(v, s, seconds, options, frame) {
  for (let i = 0; i < Math.round(seconds * 60); i++) {
    if (frame) frame(i, s);
    s.songS += 1 / 60; stepMusicPerformance(v, s, 1 / 60, options);
  }
}
function loud(s) { s.rawEnergy = .65; s.rawBass = .8; s.rawMid = .58; s.rawTreble = .24; }

test('ordinary repetitive kicks and loading a loud passage never become repeated drops', () => {
  const v = createMusicPerformance(), s = signal(); loud(s);
  run(v, s, 12, undefined, (i, f) => { f.rawEnergy = i % 30 < 3 ? .65 : .48; f.rawBass = i % 30 < 3 ? .9 : .2; });
  assert.equal(v.dropCount, 0); assert.equal(v.drop, 0);
  assert.ok(v.pluckS.some(t => t > 0), 'ordinary attacks still pluck the strings');
  assert.ok(Math.max(...v.pluckPeak) < .7, 'small notes leave room above them for a drop');
});

test('a drop needs sustained re-entry after a quieter passage and stays distinct from a short kick', () => {
  const v = createMusicPerformance(), s = signal(); run(v, s, 1);
  loud(s); run(v, s, 1 / 60);
  s.rawEnergy = .08; run(v, s, .25);
  assert.equal(v.dropCount, 0, 'one loud sample is not a drop');
  loud(s); run(v, s, .5);
  assert.equal(v.dropCount, 1); assert.ok(v.drop > .55);
  assert.ok(v.pluckPeak.every(x => x > .85), 'a drop excites all four strings much more than a small note');
  run(v, s, 5); assert.equal(v.dropCount, 1); assert.ok(v.drop < .02, 'drop has a clear recovery');
  s.rawEnergy = .07; run(v, s, 1); loud(s); run(v, s, .5);
  assert.equal(v.dropCount, 2, 'a later quiet passage can arm a fresh drop');
});

test('authored drop triggers and section boundaries use the heard clock with a cooldown', () => {
  const song = normalizeChart({ triggers: [{ startS: 1, kind: 'drop', strength: 1 },
    { startS: 1.2, kind: 'drop', strength: 1 }], sections: [{ startS: 0, endS: 4, mood: 'groove' },
    { startS: 4, endS: 8, mood: 'drop', energy: .9 }] });
  const v = createMusicPerformance(), s = signal(); loud(s);
  run(v, s, 1.5, { song }); assert.equal(v.dropCount, 1);
  run(v, s, 3, { song }); assert.equal(v.dropCount, 2);
  assert.ok(MUSIC_PERFORMANCE.dropCooldownS > 2.5);
  s.songS = 5.5; stepMusicPerformance(v, s, .1, { song });
  assert.equal(v.dropCount, 0, 'seeking into an existing drop does not replay its boundary');
});

test('quiet recordings retain drop contrast and digital silence can arm re-entry without treating unmute as a drop', () => {
  const v = createMusicPerformance(), s = signal(); s.rawEnergy = .018;
  run(v, s, 1); s.rawEnergy = .11; run(v, s, .5); assert.equal(v.dropCount, 1);
  s.active = false; s.silent = true; s.rawEnergy = s.rawBass = s.rawMid = s.rawTreble = 0;
  run(v, s, 1); assert.equal(v.drop, 0); assert.equal(v.dropCount, 0);
  s.active = true; s.silent = false; s.rawEnergy = .11; s.rawMid = .07; run(v, s, .5);
  assert.equal(v.dropCount, 1, 'audible re-entry after true silence can be a drop');
  s.active = false; s.muted = true; run(v, s, 1);
  s.active = true; s.muted = false; run(v, s, 1);
  assert.equal(v.dropCount, 0, 'unmute primes the current level instead of inventing a drop');
});

test('the one analyser identifies mute, actual silent samples and stopped playback separately', () => {
  let level = .05;
  const analyser = { frequencyBinCount: 512, disconnect() {}, getFloatTimeDomainData(a) { a.fill(level); },
    getFloatFrequencyData(a) { a.fill(-60); } };
  const ctx = { state: 'running', sampleRate: 48000, createAnalyser() { return analyser; } };
  const r = createMusicReactivity(ctx, { connect() {}, disconnect() {} });
  r.read(.1, { playing: true, songS: 1 }); assert.equal(r.frame.silent, false);
  level = 0; r.read(.1, { playing: true, songS: 2 }); assert.equal(r.frame.silent, true); assert.equal(r.frame.muted, false);
  r.read(.1, { playing: true, muted: true, songS: 3 }); assert.equal(r.frame.silent, false); assert.equal(r.frame.muted, true);
  r.read(.1, { playing: false, songS: 4 }); assert.equal(r.frame.silent, false); assert.equal(r.frame.muted, false);
  r.dispose();
});

test('midrange melodic movement produces stronger lead response and independent string plucks', () => {
  const melody = createMusicPerformance(), drums = createMusicPerformance(), m = signal(), d = signal();
  let maxLead = 0;
  for (let i = 0; i < 240; i++) {
    const hit = i % 18 === 0;
    m.rawEnergy = .4; m.rawMid = hit ? .9 : .46; m.rawBass = .1; m.rawTreble = .1;
    d.rawEnergy = .4; d.rawMid = .1; d.rawBass = hit ? .9 : .46; d.rawTreble = .1;
    m.spectrum.fill(0); m.spectrum[20 + (Math.floor(i / 18) % 12) * 2] = .7;
    m.songS += 1 / 60; d.songS = m.songS;
    stepMusicPerformance(melody, m, 1 / 60); stepMusicPerformance(drums, d, 1 / 60);
    maxLead = Math.max(maxLead, melody.lead);
  }
  assert.ok(maxLead > .5); assert.ok(melody.lead > drums.lead + .2);
  assert.ok(melody.pluckS.every(t => t > 0), 'real attacks rotate across all four strings');
  assert.equal(new Set(melody.pluckS).size, 4, 'ordinary attacks are asynchronous');
  assert.equal(melody.dropCount, 0, 'a melodic solo is not itself a drop');
});

test('Gentle rises slowly at reduced amplitude and motion off clears every added effect', () => {
  const song = normalizeChart({ triggers: [{ startS: 1, kind: 'drop', strength: 1 }] });
  const v = createMusicPerformance(), s = signal(); loud(s);
  run(v, s, .99, { song, gentle: true }); run(v, s, 1 / 60, { song, gentle: true });
  assert.equal(v.dropCount, 1); assert.ok(v.drop > 0 && v.drop < .025);
  assert.ok(v.pluckStrength0 < .025);
  run(v, s, .3, { song, gentle: true });
  assert.ok(v.drop > .12 && v.drop < .25); assert.ok(v.pluckStrength0 < .25);
  run(v, s, 1, { song, gentle: true });
  assert.ok(v.drop < .4 && v.lead < .4); assert.ok(v.pluckStrength0 < .4);
  stepMusicPerformance(v, s, .1, { song, motion: 'off' });
  for (const key of PERFORMANCE_FIELDS) assert.equal(v[key], key.includes('Age') ? 9 : 0, key);
});

test('pause holds fixed storage, silence and mute reset, and song changes or seeks discard old events', () => {
  const song = normalizeChart({ triggers: [{ startS: 1, kind: 'drop' }] });
  const v = createMusicPerformance(), s = signal(); loud(s); run(v, s, 1.2, { song });
  const before = structuredClone(v), arrays = [v.previousBins, v.baseline, v.pluckS, v.pluckPeak];
  s.songS += 5; stepMusicPerformance(v, s, .1, { song, paused: true }); assert.deepEqual(v, before);
  s.frozen = true; stepMusicPerformance(v, s, .1, { song }); assert.deepEqual(v, before); s.frozen = false;
  s.songS = before.lastS; stepMusicPerformance(v, s, .1, { song }); assert.deepEqual(v, before);
  stepMusicPerformance(v, s, .1, { song, muted: true }); assert.equal(v.drop, 0); assert.equal(v.dropCount, 0);
  run(v, s, .4, { song }); s.songS = .5; stepMusicPerformance(v, s, .1, { song }); assert.equal(v.dropCount, 0);
  run(v, s, .7, { song }); assert.equal(v.dropCount, 1);
  stepMusicPerformance(v, s, .1, { song: {} }); assert.equal(v.dropCount, 0);
  s.active = false; stepMusicPerformance(v, s, .1); assert.equal(v.lead, 0); assert.equal(v.pluckStrength0, 0);
  [v.previousBins, v.baseline, v.pluckS, v.pluckPeak].forEach((array, i) => assert.equal(array, arrays[i]));
});

const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { 'three': 'three.webgpu.min.js', 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { createAudioReactiveUniforms, updateAudioReactiveUniforms, disposeAudioReactiveUniforms } = await import('../render/audioReactive.js');
hooks.deregister();
test('the existing uniform owner exposes performance without replacing uniforms or textures', () => {
  const u = createAudioReactiveUniforms(), s = signal(), texture = u.vizTexture;
  const uniforms = PERFORMANCE_FIELDS.map(k => u[k]);
  const song = normalizeChart({ triggers: [{ startS: .5, kind: 'drop' }], hits: { kick: [[.25, .9]] } });
  loud(s);
  for (let i = 0; i < 60; i++) { s.songS += 1 / 60; updateAudioReactiveUniforms(u, s, 1 / 60, { song }); }
  assert.equal(u.dropCount.value, 1); assert.ok(u.kickCount.value > 0, 'existing instrument clock remains independent');
  for (let i = 0; i < PERFORMANCE_FIELDS.length; i++) assert.equal(u[PERFORMANCE_FIELDS[i]], uniforms[i]);
  assert.equal(u.vizTexture, texture); disposeAudioReactiveUniforms(u);
});
