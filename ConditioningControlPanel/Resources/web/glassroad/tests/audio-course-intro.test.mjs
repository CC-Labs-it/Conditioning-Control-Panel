import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { renderCourseIntroAudio, createCourseIntroAudio, INTRO_SOUND } from '../audio/courseIntro.js';
import { createAudio } from '../audio/bus.js';
import { createFeel } from '../core/feel.js';
import { createBus } from '../core/events.js';

function fakeContext() {
  const sources = [];
  const param = (value = 0) => ({ value, cancelScheduledValues() {}, setValueAtTime(v) { this.value = v; },
    linearRampToValueAtTime(v) { this.value = v; }, exponentialRampToValueAtTime(v) { this.value = v; }, setTargetAtTime(v) { this.value = v; } });
  const node = extra => ({ connect(dest) { this.destination = dest; }, disconnect() { this.disconnected = true; }, ...extra });
  const ctx = { currentTime: 1, state: 'running', sampleRate: 8000, destination: node({}),
    createGain: () => node({ gain: param(1) }), createBiquadFilter: () => node({ frequency: param(), Q: param() }),
    createOscillator: () => node({ frequency: param(), detune: param(), start() {}, stop() {} }),
    createBufferSource() { const s = node({ playbackRate: param(1), start(...args) { this.started = args; }, stop(at) { this.stopped = at; } }); sources.push(s); return s; },
    createBuffer(channels, length, rate) { const data = Array.from({ length: channels }, () => new Float32Array(length));
      return { duration: length / rate, sampleRate: rate, numberOfChannels: channels, getChannelData: i => data[i] }; },
    async resume() { ctx.state = 'running'; }, async suspend() { ctx.state = 'suspended'; }, close() { ctx.state = 'closed'; } };
  return { ctx, sources };
}

test('five reveals are distinct, finite stereo audio with quiet peaks and click-free entry/landing', () => {
  const fingerprints = new Set();
  for (const course of ['threshold', 'mainframe', 'keep', 'boudoir', 'goonworks']) {
    const r = renderCourseIntroAudio(course, 12000);
    assert.equal(r.duration, 10.6); assert.equal(r.channels.length, 2);
    let peak = 0, energy = 0, spread = 0;
    for (let i = 0; i < r.channels[0].length; i++) {
      for (const ch of r.channels) { assert.ok(Number.isFinite(ch[i])); peak = Math.max(peak, Math.abs(ch[i])); energy += ch[i] ** 2; }
      spread += Math.abs(r.channels[0][i] - r.channels[1][i]);
    }
    assert.ok(peak > .035 && peak < .2, `${course} peak ${peak}`);
    assert.ok(Math.sqrt(energy / r.channels[0].length / 2) > .008, `${course} is audible`);
    assert.ok(spread > 400, `${course} has stereo space`);
    for (const ch of r.channels) { assert.equal(ch[0], 0); assert.ok(Math.abs(ch.at(-1)) < 1e-6); }
    fingerprints.add(energy.toFixed(5));
  }
  assert.equal(fingerprints.size, 5);
});

test('Mainframe impacts follow each scenic cut; Threshold has separated low stereo tones', () => {
  const machine = renderCourseIntroAudio('mainframe', 12000), rainbow = renderCourseIntroAudio('threshold', 12000);
  const rms = (r, at, length) => { const samples = r.channels[0].slice(at * r.sampleRate, (at + length) * r.sampleRate); return Math.sqrt(samples.reduce((sum, x) => sum + x * x, 0) / samples.length); };
  for (const cut of [2.5, 5, 7.5]) assert.ok(rms(machine, cut + .12, .2) > rms(machine, cut + 1.95, .2) * 2);
  function component(ch, hz) { let re = 0, im = 0; for (let i = 36000; i < 48000; i++) {
    const phase = 2 * Math.PI * hz * i / 12000; re += ch[i] * Math.cos(phase); im += ch[i] * Math.sin(phase);
  } return Math.hypot(re, im); }
  assert.ok(component(rainbow.channels[0], 174) > component(rainbow.channels[0], 180) * 8);
  assert.ok(component(rainbow.channels[1], 180) > component(rainbow.channels[1], 174) * 8);
});

test('visible clock owns offsets, loading holds and seeks without repeated cue bursts or node growth', () => {
  const { ctx, sources } = fakeContext(), intro = createCourseIntroAudio(ctx, ctx.destination);
  intro.prepare('mainframe'); assert.equal(sources.length, 0, 'prepare is silent');
  intro.update('mainframe', 0, { active: true }); const first = sources[0], buffer = first.buffer;
  for (let i = 1; i <= 180; i++) { ctx.currentTime = 1 + i / 60; intro.update('mainframe', i / 60, { active: true }); }
  assert.equal(sources.length, 1, 'no per-frame allocation');
  intro.update('mainframe', 3, { active: true, paused: true }); assert.equal(intro.inspect.playing, false);
  ctx.currentTime += 2; intro.update('mainframe', 3, { active: true });
  assert.deepEqual(sources.at(-1).started, [ctx.currentTime, 3]); assert.equal(sources.at(-1).buffer, buffer);
  intro.update('mainframe', 8, { active: true }); ctx.currentTime += .05; intro.update('mainframe', 8, { active: true }); assert.equal(sources.at(-1).started[1], 8);
  for (let i = 0; i < 20; i++) { intro.update('mainframe', i % 2 ? 1 : 6, { active: true }); assert.ok(intro.inspect.voices <= 2); }
  intro.update('mainframe', INTRO_SOUND.seconds, { active: true }); assert.equal(intro.inspect.playing, false);
  ctx.currentTime += .05; intro.update('keep', .4, { active: true, gentle: true }); assert.notEqual(sources.at(-1).buffer, buffer);
  assert.equal(sources.at(-1).destination.gain.value, .55);
  intro.dispose(); assert.equal(intro.inspect.voices, 0); const count = sources.length;
  intro.update('threshold', 0, { active: true }); assert.equal(sources.length, count);
});

test('#3084: per-frame stops leave the retiring fade alone; held flips never stack or stick voices', () => {
  const { ctx, sources } = fakeContext(), intro = createCourseIntroAudio(ctx, ctx.destination);
  intro.update('keep', 1, { active: true }); const first = sources[0];
  ctx.currentTime += 1 / 60; intro.update('keep', 1 + 1 / 60, { active: true, paused: true });
  assert.ok(Math.abs(first.stopped - (ctx.currentTime + INTRO_SOUND.fadeS)) < 1e-9, 'fade scheduled its own stop');
  const scheduled = first.stopped;
  for (let i = 0; i < 3; i++) { ctx.currentTime += .01; intro.update('keep', 2, { active: true, paused: true }); intro.update('keep', 2); intro.stop(); }
  assert.equal(first.stopped, scheduled, 'later stops never cut the fade short'); assert.ok(!first.disconnected);
  assert.equal(intro.inspect.voices, 1); assert.equal(intro.inspect.playing, false);
  intro.update('keep', 2, { active: true }); assert.equal(sources.length, 1, 'unheld mid-fade waits, no double voice');
  ctx.currentTime += .02; intro.update('keep', 2.1, { active: true });
  assert.ok(first.disconnected, 'retired voice dropped after its stop time even without onended');
  assert.equal(sources.length, 2); assert.equal(sources[1].started[1], 2.1); assert.equal(intro.inspect.voices, 1);
  for (let i = 0; i < 30; i++) { ctx.currentTime += 1 / 60; intro.update('keep', 3 + i / 60, { active: true, paused: i % 2 === 0 }); assert.ok(intro.inspect.voices <= 1); }
  intro.dispose(); assert.equal(intro.inspect.voices, 0);
});

test('course sound shares gesture, SFX/master volume, pause, restart and disposal gates', async () => {
  const { ctx, sources } = fakeContext(); let contexts = 0;
  const feel = createFeel(JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8')));
  const audio = createAudio(feel, createBus(), { win: null, makeCtx: () => { contexts++; return ctx; }, fetchFn: async () => { throw Error('offline'); } });
  audio.prepareIntro('threshold'); audio.updateIntro('threshold', 0, { active: true }); assert.equal(contexts, 0);
  await audio.start(); audio.updateIntro('threshold', 0, { active: true }); assert.equal(contexts, 1);
  assert.equal(audio.music.playing, false, 'race recording still waits for GO');
  const source = sources.find(s => s.buffer?.duration === 10.6), sfx = source.destination.destination;
  audio.setVolumes({ master: 0, sfx: 0 }); assert.equal(sfx.gain.value, 0); assert.equal(sfx.destination.gain.value, 0);
  audio.setPaused(true); assert.equal(audio.introSound.playing, false); assert.equal(ctx.state, 'suspended');
  audio.setPaused(false); ctx.currentTime += .05; audio.updateIntro('threshold', 2, { active: true }); assert.equal(audio.introSound.playing, true);
  audio.reset(); assert.equal(audio.introSound.playing, false);
  audio.prepareIntro('mainframe'); audio.updateIntro('mainframe', 0, { active: true }); assert.equal(audio.introSound.course, 'mainframe');
  audio.dispose(); assert.equal(audio.introSound.voices, 0); assert.equal(ctx.state, 'closed');
});
