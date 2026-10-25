import test from 'node:test';
import assert from 'node:assert/strict';
import { createMusicEdgeState, createMusicEdges } from '../render/musicEdges.js';

const song = { bpm: 120, downbeatS: 0, beats: [] };
const signal = level => ({ active: true, frozen: false, energy: level, bass: level, mid: level, treble: level });
const view = (extra = {}) => ({ enabled: true, songS: 0, beatSong: song, audioReactive: signal(.65), ...extra });
const screen = (kind, extra = {}) => ({ kind, k: 1, strength: 1, params: { seed: 1 }, ...extra });
function run(state, input, seconds = 4, start = 0) {
  for (let i = 0; i < seconds * 60; i++) state.update(1 / 60, { ...input, songS: start + i / 60 });
  return state.frame;
}

test('edges build with actual music energy and remain bounded across a long song', () => {
  const quiet = createMusicEdgeState(), loud = createMusicEdgeState();
  run(quiet, view({ audioReactive: signal(.04) }));
  run(loud, view({ audioReactive: signal(.9) }));
  assert.ok(loud.frame.level > quiet.frame.level * 2, 'louder music visibly strengthens the edge wash');
  let shook = false, particles = false;
  for (let i = 0; i < 2400; i++) {
    loud.update(1 / 60, view({ songS: 4 + i / 60, audioReactive: signal(.9) }));
    const f = loud.frame;
    for (const key of ['level', 'pulse', 'hueMix', 'visibility']) assert.ok(Number.isFinite(f[key]) && f[key] >= 0 && f[key] <= 1, key);
    assert.ok(Number.isFinite(f.shakePx) && Math.abs(f.shakePx) <= 4);
    assert.ok(Number.isInteger(f.particleCount) && f.particleCount >= 0 && f.particleCount <= 48);
    shook ||= Math.abs(f.shakePx) > .01;
    particles ||= f.particleCount > 0;
  }
  assert.ok(shook && particles, 'active music drives both movement and particles');
});

test('silence and mute stop rhythmic movement immediately and the wash fades fully away', () => {
  const state = createMusicEdgeState();
  run(state, view());
  const before = state.frame.level, silent = view({ audioReactive: { ...signal(1), active: false } });
  state.update(1 / 60, silent);
  assert.ok(state.frame.level > 0 && state.frame.level < before, 'the wash has an OUT transition');
  for (const key of ['shakePx', 'pulse', 'particleCount']) assert.equal(state.frame[key], 0, key);
  run(state, silent, 30);
  assert.equal(state.frame.level, 0, 'no permanent residual glow');
  const empty = createMusicEdgeState();
  run(empty, view({ audioReactive: null }), 2);
  assert.equal(empty.frame.level, 0, 'a song grid alone cannot invent music energy');
});

test('beat phase follows the heard song offset rather than elapsed render time', () => {
  const a = createMusicEdgeState(), b = createMusicEdgeState();
  let different = false;
  for (let i = 0; i < 360; i++) {
    a.update(1 / 60, view({ songS: i / 60 }));
    b.update(1 / 60, view({ songS: i / 60, beatSong: { ...song, downbeatS: .25 } }));
    if (i > 180 && Math.abs(a.frame.pulse - b.frame.pulse) > .01) different = true;
  }
  assert.ok(different, 'the same frame clock with another heard beat grid produces another pulse');
});

test('pause, frozen audio and zero delta hold every presentation value; disabled resets', () => {
  const state = createMusicEdgeState();
  run(state, view());
  const before = { ...state.frame };
  state.update(1, view({ paused: true, songS: 200 }));
  assert.deepEqual(state.frame, before);
  state.update(1, view({ audioReactive: { ...signal(1), frozen: true }, songS: 200 }));
  assert.deepEqual(state.frame, before);
  state.update(0, view({ songS: 200 }));
  assert.deepEqual(state.frame, before);
  state.update(.1, view({ enabled: false }));
  for (const key of ['level', 'shakePx', 'pulse', 'particleCount', 'clock']) assert.equal(state.frame[key], 0, key);
});

test('Gentle and reduced motion retain a soft wash without vibration or particles', () => {
  for (const option of ['gentle', 'reducedMotion']) {
    const state = createMusicEdgeState();
    run(state, view());
    state.update(1 / 60, view({ [option]: true }));
    for (const key of ['shakePx', 'pulse', 'particleCount']) assert.equal(state.frame[key], 0, `${option}: ${key}`);
    run(state, view({ [option]: true }), 3);
    assert.ok(state.frame.level > 0, `${option} keeps the colour wash`);
  }
});

test('photosafe asks once per musical bar, and refusal removes movement and particles', () => {
  let requests = 0;
  const denied = createMusicEdgeState({ photosafe: { request() { requests++; return false; } } });
  const allowed = createMusicEdgeState({ photosafe: { request() { return true; } } });
  run(denied, view(), 1.5); run(allowed, view(), 1.5);
  assert.equal(requests, 1, '90 rendered frames in one bar spend one request');
  for (const key of ['shakePx', 'particleCount']) assert.equal(denied.frame[key], 0, key);
  assert.ok(denied.frame.level > 0 && denied.frame.level < allowed.frame.level, 'refusal keeps a softer wash');
  run(denied, view(), 1, 2.1);
  assert.equal(requests, 2, 'the next heard bar gets one new decision');
});

test('recorded rests retain the same-bar grant and quiet modes spend no accent budget', () => {
  const requests = [], state = createMusicEdgeState({ photosafe: { request(kind) { requests.push(kind); return true; } } });
  state.update(.1, view({ songS: .1 }));
  state.update(.1, view({ songS: .2, audioReactive: { ...signal(0), active: false } }));
  state.update(.1, view({ songS: .3 }));
  assert.equal(requests.length, 1, 'a short musical rest cannot charge the bar twice');
  assert.ok(state.frame.pulse > 0, 'the held grant resumes after the rest');
  state.update(.1, view({ songS: 2.1 }));
  assert.equal(requests.length, 2);
  for (const option of ['gentle', 'reducedMotion']) {
    let spent = 0;
    const quiet = createMusicEdgeState({ photosafe: { request() { spent++; return true; } } });
    run(quiet, view({ [option]: true }), 5);
    assert.equal(spent, 0, `${option} leaves the shared budget available to other effects`);
  }
});

test('blackout, outgoing blackout and settled fog obscure edge light', () => {
  const state = createMusicEdgeState();
  run(state, view());
  state.update(.1, view({ screen: screen('blackout') }));
  assert.equal(state.frame.visibility, 0);
  state.update(.1, view({ screen: screen('melt', { prevKind: 'blackout', prevK: .5 }) }));
  assert.equal(state.frame.visibility, .5);
  state.update(.1, view({ screen: null }));
  assert.equal(state.frame.visibility, 1);
  run(state, view({ screen: screen('pink') }), 6);
  assert.ok(state.frame.visibility <= .051);
  state.update(.1, view({ screen: null }));
  assert.equal(state.frame.visibility, 1, 'cleared effects leave no stale obscuration');
});

function dom(width = 3840, height = 2160) {
  const children = [], calls = [];
  const context = new Proxy({}, { get(target, key) {
    if (key in target) return target[key];
    if (key === 'createLinearGradient' || key === 'createRadialGradient') return () => ({ addColorStop() {} });
    return (...args) => calls.push([key, ...args]);
  } });
  const parent = { clientWidth: width, clientHeight: height, appendChild(c) { children.push(c); c.parentNode = this; },
    removeChild(c) { const i = children.indexOf(c); if (i >= 0) children.splice(i, 1); c.parentNode = null; } };
  const doc = { body: parent, defaultView: { innerWidth: width, innerHeight: height }, createElement() {
    return { width: 0, height: 0, style: {}, setAttribute() {}, getContext: () => context, remove() { this.parentNode?.removeChild(this); } };
  } };
  return { parent, doc, children, calls };
}

test('actual course red and magenta colours reserve the stricter photosafe budget', () => {
  for (const [palette, expected] of [[['#d02030', '#ffffff'], 'red'], [['#bd006e', '#fff9ff'], 'red'], [['#21aa62', '#c5ed66'], 'music-edges']]) {
    const fixture = dom(), requests = [];
    const effect = createMusicEdges({ ...fixture, palette: () => palette, photosafe: { request(kind) { requests.push(kind); return true; } } });
    effect.update(.1, view({ songS: .1 }));
    assert.deepEqual(requests, [expected]);
    effect.dispose();
  }
});

test('screen displacement uses viewport pixels, with blackout and quiet modes returning zero', () => {
  for (const [width, height] of [[1920, 1080], [390, 844]]) {
    const fixture = dom(width, height), effect = createMusicEdges({ ...fixture });
    let moved = false;
    for (let i = 0; i < 180; i++) {
      effect.update(1 / 60, view({ songS: i / 60 }));
      assert.ok(Math.abs(effect.shiftU) <= 4 / width);
      assert.ok(Math.abs(effect.shiftU * width - effect.stats.shakePx * effect.stats.visibility) < 1e-12);
      moved ||= Math.abs(effect.shiftU) > .00001;
    }
    assert.ok(moved, 'the edge sampler receives a real displacement');
    const held = effect.shiftU;
    effect.update(.1, view({ paused: true, songS: 100 })); assert.equal(effect.shiftU, held);
    effect.update(.1, view({ songS: 3.07, screen: screen('blackout') })); assert.equal(effect.shiftU, 0);
    for (const option of ['gentle', 'reducedMotion']) {
      effect.update(.1, view({ songS: 3.07, [option]: true })); assert.equal(effect.shiftU, 0);
    }
    effect.update(.1, view({ enabled: false })); assert.equal(effect.shiftU, 0);
    effect.dispose();
  }
});

test('one bounded edge canvas follows the live palette, freezes and disposes between courses', () => {
  for (const [tier, cap] of [['high', 960], ['mid', 768], ['phone', 480]]) {
    const fixture = dom(), { parent, doc, children, calls } = fixture;
    let palette = ['#21aa62', '#c5ed66'];
    const effect = createMusicEdges({ parent, doc, tier, palette: () => palette });
    for (let i = 0; i < 120; i++) effect.update(1 / 60, view({ songS: i / 60 }));
    assert.equal(children.length, 1);
    assert.equal(effect.canvas.style.zIndex, '1');
    assert.equal(effect.canvas.style.pointerEvents, 'none');
    assert.ok(Math.max(effect.canvas.width, effect.canvas.height) <= cap);
    let clip = null, path = null;
    const stack = [], width = effect.canvas.width;
    for (const [kind, ...args] of calls) {
      if (kind === 'save') stack.push(clip);
      if (kind === 'restore') clip = stack.pop();
      if (kind === 'beginPath') path = null;
      if (kind === 'rect') path = args;
      if (kind === 'clip') clip = path;
      if (kind !== 'fill' && kind !== 'fillRect') continue;
      assert.ok(clip, 'every painted ribbon, wash and particle stays inside an edge clip');
      assert.ok(clip[0] + clip[2] <= width * .16 + 1e-6 || clip[0] >= width * .84 - 1e-6,
        'the central 68% of the driving image receives no paint');
    }
    assert.equal(effect.stats.primary, palette[0]); assert.equal(effect.stats.accent, palette[1]);
    const frozen = { ...effect.stats }, count = calls.length;
    palette = ['#e67cb1', '#f5e5ef'];
    effect.update(.5, view({ paused: true }));
    assert.deepEqual(effect.stats, frozen); assert.equal(calls.length, count);
    effect.update(.1, view());
    assert.equal(effect.stats.primary, palette[0]); assert.equal(effect.stats.accent, palette[1]);
    effect.dispose(); effect.dispose(); assert.equal(children.length, 0);
    const fresh = createMusicEdges({ parent, doc, tier });
    assert.equal(fresh.stats.level, 0, 'the next course starts without old music energy');
    fresh.dispose();
  }
});
