import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

// The player page's half of the floating-video polish (owner, 2026-10-19): it reports the first
// presented frame, and it lays the end card over the clip when the host says `outro`.
const source = readFileSync(new URL('../../ConditioningControlPanel/Resources/web/player/player.js', import.meta.url), 'utf8');
function harness({ frameCallback = true } = {}) {
  const messages = [], listeners = new Map(), timers = [];
  let receive, now = 1000, frameCb = null;
  const noop = () => {};
  const node = { style: {}, hidden: true, classList: { add: noop, remove: noop, toggle: noop, contains: () => false }, getContext: () => null, querySelectorAll: () => [] };
  const emit = type => (listeners.get(type) || []).forEach(fn => fn());
  const video = {
    currentTime: 0, duration: NaN, paused: true, videoWidth: 640, videoHeight: 360,
    played: { length: 0, start: () => 0, end: () => 0 },
    addEventListener(type, fn) { listeners.set(type, [...(listeners.get(type) || []), fn]); },
    play() { this.paused = false; emit('playing'); return Promise.resolve(); },
    pause() { this.paused = true; emit('pause'); },
    load: noop,
    removeAttribute: noop,
  };
  if (frameCallback) video.requestVideoFrameCallback = cb => { frameCb = cb; return 1; };
  const bridge = {
    postMessage: msg => messages.push(JSON.parse(JSON.stringify(msg))),
    addEventListener: (_type, fn) => { receive = fn; },
  };
  vm.runInNewContext(source, {
    window: { chrome: { webview: bridge }, addEventListener: noop }, navigator: {},
    document: { body: node, getElementById: id => id === 'fg' ? video : node },
    performance: { now: () => now }, setInterval: () => 1, clearInterval: noop,
    setTimeout: (fn, ms) => { timers.push({ fn, ms }); return timers.length; }, clearTimeout: noop,
    requestAnimationFrame: () => 1, cancelAnimationFrame: noop,
  }, { filename: 'player.js' });
  return {
    video, emit, timers, node, send: data => receive({ data }),
    count: type => messages.filter(m => m.type === type).length,
    advance(ms) { now += ms; }, frame() { if (frameCb) frameCb(); },
  };
}

test('firstFrame follows the first presented frame, once', () => {
  const h = harness();
  h.send({ type: 'load', url: 'https://example.invalid/a.mp4' });
  assert.equal(h.count('playing'), 1);
  assert.equal(h.count('firstFrame'), 0);
  h.frame();
  h.frame();
  assert.equal(h.count('firstFrame'), 1);
});

test('without a frame callback firstFrame goes out with playing', () => {
  const h = harness({ frameCallback: false });
  h.send({ type: 'load', url: 'https://example.invalid/a.mp4' });
  assert.equal(h.count('firstFrame'), 1);
});

