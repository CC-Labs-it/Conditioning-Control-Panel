import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createMenuMelody, renderMenuMelody, MENU_MELODY } from '../audio/menuMelody.js';
import { createAudio } from '../audio/bus.js';
import { createFeel } from '../core/feel.js';
import { createBus } from '../core/events.js';

const feel = () => createFeel(JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8')));

function fakeContext() {
  const sources = [], gains = [], sets = [];
  function param(value = 0) {
    const p = { value, cancelScheduledValues() {}, setTargetAtTime(v, at, tc) { p.value = v; sets.push({ p, v, at, tc }); },
      setValueAtTime(v) { p.value = v; }, linearRampToValueAtTime(v) { p.value = v; }, exponentialRampToValueAtTime(v) { p.value = v; } };
    return p;
  }
  const node = extra => ({ connect(dest) { this.destination = dest; }, disconnect() { this.disconnected = true; }, ...extra });
  const ctx = {
    currentTime: 2, state: 'running', sampleRate: 8000, destination: node({}),
    createGain() { const n = node({ gain: param(1) }); gains.push(n); return n; },
    createBiquadFilter: () => node({ frequency: param(), Q: param() }),
    createOscillator: () => node({ frequency: param(), detune: param(), start() {}, stop() {} }),
    createBufferSource() { const s = node({ playbackRate: param(1), start() { this.started = true; }, stop(at) { this.stoppedAt = at; } }); sources.push(s); return s; },
    createBuffer(channels, length, sampleRate) { const data = Array.from({ length: channels }, () => new Float32Array(length));
      return { duration: length / sampleRate, sampleRate, numberOfChannels: channels, getChannelData: i => data[i] }; },
    async resume() { ctx.state = 'running'; }, async suspend() { ctx.state = 'suspended'; }, close() { ctx.state = 'closed'; },
  };
  return { ctx, sources, gains, sets };
}

test('upbeat original instrumental keeps its 120 BPM pocket quiet and joins without a click', () => {
  const rendered = renderMenuMelody(12000);
  assert.equal(rendered.duration, 24); assert.equal(MENU_MELODY.bpm, 120); assert.equal(MENU_MELODY.beats, 48);
  let difference = 0;
  for (const samples of rendered.channels) {
    let peak = 0, sum = 0;
    for (let i = 0; i < samples.length; i++) { assert.ok(Number.isFinite(samples[i])); peak = Math.max(peak, Math.abs(samples[i])); sum += samples[i] ** 2; }
    const rms = Math.sqrt(sum / samples.length);
    assert.ok(peak < .2 && peak > .03, `peak ${peak}`);
    assert.ok(rms > .012 && rms < .055, `rms ${rms}`);
    assert.ok(Math.abs(samples[0] - samples.at(-1)) < .012, 'circular note tails join without a click');
  }
  for (let i = 0; i < rendered.channels[0].length; i++) difference += Math.abs(rendered.channels[0][i] - rendered.channels[1][i]);
  assert.ok(difference > 100, 'melody and echoes use a soft stereo spread');
});

test('menu uses one cached loop with smooth transitions and at most two sources during rapid toggles', () => {
  const { ctx, sources, sets } = fakeContext(), out = ctx.createGain(), menu = createMenuMelody(ctx, out);
  menu.update(true); const first = sources[0], buffer = first.buffer;
  assert.equal(first.loop, true); assert.equal(first.destination.destination, out);
  assert.equal(sets.at(-1).v, 1); assert.equal(sets.at(-1).tc, MENU_MELODY.fadeInS / 3);
  for (let i = 0; i < 300; i++) menu.update(true);
  assert.equal(sources.length, 1, 'no per-frame sound allocation');
  menu.update(false); assert.equal(first.stoppedAt, ctx.currentTime + MENU_MELODY.fadeOutS);
  assert.equal(menu.playing, false);
  for (let i = 0; i < 12; i++) { menu.update(true); assert.equal(sources.at(-1).buffer, buffer); menu.update(false); assert.ok(menu.inspect.voices <= 2); }
  menu.update(true); const final = sources.at(-1); menu.dispose();
  assert.equal(final.disconnected, true); assert.deepEqual(menu.inspect, { playing: false, cached: false, voices: 0 });
  menu.update(true); assert.equal(sources.at(-1), final, 'disposed menu never resurrects');
});

test('menu is gesture-gated and follows music/master volume, pause and race transitions on the shared context', async () => {
  const { ctx, gains, sources } = fakeContext(); ctx.state = 'suspended'; let contexts = 0;
  const audio = createAudio(feel(), createBus(), { win: null, makeCtx: () => { contexts++; return ctx; }, fetchFn: async () => { throw Error('offline'); } });
  audio.update(0, { active: false, menu: true }); assert.equal(contexts, 0);
  await audio.start(); audio.update(0, { active: false, menu: true });
  assert.equal(contexts, 1); assert.equal(audio.menuMusic.playing, true); assert.equal(audio.music.playing, false);
  const menuSource = sources.find(s => s.buffer?.duration === 24), menuOutput = menuSource.destination.destination;
  assert.equal(menuOutput.gain.value, .85, 'instrumental routes through the music gain');
  audio.setVolumes({ master: 0, music: 0, sfx: 1 });
  assert.equal(menuOutput.gain.value, 0, 'music slider silences the instrumental');
  assert.equal(menuOutput.destination.destination.gain.value, 0, 'master slider silences the instrumental');
  audio.setPaused(true); assert.equal(ctx.state, 'suspended');
  const paused = audio.menuMusic.voices; audio.update(0, { active: false, menu: true }); assert.equal(audio.menuMusic.voices, paused);
  audio.setPaused(false); await audio.start(); audio.update(.4, { active: true, menu: false });
  assert.equal(audio.menuMusic.playing, false); assert.equal(contexts, 1);
  audio.update(0, { active: false, menu: true }); assert.equal(audio.menuMusic.playing, true);
  assert.ok(gains.every(g => g.gain.value !== .035), 'legacy continuous section pad is silenced in the menu');
  audio.dispose(); assert.equal(ctx.state, 'closed'); assert.equal(audio.menuMusic.playing, false);
});
