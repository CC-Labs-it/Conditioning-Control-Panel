import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createCueBudget } from '../audio/repetition.js';
import { createKit, BUS } from '../audio/kit.js';

test('pop variants share their cooldown and no clip overlaps itself', () => {
  const b = createCueBudget();
  const ticket = b.request('pop/Pop.mp3', 0);
  assert.equal(b.request('pop/Pop2.mp3', 100), null);
  const release = b.start(ticket, 0);
  assert.equal(typeof release, 'function');
  assert.equal(b.start(b.request('pop/Pop.mp3', 350), 350), null);
  release();
  assert.equal(typeof b.start(b.request('pop/Pop.mp3', 700), 700), 'function');
});

test('all short cues share four slots and stale loads never burst together', () => {
  const b = createCueBudget(), releases = [];
  for (let i = 0; i < 4; i++) releases.push(b.start(b.request(`${i}.mp3`, 0), 0));
  assert.equal(b.start(b.request('extra.mp3', 0), 0), null);
  releases[0](); releases[0]();
  assert.equal(b.live, 3);
  assert.equal(b.start(b.request('late.mp3', 0), 251), null);
  assert.equal(typeof b.start(b.request('fresh.mp3', 300), 300), 'function');
});

test('a hold invalidates pending requests and old completion cannot release a new slot', () => {
  const b = createCueBudget();
  const old = b.request('old.mp3', 0), release = b.start(b.request('one.mp3', 0), 0);
  b.reset();
  assert.equal(b.start(old, 100), null);
  b.start(b.request('one.mp3', 100), 100);
  release();
  assert.equal(b.live, 1);
});

function fixture() {
  let clock = 0;
  const sources = [], gains = [], loads = [];
  const node = (props = {}) => ({ connect(n) { return n; }, disconnect() { this.disconnected = true; }, ...props });
  globalThis.window = { AudioContext: class {
    constructor() { this.state = 'running'; this.currentTime = 0; this.destination = node(); }
    createGain() {
      const events = [];
      const g = node({ events, gain: { value: 1, cancelScheduledValues() {}, setTargetAtTime() {},
        setValueAtTime(value, time) { events.push(['set', value, time]); },
        linearRampToValueAtTime(value, time) { events.push(['ramp', value, time]); } } });
      gains.push(g); return g;
    }
    createBufferSource() { const s = node({ playbackRate: { value: 1 }, started: false, stopped: false, start() { this.started = true; }, stop() { this.stopped = true; this.onended?.(); } }); sources.push(s); return s; }
    decodeAudioData() { return Promise.resolve({ duration: 1.44 }); }
    suspend() { return Promise.resolve(); }
    resume() { return Promise.resolve(); }
  } };
  globalThis.fetch = () => new Promise((resolve) => loads.push(() => resolve({ ok: true, arrayBuffer: async () => new ArrayBuffer(1) })));
  const kit = createKit({ now: () => clock });
  kit.arm();
  return { kit, sources, gains, loads, at(t) { clock = t; } };
}

test('SFX has a quieter default without changing speech or podcast levels', () => {
  assert.deepEqual(BUS, { sfx: .3, ambient: .2, voice: .5, podcast: .5 });
});

test('a pending sample stays discarded after a rapid pause and resume', async () => {
  const f = fixture();
  const pending = f.kit.play('ui_click.mp3');
  f.kit.hold(true); f.kit.hold(false);
  f.loads.shift()();
  await pending;
  assert.equal(f.sources.length, 0);
  f.at(300);
  await f.kit.play('ui_click.mp3');
  assert.equal(f.sources.length, 1);
  f.kit.hold(true); f.kit.hold(false);
  assert.equal(f.sources[0].stopped, true);
  assert.equal(f.sources[0].disconnected, true);
});

test('old downloads and requests made while muted never become later sound', async () => {
  const f = fixture();
  const pending = f.kit.play('ui_click.mp3');
  f.at(300); f.loads.shift()(); await pending;
  assert.equal(f.sources.length, 0);
  f.kit.setMuted(true);
  await f.kit.play('another.mp3');
  assert.equal(f.loads.length, 0);
  f.kit.setMuted(false);
  f.at(500);
  await f.kit.play('ui_click.mp3');
  assert.equal(f.sources.length, 1);
  f.kit.stopStreams();
  assert.equal(f.sources[0].stopped, true);
});

test('completed short cues disconnect and release their shared slot', async () => {
  const f = fixture();
  const pending = f.kit.play('ui_click.mp3', { gain: .18 });
  f.loads.shift()(); await pending;
  assert.equal(f.gains[1].gain.value, .3 * .18 * .75);
  f.sources[0].onended();
  assert.equal(f.sources[0].disconnected, true);
  f.at(200); await f.kit.play('ui_click.mp3');
  assert.equal(f.sources.length, 2);
});

test('a soft voice starts at silence, eases in, fades out and still stops on pause', async () => {
  const f = fixture();
  const pending = f.kit.play('moan.mp3', { bus: 'voice', gain: .4, fadeInMs: 400, fadeOutMs: 180 });
  f.loads.shift()(); await pending;
  assert.equal(f.gains[1].gain.value, 0);
  assert.deepEqual(f.gains[1].events, [['set', 0, 0], ['ramp', .2, .4], ['set', .2, 1.26], ['ramp', 0, 1.44]]);
  assert.equal(f.sources[0].started, true);
  f.kit.hold(true);
  assert.equal(f.sources[0].stopped, true);
  assert.equal(f.sources[0].disconnected, true);
});

test('fades fit within the actual duration even when a short voice plays faster', async () => {
  const f = fixture();
  const pending = f.kit.play('moan.mp3', { bus: 'voice', gain: .4, rate: 2, fadeInMs: 800, fadeOutMs: 800 });
  f.loads.shift()(); await pending;
  assert.deepEqual(f.gains[1].events, [['set', 0, 0], ['ramp', .2, .36], ['set', .2, .36], ['ramp', 0, .72]]);
});
