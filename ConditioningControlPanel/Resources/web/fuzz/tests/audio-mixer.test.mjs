import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createMixer } from '../audio/mixer.js';
import { createKit, BUS } from '../audio/kit.js';

test('preferences restore before unlocks without exposing locked sources', () => {
  const m = createMixer();
  m.restore(JSON.parse('{"rain":{"volume":0.35,"muted":true},"binaural":{"volume":0.7,"muted":false}}'));
  assert.deepEqual(m.sources(), []);
  m.register('rain', 'Rain');
  assert.deepEqual(m.sources(), [{ id: 'rain', label: 'Rain', volume: .35, muted: true }]);
  const saved = m.snapshot();
  saved.rain.volume = .9;
  assert.equal(m.sources()[0].volume, .35, 'snapshot does not share mutable preferences');
  m.register('binaural', 'Binaural');
  assert.equal(m.sources()[1].volume, .7);
});

test('source controls clamp, reject invalid values, notify changes and unsubscribe', () => {
  const m = createMixer(), seen = [];
  const off = m.subscribe((rows) => seen.push(rows));
  m.register('rain', 'Rain'); m.register('rain', 'Rain');
  assert.equal(seen.length, 1);
  m.setVolume('rain', -5); m.setVolume('rain', NaN); m.setVolume('rain', Infinity);
  m.setMuted('rain', true);
  assert.deepEqual(m.sources()[0], { id: 'rain', label: 'Rain', volume: 0, muted: true });
  m.setVolume('rain', 6);
  assert.equal(m.sources()[0].volume, 1);
  const count = seen.length;
  off(); m.setMuted('rain', false);
  assert.equal(seen.length, count);
  m.restore({ rain: { volume: 'loud', muted: 'yes' } });
  assert.deepEqual(m.sources()[0], { id: 'rain', label: 'Rain', volume: 1, muted: false });
});

class Media {
  constructor() { this.volume = 1; this.muted = false; this.paused = true; this.listeners = {}; }
  addEventListener(type, fn) { (this.listeners[type] ||= []).push(fn); }
  fire(type) { for (const fn of this.listeners[type] || []) fn(); }
  play() { this.paused = false; this.fire('play'); return Promise.resolve(); }
  pause() { this.paused = true; this.fire('pause'); }
  removeAttribute() {}
  load() {}
}

function fixture({ routing = true, arm = true } = {}) {
  const gains = [], sources = [], media = [], panners = [];
  const node = (props = {}) => ({ outputs: [], connect(n) { this.outputs.push(n); return n; }, disconnect() { this.disconnected = true; }, ...props });
  globalThis.window = { AudioContext: class {
    constructor() { this.state = 'running'; this.currentTime = 4; this.destination = node(); }
    createGain() {
      const events = [], gain = { value: 1 };
      for (const kind of ['cancelScheduledValues', 'setTargetAtTime', 'setValueAtTime', 'linearRampToValueAtTime', 'exponentialRampToValueAtTime']) {
        gain[kind] = (...args) => { events.push([kind, ...args]); if (kind !== 'cancelScheduledValues') gain.value = args[0]; };
      }
      const g = node({ gain, events }); gains.push(g); return g;
    }
    createBufferSource() { const s = node({ playbackRate: { value: 1 }, start() { this.started = true; }, stop() { this.stopped = true; this.onended?.(); } }); sources.push(s); return s; }
    createMediaElementSource() { if (!routing) throw new Error('native fallback'); const s = node(); media.push(s); return s; }
    createStereoPanner() { const p = node({ pan: { value: 0 } }); panners.push(p); return p; }
    decodeAudioData() { return Promise.resolve({ duration: 2 }); }
    suspend() { this.state = 'suspended'; return Promise.resolve(); }
    resume() { this.state = 'running'; return Promise.resolve(); }
  } };
  globalThis.fetch = async () => ({ ok: true, arrayBuffer: async () => new ArrayBuffer(1) });
  const elements = [], kit = createKit({ makeMedia: () => { const el = new Media(); elements.push(el); return el; }, now: () => 0 });
  if (arm) kit.arm();
  return { kit, gains, sources, media, elements, panners };
}

test('sources register before gesture and while globally muted, with voices as the voice default', async () => {
  const f = fixture({ arm: false });
  await f.kit.play('voice.mp3', { bus: 'voice' });
  f.kit.setMuted(true);
  await f.kit.play('rain.mp3', { source: 'rain', sourceLabel: 'Rain' });
  assert.deepEqual(f.kit.mixer.sources().map(({ id, label }) => ({ id, label })), [{ id: 'voices', label: 'Voices' }, { id: 'rain', label: 'Rain' }]);
  assert.equal(f.sources.length, 0);
});

test('live source changes never overwrite a shot envelope and sit before its pan and master', async () => {
  const f = fixture();
  await f.kit.play('voice.mp3', { bus: 'voice', gain: .4, source: 'whispers', pan: -.4, fadeInMs: 400, fadeOutMs: 200 });
  const envelope = f.sources[0].outputs[0], sourceGain = envelope.outputs[0];
  const automation = structuredClone(envelope.events);
  assert.equal(sourceGain.outputs[0], f.panners[0]);
  assert.equal(f.panners[0].outputs[0], f.gains[0]);
  f.kit.mixer.setVolume('whispers', .3);
  assert.equal(sourceGain.gain.value, .3);
  f.kit.mixer.setMuted('whispers', true);
  assert.equal(sourceGain.gain.value, 0);
  f.kit.mixer.setMuted('whispers', false);
  assert.equal(sourceGain.gain.value, .3);
  assert.deepEqual(envelope.events, automation);
  f.kit.hold(true);
  f.kit.mixer.setVolume('whispers', 1);
  assert.equal(f.gains[0].gain.value, 0, 'a source slider cannot defeat the master hold');
  assert.equal(f.sources[0].stopped, true);
  assert.equal(sourceGain.disconnected, true);
});

test('running loops keep their fade and one source never alters another', async () => {
  const f = fixture();
  const rain = f.kit.loop('rain.mp3', { source: 'rain', fadeMs: 2000 });
  const binaural = f.kit.loop('binaural.mp3', { source: 'binaural' });
  await new Promise((resolve) => setTimeout(resolve, 0));
  const env = f.sources[0].outputs[0], sourceGain = env.outputs[0], other = f.sources[1].outputs[0].outputs[0];
  const fade = structuredClone(env.events);
  f.kit.mixer.setVolume('rain', .25);
  assert.equal(sourceGain.gain.value, .25);
  assert.equal(other.gain.value, 1);
  assert.deepEqual(env.events, fade);
  rain.stop(0); binaural.stop(0);
  assert.equal(sourceGain.disconnected, true);
});

test('closing a cabinet cancels its in-flight and already playing cues independently', async () => {
  const f = fixture(), first = new AbortController(), second = new AbortController();
  const pending = f.kit.play('wall.wav',{signal:first.signal});
  first.abort(); await pending;
  assert.equal(f.sources.length,0);
  await f.kit.play('paddle.wav',{signal:second.signal});
  await f.kit.play('other.wav');
  second.abort();
  assert.equal(f.sources[0].stopped,true);
  assert.equal(f.sources[1].stopped,undefined);
});

test('routed media has one bus gain and one source gain; mute also reaches the element', () => {
  const f = fixture();
  f.kit.mixer.restore({ rain: { volume: .4, muted: false } });
  const s = f.kit.stream('rain.mp3', { bus: 'ambient', gain: .5, source: 'rain', pan: 1 });
  const el = f.elements[0], envelope = f.media[0].outputs[0], sourceGain = envelope.outputs[0];
  assert.equal(el.volume, 1);
  assert.equal(envelope.gain.value, BUS.ambient * .5);
  assert.equal(sourceGain.gain.value, .4);
  f.kit.mixer.setVolume('rain', 0);
  assert.equal(el.muted, true, 'zero slider also mutes iOS media');
  f.kit.mixer.setVolume('rain', .7);
  assert.equal(el.muted, false);
  assert.equal(sourceGain.gain.value, .7);
  f.kit.mixer.setMuted('rain', true);
  el.muted = false; el.volume = .1; el.fire('volumechange');
  assert.equal(el.muted, true);
  assert.equal(el.volume, 1);
  f.kit.setMuted(true); f.kit.mixer.setMuted('rain', false);
  assert.equal(el.muted, true, 'source unmute cannot defeat master mute');
  f.kit.setMuted(false); s.setGain(.2);
  assert.equal(envelope.gain.value, BUS.ambient * .2);
  s.stop();
  assert.equal(sourceGain.disconnected, true);
});

test('native fallback multiplies source volume once and keeps per-source mute during hold', () => {
  const f = fixture({ routing: false });
  f.kit.stream('rain.mp3', { bus: 'ambient', gain: .5, source: 'rain' });
  const el = f.elements[0];
  f.kit.mixer.setVolume('rain', .5);
  assert.equal(el.volume, BUS.ambient * .5 * .5);
  f.kit.setVolume(.4);
  assert.equal(el.volume, BUS.ambient * .5 * .5 * .4);
  f.kit.mixer.setMuted('rain', true);
  f.kit.hold(true); f.kit.hold(false);
  assert.equal(el.muted, true);
  assert.equal(el.paused, false);
  f.kit.mixer.setMuted('rain', false);
  assert.equal(el.muted, false);
});

test('master volume set before first gesture survives mute, pause and resume', () => {
  const f = fixture({ arm: false });
  f.kit.setVolume(.36);
  f.kit.setMuted(true);
  f.kit.arm();
  const master = f.gains[0].gain;
  assert.equal(master.value, 0);
  f.kit.hold(true);
  f.kit.setMuted(false);
  assert.equal(master.value, 0);
  f.kit.hold(false);
  assert.equal(master.value, .36);
});
