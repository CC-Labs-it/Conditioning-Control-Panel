// The audio kit's streams: panic and every level state reach the element with no unlock's help.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { BUS, createKit } from '../audio/kit.js';

class FakeMedia {
  constructor() { this.paused = true; this.volume = 1; this.muted = false; this.listeners = {}; this.plays = 0; }
  addEventListener(t, f) { (this.listeners[t] ||= []).push(f); }
  fire(t) { for (const f of this.listeners[t] || []) f(); }
  play() { this.plays += 1; this.paused = false; this.fire('play'); return Promise.resolve(); }
  pause() { this.paused = true; this.fire('pause'); }
  removeAttribute() { this.src = null; }
  load() {}
}

/** An armed kit. arm() needs an AudioContext, so the test hands it a silent one. */
function armedKit() {
  globalThis.window = { AudioContext: class {
    constructor() { this.state = 'running'; this.currentTime = 0; this.destination = {}; }
    createGain() { return { gain: { value: 1, cancelScheduledValues() {}, setTargetAtTime() {} }, connect() {} }; }
    suspend() { return Promise.resolve(); }
    resume() { return Promise.resolve(); }
  } };
  const made = [];
  const kit = createKit({ makeMedia: () => { const m = new FakeMedia(); made.push(m); return m; } });
  kit.arm();
  return { kit, made };
}

test('a stream waits for the first gesture, then plays at its bus level', () => {
  globalThis.window = { AudioContext: class { constructor() { this.state = 'running'; this.currentTime = 0; this.destination = {}; } createGain() { return { gain: { value: 1 }, connect() {} }; } } };
  const made = [];
  const kit = createKit({ makeMedia: () => { const m = new FakeMedia(); made.push(m); return m; } });
  const s = kit.stream('a.mp3', { bus: 'podcast', gain: 0.5 });
  assert.equal(made[0].plays, 0, 'no sound before a gesture');
  kit.arm();
  assert.equal(s.playing, true);
  assert.equal(made[0].volume, BUS.podcast * 0.5);
  assert.equal(made[0].preload, 'auto');
});

test('hold pauses in the same call, with nothing else running, and release brings it back', () => {
  const { kit, made } = armedKit();
  const s = kit.stream('a.mp3');
  kit.hold(true);
  assert.equal(made[0].paused, true);
  assert.equal(s.playing, false);
  kit.hold(false);
  assert.equal(made[0].paused, false);
});

test('a stream the unlock paused stays paused after a hold', () => {
  const { kit, made } = armedKit();
  const s = kit.stream('a.mp3');
  s.pause();
  kit.hold(true);
  kit.hold(false);
  assert.equal(made[0].paused, true);
});

test('hands on the element cannot beat a hold or a mute', () => {
  const { kit, made } = armedKit();
  kit.stream('a.mp3');
  kit.hold(true);
  made[0].play();
  assert.equal(made[0].paused, true, 'a play during a hold is paused');
  kit.hold(false);
  kit.setMuted(true);
  made[0].muted = false;
  made[0].volume = 1;
  made[0].fire('volumechange');
  assert.equal(made[0].muted, true);
  assert.equal(made[0].volume, BUS.podcast);
});

test('mute, quiet and blank each silence a stream, and it comes back only when all are clear', () => {
  const { kit, made } = armedKit();
  kit.stream('a.mp3');
  assert.equal(made[0].muted, false);
  for (const set of ['setMuted', 'setQuiet', 'setBlank']) {
    kit[set](true);
    assert.equal(made[0].muted, true, set);
    kit[set](false);
    assert.equal(made[0].muted, false, set);
  }
  kit.setMuted(true);
  kit.setBlank(true);
  kit.setBlank(false);
  assert.equal(made[0].muted, true);
});

test('stop kills it, and a stopped stream never plays again', () => {
  const { kit, made } = armedKit();
  const s = kit.stream('a.mp3');
  s.stop();
  assert.equal(made[0].paused, true);
  s.play();
  kit.hold(true);
  kit.hold(false);
  assert.equal(made[0].paused, true);
  made[0].play();
  assert.equal(made[0].paused, true);
});

test('a stop still fading dies at once on a hold', () => {
  const { kit, made } = armedKit();
  const s = kit.stream('a.mp3');
  s.stop(5000);
  kit.hold(true);
  kit.hold(false);
  assert.equal(made[0].paused, true);
  assert.equal(s.playing, false);
});

/* A graph that records what connects to what. */
function graphKit() {
  const log = { panners: [], sources: 0, chains: [] };
  const node = (name, extra = {}) => ({ name, connect(n) { log.chains.push(`${name}>${n.name}`); return n; }, ...extra });
  globalThis.window = { AudioContext: class {
    constructor() { this.state = 'running'; this.currentTime = 0; this.destination = { name: 'out' }; }
    createGain() { const g = { value: 1, cancelScheduledValues() {}, setTargetAtTime() {}, setValueAtTime() {}, exponentialRampToValueAtTime() {} }; return node('gain', { gain: g }); }
    createStereoPanner() { const p = node('pan', { pan: { value: 0 } }); log.panners.push(p); return p; }
    createBufferSource() { return node('src', { playbackRate: { value: 1 }, start() {} }); }
    createMediaElementSource() { log.sources += 1; return node('media'); }
    decodeAudioData() { return Promise.resolve({}); }
    suspend() { return Promise.resolve(); }
    resume() { return Promise.resolve(); }
  } };
  globalThis.fetch = async () => ({ ok: true, arrayBuffer: async () => new ArrayBuffer(1) });
  const made = [];
  const kit = createKit({ makeMedia: () => { const m = new FakeMedia(); made.push(m); return m; } });
  kit.arm();
  return { kit, made, log };
}

test('pan on play and loop goes through a panner, clamped; no pan adds no node', async () => {
  const { kit, log } = graphKit();
  await kit.play('a.mp3');
  assert.equal(log.panners.length, 0);
  await kit.play('c.mp3', { pan: -0.5 });
  assert.equal(log.panners[0].pan.value, -0.5);
  kit.loop('b.mp3', { pan: 9 });
  await new Promise((r) => setTimeout(r, 5));
  assert.equal(log.panners[1].pan.value, 1);
});

test('all streams join the graph once for source volume; only panned streams add a panner', () => {
  const { kit, made, log } = graphKit();
  kit.stream('a.mp3');
  assert.equal(log.sources, 1);
  assert.equal(made[0].crossOrigin, 'anonymous');
  assert.equal(log.panners.length, 0);
  assert.equal(made[0].volume, 1, 'the graph owns the gain, without a second element gain');
  const s = kit.stream('b.mp3', { pan: 1 });
  assert.equal(log.sources, 2);
  assert.equal(made[1].crossOrigin, 'anonymous');
  assert.equal(log.panners[0].pan.value, 1);
  kit.hold(true);
  assert.equal(made[1].paused, true, 'a panned stream pauses at once too');
  kit.hold(false);
  s.play();
  assert.equal(log.sources, 2, 'an element can join only once');
});

test('the end of a run kills every stream', () => {
  const { kit, made } = armedKit();
  kit.stream('a.mp3');
  kit.stream('b.mp3');
  kit.stopStreams();
  assert.ok(made.every((m) => m.paused));
});
