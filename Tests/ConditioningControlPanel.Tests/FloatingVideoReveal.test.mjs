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

const credit = { sub: 'r/x', site: 'scrolller.com/r/x', clickable: false, outroMs: 2500 };

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

test('an early outro shortens the hold after the end, and ends at once when it has run', () => {
  const h = harness();
  h.send({ type: 'load', url: 'https://example.invalid/a.mp4', credit });
  h.send({ type: 'outro' });
  assert.equal(h.node.hidden, false, 'the end card is up over the playing clip');
  h.advance(2000);
  h.emit('ended');
  assert.equal(h.count('mediaEnded'), 1);
  assert.equal(h.count('ended'), 0);
  const hold = h.timers.at(-1);
  assert.ok(hold.ms > 0 && hold.ms <= 500, `holds only what is left of the card (${hold.ms})`);
  hold.fn();
  assert.equal(h.count('ended'), 1);

  const g = harness();
  g.send({ type: 'load', url: 'https://example.invalid/b.mp4', credit });
  g.send({ type: 'outro' });
  g.advance(3000);
  g.emit('ended');
  assert.equal(g.count('ended'), 1, 'card already over: ended right away');
});

test('no outro from the host keeps the old card after the end', () => {
  const h = harness();
  h.send({ type: 'load', url: 'https://example.invalid/a.mp4', credit });
  h.emit('ended');
  assert.equal(h.count('ended'), 0);
  assert.equal(h.timers.at(-1).ms, 2500);
});

test('outro is ignored for a clip with no credit', () => {
  const h = harness();
  h.send({ type: 'load', url: 'https://example.invalid/a.mp4' });
  h.send({ type: 'outro' });
  h.emit('ended');
  assert.equal(h.count('ended'), 1);
});
