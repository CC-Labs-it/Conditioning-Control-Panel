/* tests/phone-audio-touch.test.mjs - phones decode the race song at half the live rate (audio/music.js lowRateDecode),
 * fall back to the full-rate decode, keep every song clock; the touch throw button sits bottom right (input/touch.js). */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createFeel } from '../core/feel.js';
import { createMusic, halfRateContext, analyzeAudioBuffer } from '../audio/music.js';
import { createTouch, THROW_CSS, DROP_CSS } from '../input/touch.js';

const feelJson = JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));
const chart = { bpm: 120, downbeatS: 0.5, sections: [{ id: 'a', startS: 0 }], audio: 'song.mp3' };
const fetchFn = async () => ({ ok: true, json: async () => chart, arrayBuffer: async () => new ArrayBuffer(8) });

function fakeCtx(buffer) {
  const starts = [], rates = [];
  const param = (v = 0) => {
    const p = { value: v };
    for (const k of ['setValueAtTime', 'linearRampToValueAtTime', 'setTargetAtTime', 'cancelScheduledValues']) p[k] = (x) => { if (k === 'setValueAtTime') rates.push(x); return p; };
    return p;
  };
  const node = (extra = {}) => ({ connect() {}, disconnect() {}, stop() {}, ...extra });
  const ctx = {
    state: 'running', currentTime: 5, outputLatency: 0.02, sampleRate: 48000, decodes: 0,
    createGain: () => node({ gain: param(1) }),
    createBufferSource: () => node({ buffer: null, loop: false, playbackRate: param(1), start(when = 0, offset = 0) { starts.push([when, offset]); } }),
    decodeAudioData(bytes, ok) { ctx.decodes++; ok?.(buffer); return Promise.resolve(buffer); },
  };
  return { ctx, starts };
}
const FULL = { duration: 230, sampleRate: 48000, length: 230 * 48000 };
const HALF = { duration: 230, sampleRate: 24000, length: 230 * 24000 };
/** An OfflineAudioContext stand-in: records its construction, decodes to `buffer` (or rejects). */
function fakeOffline(buffer, { fail = false } = {}) {
  const made = [];
  class Offline {
    constructor(ch, len, rate) { made.push([ch, len, rate]); this.sampleRate = rate; }
    decodeAudioData() { return fail ? Promise.reject(new Error('refused')) : Promise.resolve(buffer); }
  }
  return { Offline, made };
}
const play = async (opts, ctx) => {
  const music = createMusic(ctx, ctx.createGain(), createFeel(feelJson), { fetchFn, ...opts });
  assert.equal(await music.playChart('https://x.test/c.json'), true);
  return music;
};

test('phones decode through an OfflineAudioContext at half the live rate; desktop never builds one', async () => {
  const { Offline, made } = fakeOffline(HALF);
  const phone = fakeCtx(FULL);
  const m = await play({ lowRateDecode: () => true, Offline }, phone.ctx);
  assert.deepEqual(made, [[2, 1, 24000]]);
  assert.equal(phone.ctx.decodes, 0, 'the live context never decodes the song');
  m.dispose();
  for (const lowRateDecode of [() => false, null, () => { throw new Error('x'); }]) {
    const desk = fakeCtx(FULL), off = fakeOffline(HALF);
    const d = await play({ lowRateDecode, Offline: off.Offline }, desk.ctx);
    assert.equal(off.made.length, 0); assert.equal(desk.ctx.decodes, 1);
    d.dispose();
  }
  assert.equal(halfRateContext({ sampleRate: 44100 }, Offline).sampleRate, 22050);
  assert.equal(halfRateContext({ sampleRate: 0 }, Offline), null);
});

test('no OfflineAudioContext, a refused rate or a failed half-rate decode: the full-rate decode', async () => {
  const thrower = class { constructor() { throw new Error('rate'); } };
  for (const Offline of [null, thrower, fakeOffline(HALF, { fail: true }).Offline]) {
    const { ctx } = fakeCtx(FULL);
    const m = await play({ lowRateDecode: () => true, Offline }, ctx);
    assert.equal(ctx.decodes, 1); assert.equal(m.playing, true);
    m.dispose();
  }
});

test('a half-rate buffer keeps songS, beat, the loop, alignStart and setRate exactly', async () => {
  const runs = [];
  for (const [buf, low] of [[FULL, false], [HALF, true]]) {
    const { ctx, starts } = fakeCtx(buf);
    const music = createMusic(ctx, ctx.createGain(), createFeel(feelJson), { fetchFn, lowRateDecode: () => low, Offline: fakeOffline(HALF).Offline });
    await music.playChart('https://x.test/c.json', { align: () => 3 });
    const seen = [starts.at(-1)];
    for (const t of [6, 40, 5 + 231]) { ctx.currentTime = t; seen.push(music.songS, music.beat.index, music.beat.phase); }
    music.setRate(1.06); ctx.currentTime = 300; seen.push(music.songS);
    runs.push(seen); music.dispose();
  }
  assert.deepEqual(runs[1], runs[0]);
  assert.ok(runs[0][7] < 230, 'the loop wraps on buffer.duration');
});

test('the picked-file analysis reads the buffer rate: a 120 bpm click at 24 kHz matches 48 kHz', () => {
  const click = (sr) => {
    const d = new Float32Array(sr * 12);
    for (let b = 0; b < 24; b++) for (let i = 0; i < sr * 0.02; i++) d[Math.floor(b * 0.5 * sr) + i] = Math.sin(i * 2 * Math.PI * 80 / sr) * (1 - i / (sr * 0.02));
    return { numberOfChannels: 1, sampleRate: sr, getChannelData: () => d };
  };
  const full = analyzeAudioBuffer(click(48000)), half = analyzeAudioBuffer(click(24000));
  assert.ok(full && half);
  assert.ok(Math.abs(full.bpm - half.bpm) < 0.5, full.bpm + ' vs ' + half.bpm);
  assert.ok(Math.abs(full.durationS - half.durationS) < 1e-6);
});

class Node {
  constructor() { this.listeners = new Map(); this.children = []; this.style = {}; this.attrs = {}; this.classes = new Set(); this.classList = { add: c => this.classes.add(c), remove: c => this.classes.delete(c) }; }
  addEventListener(name, fn) { const list = this.listeners.get(name) || new Set(); list.add(fn); this.listeners.set(name, list); }
  removeEventListener(name, fn) { this.listeners.get(name)?.delete(fn); }
  emit(name, props = {}) { const e = { target: this, cancelable: true, preventDefault() {}, stopPropagation() {}, ...props }; for (const fn of this.listeners.get(name) || []) fn(e); }
  appendChild(n) { n.parentNode = this; this.children.push(n); }
  removeChild(n) { this.children = this.children.filter(c => c !== n); n.parentNode = null; }
  setAttribute(k, v) { this.attrs[k] = v; }
  closest() { return this.className?.includes('gr-touch-btn') ? this : null; }
  getBoundingClientRect() { return { left: 0, top: 0, width: 800, height: 400 }; }
  setPointerCapture() {}
  releasePointerCapture() {}
}

test('touch: the throw button sits bottom right in the safe area, fires one item edge, never hops or steers', () => {
  const root = new Node(), win = new Node(), doc = { createElement: () => new Node() };
  win.location = { search: '?touch=1' }; win.document = doc;
  const touch = createTouch({ root, win, doc }), into = {};
  const btn = touch.el.children.find(n => n.className?.includes('gr-touch-item'));
  assert.ok(btn, 'a throw button');
  assert.equal(btn.style.cssText, THROW_CSS);
  assert.match(THROW_CSS, /right:max\(16px,env\(safe-area-inset-right\)\)/);
  assert.match(THROW_CSS, /bottom:max\(14px,env\(safe-area-inset-bottom\)\)/);
  assert.doesNotMatch(THROW_CSS, /\b(top|left):/, 'anchored to the bottom right only');
  for (const n of touch.el.children.filter(c => /gr-touch-(view|rear)/.test(c.className || ''))) assert.match(n.style.cssText, /left:calc\(50% \+ -?\d+px\)/, 'View / Rear stay bottom centre');
  assert.ok(root.classes.has('gr-touch-on'));
  btn.emit('pointerdown', { pointerId: 3, clientX: 760 }); touch.poll(into);
  assert.equal(into.item, true); assert.equal(into.hop, false); assert.equal(into.steer, 0);
  touch.poll(into); assert.equal(into.item, false, 'an edge, once');
  btn.emit('click', { detail: 0 }); touch.poll(into); assert.equal(into.item, true, 'keyboard activation');
  btn.emit('pointerdown', { pointerId: 3 }); win.emit('blur'); touch.poll(into); assert.equal(into.item, false, 'blur drops a pending throw');
  touch.dispose();
  assert.equal(root.classes.has('gr-touch-on'), false);
  const css = readFileSync(new URL('../styles.css', import.meta.url), 'utf8');
  assert.match(css, /\.gr-touch-on \.hud-speed \{ bottom: calc\(max\(14px, env\(safe-area-inset-bottom\)\) \+ 136px\); \}/, 'speed lifts over both buttons');
  assert.match(css, /\.gr-touch-on \.hud-credit \{ bottom: calc\(/, 'the media credit lifts too');
});

test('touch: Drop leaves an item behind once, keeps driving, and clears on blur', () => {
  const root = new Node(), win = new Node(), doc = { createElement: () => new Node() };
  win.location = { search: '?touch=1' }; win.document = doc;
  const touch = createTouch({ root, win, doc }), into = {};
  try {
    const drop = touch.el.children.find(n => n.className?.includes('gr-touch-drop'));
    const forward = touch.el.children.find(n => n.className?.includes('gr-touch-item'));
    assert.equal(drop.style.cssText, DROP_CSS);
    assert.equal(drop.attrs['aria-label'], 'Drop item behind');
    touch.el.emit('pointerdown', { pointerId: 1, pointerType: 'touch', clientX: 100, clientY: 100 });
    drop.emit('pointerdown', { pointerId: 2 }); touch.poll(into);
    assert.equal(into.item, true); assert.equal(into.back, true);
    assert.equal(into.throttle, 1); assert.equal(into.hop, false); assert.equal(into.steer, 0);
    touch.poll(into); assert.equal(into.item, false); assert.equal(into.back, false);
    forward.emit('pointerdown', { pointerId: 3 }); touch.poll(into);
    assert.equal(into.item, true); assert.equal(into.back, false, 'forward stays forward after a drop');
    drop.emit('click', { detail: 0 }); touch.poll(into);
    assert.equal(into.item, true); assert.equal(into.back, true, 'accessible activation also drops');
    drop.emit('pointerdown', { pointerId: 4 }); win.emit('blur'); touch.poll(into);
    assert.equal(into.item, false); assert.equal(into.back, false);
    touch.dispose();
    drop.emit('pointerdown', { pointerId: 5 }); touch.poll(into);
    assert.equal(into.item, false, 'disposed button is disconnected');
  } finally { touch.dispose(); }
});
