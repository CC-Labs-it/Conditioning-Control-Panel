// Owner 2026-10-27: traps telegraph their close and open, with more headroom; a flash burst never repeats a picture.
import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { normalizeChart } from '../audio/chart.js';
import { trapArrangement, trapStroke, WARN_BEATS, SPACING_BEATS } from '../track/trapArrangement.js';
import { setMachineryBeat, machineryBeatAt } from '../track/machineryBeat.js';
import { telegraphKey, createTrapTicks, TELEGRAPH, trapTickGain } from '../render/trapTelegraph.js';
import { sfxPlan } from '../audio/sfxmap.js';
import { AUDIO_CUES } from '../audio/cues.js';
import { FX_CUES } from '../fx/cues.js';
import { createScrolllerFeed } from '../host/scrolllerFeed.js';
registerHooks({ resolve(s, c, next) {
  const files = { 'three/tsl': 'three.tsl.min.js', 'three/webgpu': 'three.webgpu.min.js' };
  return files[s] ? { url: new URL('../vendor/three/build/' + files[s], import.meta.url).href, shortCircuit: true } : next(s, c);
} });
const { createFlashBurst } = await import('../fx/flashBurst.js');

const chart = more => normalizeChart({ id: 'tele-song', bpm: 96, durationS: 80, ...more });
function setup(song = chart(), id = 'threshold') {
  const track = { id };
  setMachineryBeat(track, { enabled: true, song, bpm: song.bpm, songS: 0, raceS: 0 });
  return { track, frame: b => machineryBeatAt(track, b * 60 / song.bpm) };
}
const BEAT = .625;
const row = { id: 'p1', kind: 'piston', offsetS: 0, offsetBeats: 0, x: 10, side: 1, travelM: 10, w: 5, len: 4, roadWidth: 24,
  p: [0, 0, 0], b: [1, 0, 0], n: [0, 1, 0], t: [0, 0, -1] };

test('four warning beats carry a countdown, the rise is flagged as opening, closes keep eight beats apart', () => {
  const { frame } = setup(), pose = b => trapStroke(row, frame(b));
  assert.equal(WARN_BEATS, 4); assert.equal(SPACING_BEATS, 8);
  assert.equal(pose(0).countdown, 4); assert.equal(pose(0).extension, 0); assert.equal(pose(0).warning, true);
  assert.ok(Math.abs(pose(1.5).countdown - 2.5) < 1e-9);
  assert.equal(pose(4.5).countdown, null); assert.equal(pose(4.5).opening, false); assert.equal(pose(4.5).extension, 1);
  assert.equal(pose(6.5).opening, true); assert.equal(pose(7.5).opening, false); assert.equal(pose(7.5).phase, 'clear');
  assert.equal(pose(7.5).countdown, null); assert.equal(pose(8.1).countdown > 3.8, true);
  for (const name of ['threshold', 'keep', 'mainframe', 'boudoir', 'goonworks']) {
    const closes = trapArrangement(chart(), name).closes;
    assert.ok(closes.length >= 4, name);
    for (let i = 1; i < closes.length; i++) assert.ok(closes[i] - closes[i - 1] >= SPACING_BEATS, name + ' ' + closes.join(','));
  }
  const peak = trapArrangement(chart({ sections: [{ startS: 0, endS: 80, mood: 'peak' }] }), 'goonworks').closes;
  for (let i = 1; i < peak.length; i++) assert.ok(peak[i] - peak[i - 1] >= SPACING_BEATS, 'peak ' + peak.join(','));
  // the rise is never cut short by the next warning
  for (let b = 0; b < 64; b += .01) assert.ok(Math.abs(pose(b + 1e-6).extension - pose(b).extension) < .0001, 'continuous at ' + b);
});

test('telegraph ladder: calm, amber once per warning beat (steady under Gentle), solid on the last beat, hot closed, green opening', () => {
  const { frame } = setup(), key = (b, gentle) => telegraphKey(trapStroke(row, frame(b)), gentle);
  assert.equal(key(7.5), 'calm');
  assert.equal(key(8.1), 'warn'); assert.equal(key(8.7), 'dim'); assert.equal(key(9.1), 'warn'); assert.equal(key(10.6), 'dim');
  assert.equal(key(8.7, true), 'warn');
  assert.equal(key(10.9), 'dim'); assert.equal(key(11.2), 'hot', 'the closing travel is the last beat');
  assert.equal(key(11.7), 'hot'); assert.equal(key(12.5), 'hot');
  assert.equal(key(14.5), 'open');
  assert.equal(key(15.5), 'calm');
  assert.equal(telegraphKey(null), 'calm');
  assert.equal(telegraphKey({ safe: true, warning: true, extension: 0 }), 'calm');
  assert.equal(telegraphKey({ safe: false, warning: true, extension: 0, active: true }), 'warn', 'the online fallback pose: a steady warning');
  assert.equal(telegraphKey({ safe: false, warning: false, extension: .5, active: true }), 'hot');
  assert.ok(TELEGRAPH.warn && TELEGRAPH.hot && TELEGRAPH.open && TELEGRAPH.calm === null);
});

test('the nearest trap ahead ticks once per warning beat; far, seeking, paused and menu frames stay quiet', () => {
  const { track } = setup(); track.moving = { traps: [row] };
  const heard = [], ticks = createTrapTicks(track, (...a) => heard.push(a));
  for (let t = 7 * BEAT; t <= 12.2 * BEAT; t += 1 / 60) ticks.update(t, [0, 0, 0]);
  assert.deepEqual(heard.map(h => h[0]), ['trap-tick', 'trap-tick', 'trap-tick', 'trap-tick']);
  assert.deepEqual(heard.map(h => h[1].step), [4, 3, 2, 1]);
  assert.ok(heard[0][1].gain > .5 && heard[0][1].gain < 1); assert.equal(heard[0][1].id, 'p1');
  heard.length = 0;
  for (let t = 23 * BEAT; t <= 28.2 * BEAT; t += 1 / 60) ticks.update(t, [70, 0, 0]);
  assert.equal(heard.length, 0, 'out of range');
  ticks.reset();
  ticks.update(24.5 * BEAT, [0, 0, 0]); ticks.update(26.5 * BEAT, [0, 0, 0]);
  assert.equal(heard.length, 0, 'a seek lands inside the warning without replaying ticks');
  ticks.update(26.6 * BEAT, [0, 0, 0], { paused: true }); ticks.update(26.6 * BEAT, [0, 0, 0], { active: false });
  assert.equal(heard.length, 0);
  for (let t = 39 * BEAT; t <= 41.2 * BEAT; t += 1 / 60) ticks.update(t, [0, 0, 0], { gentle: true });
  assert.ok(heard.length >= 2); assert.equal(heard[0][1].gentle, true);
});

test('trap ticks are a quiet rising ladder on the sample and synth paths, with their fx registry entry', () => {
  assert.deepEqual(sfxPlan('trap-tick', { gain: 0 }), []);
  const p4 = sfxPlan('trap-tick', { gain: 1, step: 4 })[0], p1 = sfxPlan('trap-tick', { gain: 1, step: 1 })[0];
  assert.equal(p4.id, 'countdown'); assert.ok(p4.vol <= .05); assert.ok(p1.rate > p4.rate);
  assert.ok(sfxPlan('trap-tick', { gain: 1, gentle: true })[0].vol < p4.vol);
  assert.equal(trapTickGain({ gain: 3 }), 1); assert.equal(trapTickGain({ gain: 1, gentle: true }), .6);
  const tones = [], syn = { tone: (f, o) => tones.push([f, o]), semis: n => n };
  AUDIO_CUES['trap-tick'](syn, { gain: 1, step: 4 }); AUDIO_CUES['trap-tick'](syn, { gain: 1, step: 1 }); AUDIO_CUES['trap-tick'](syn, { gain: 0, step: 1 });
  assert.equal(tones.length, 2); assert.ok(tones[1][0] > tones[0][0]); assert.ok(tones[0][1].vol <= .06);
  assert.equal(typeof FX_CUES['trap-tick'], 'function');
});

test('the piston view recolours a trap through the ladder and reports it', async () => {
  const { createMovingHazardView } = await import('../render/movingHazards.js');
  const { Scene } = await import('three/webgpu');
  const { track } = setup(); track.moving = { traps: [row] }; track.forks = [];
  const view = createMovingHazardView(new Scene(), track, null);
  assert.deepEqual(view.inspect().telegraph, ['warn'], 'the first close at beat 4 is already in warning at 0');
  view.update(7.5 * BEAT); assert.deepEqual(view.inspect().telegraph, ['calm']);
  view.update(8.1 * BEAT); assert.deepEqual(view.inspect().telegraph, ['warn']);
  view.update(8.7 * BEAT); assert.deepEqual(view.inspect().telegraph, ['dim']);
  view.update(8.7 * BEAT, { gentle: true }); assert.deepEqual(view.inspect().telegraph, ['warn']);
  view.update(12.5 * BEAT); assert.deepEqual(view.inspect().telegraph, ['hot']);
  view.update(14.5 * BEAT); assert.deepEqual(view.inspect().telegraph, ['open']);
  view.update(15.5 * BEAT); assert.deepEqual(view.inspect().telegraph, ['calm']);
  const accents = view.group.children.filter(o => o.isInstancedMesh && o.instanceColor);
  assert.ok(accents.length >= 1, 'the accent batch carries per-instance colours');
  view.dispose();
});

test('a flash burst never repeats a picture: as many cards as distinct stills, seven when the pool is unknown', () => {
  const make = () => ({ style: {}, setAttribute() {}, remove() {}, getContext: () => ({ drawImage() {}, fillRect() {}, fillText() {} }) });
  const parent = { appendChild() {} };
  const burst = (pool) => {
    const fx = createFlashBurst({ make, parent, acquireLoom: () => null, pool });
    fx.fire({ id: 'hit-1', params: {} });
    for (let i = 0; i < 400; i++) fx.update(1 / 120);
    return fx.stats.total;
  };
  assert.equal(burst(null), 7);
  assert.equal(burst(() => 0), 7, 'no stills known: the Loom fallback fills the cards');
  assert.equal(burst(() => 3), 3);
  assert.equal(burst(() => 1), 1);
  assert.equal(burst(() => 12), 7);
});

test('the web feed counts its loaded stills for the burst', () => {
  const handlers = {}, bridge = { on: (n, fn) => { handlers[n] = fn; return () => {}; }, send() {} }, imgs = [], vids = [];
  const feed = createScrolllerFeed({ bridge,
    makeImage: () => { const i = { complete: false, naturalWidth: 0, src: '', removeAttribute(k) { this[k] = ''; } }; imgs.push(i); return i; },
    makeVideo: () => { const v = { tagName: 'VIDEO', readyState: 0, error: null, src: '', poster: '', removeAttribute(k) { this[k] = ''; }, load() {} }; vids.push(v); return v; } });
  handlers['scrolller-feed']({ v: 1, rows: [{ url: 'https://images.scrolller.com/a.jpg', kind: 'image' },
    { url: 'https://images.scrolller.com/b.jpg', kind: 'image' }, { url: 'https://iris.scrolller.com/c.mp4', kind: 'video' }] });
  assert.equal(feed.stillCount, 0);
  imgs[0].complete = true; imgs[0].naturalWidth = 640; vids[0].readyState = 1;
  assert.equal(feed.stillCount, 1); assert.equal(feed.ready, 2, 'ready counts clips too, stillCount never does');
  imgs[1].complete = true; imgs[1].naturalWidth = 640;
  assert.equal(feed.stillCount, 2);
  feed.dispose();
});
