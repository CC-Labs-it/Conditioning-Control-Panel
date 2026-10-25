import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const source = readFileSync(new URL('../../ConditioningControlPanel/Resources/web/player/player.js', import.meta.url), 'utf8');
function harness() {
  const messages = [], listeners = new Map();
  let receive, now = 1000, ranges = [];
  const noop = () => {};
  const node = { style: {}, classList: { add: noop, remove: noop, toggle: noop }, getContext: () => null };
  const emit = type => (listeners.get(type) || []).forEach(fn => fn());
  const video = {
    currentTime: 0, duration: NaN, paused: true, videoWidth: 640, videoHeight: 360,
    played: { get length() { return ranges.length; }, start: i => ranges[i][0], end: i => ranges[i][1] },
    addEventListener(type, fn) { listeners.set(type, [...(listeners.get(type) || []), fn]); },
    play() { this.paused = false; emit('playing'); return Promise.resolve(); },
    pause() { this.paused = true; emit('pause'); },
    load() { this.currentTime = 0; ranges = []; },
    removeAttribute: noop,
  };
  const bridge = {
    postMessage: msg => messages.push(JSON.parse(JSON.stringify(msg))),
    addEventListener: (_type, fn) => { receive = fn; },
  };
  vm.runInNewContext(source, {
    window: { chrome: { webview: bridge }, addEventListener: noop }, navigator: {},
    document: { body: node, getElementById: id => id === 'fg' ? video : node },
    performance: { now: () => now }, setInterval: () => 1, clearInterval: noop,
    setTimeout: () => 1, clearTimeout: noop, requestAnimationFrame: () => 1, cancelAnimationFrame: noop,
  }, { filename: 'player.js' });
  return {
    video, emit, send: data => receive({ data }), last: type => messages.findLast(m => m.type === type),
    advance(ms) { now += ms; }, ranges(value) { ranges = value; },
  };
}

test('host seeks after metadata without crediting the skipped intro as played time', () => {
  const h = harness();
  assert.equal(h.last('ready').type, 'ready');
  h.send({ type: 'load', url: 'https://example.invalid/twenty-minutes.mp4' });
  assert.equal(h.video.src, 'https://example.invalid/twenty-minutes.mp4');
  h.video.duration = 1200;
  h.emit('loadedmetadata');
  assert.equal(h.last('meta').durationMs, 1200000);
  h.send({ type: 'seek', ms: 960000 });
  assert.equal(h.video.currentTime, 960);
  assert.deepEqual(h.last('time'), { type: 'time', ms: 960000, playedMs: 0, paused: false });
  h.ranges([[960, 1200]]);
  h.video.currentTime = 1200;
  h.emit('ended');
  assert.equal(h.last('time').playedMs, 240000);
});

test('played time sums media ranges and stays frozen during host pauses', () => {
  const h = harness();
  h.send({ type: 'load', url: 'https://example.invalid/clip.mp4' });
  h.ranges([[600, 604], [700, 706]]);
  h.video.currentTime = 706;
  h.emit('timeupdate');
  assert.equal(h.last('time').playedMs, 10000);
  h.send({ type: 'pause' });
  h.advance(60000);
  h.send({ type: 'pause' });
  assert.deepEqual(h.last('time'), { type: 'time', ms: 706000, playedMs: 10000, paused: true });
  h.send({ type: 'resume' });
  h.ranges([[600, 604], [700, 709]]);
  h.video.currentTime = 709;
  h.advance(3000);
  h.emit('timeupdate');
  assert.deepEqual(h.last('time'), { type: 'time', ms: 709000, playedMs: 13000, paused: false });
  h.send({ type: 'load', url: 'https://example.invalid/next.mp4' });
  h.advance(100);
  h.emit('timeupdate');
  assert.equal(h.last('time').playedMs, 0);
});
