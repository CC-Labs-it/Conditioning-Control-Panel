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

test('the end of a run kills every stream', () => {
  const { kit, made } = armedKit();
  kit.stream('a.mp3');
  kit.stream('b.mp3');
  kit.stopStreams();
  assert.ok(made.every((m) => m.paused));
});
