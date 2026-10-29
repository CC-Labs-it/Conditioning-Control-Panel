/* tests/audio-p5.test.mjs - polish 5 START: go lands on a bar downbeat (S3, the song is offset, go never moves)
 * and the menu / lobby sounds (S4) on the shipped files with a synth fallback for each. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createFeel } from '../core/feel.js';
import { goDownbeat, alignStart, createMusic } from '../audio/music.js';
import { normalizeChart } from '../audio/chart.js';
import { sfxPlan, resolvePlan } from '../audio/sfxmap.js';
import { UI_CUES } from '../audio/cues-b1.js';
import { SFX_BANK } from '../audio/sfx-bank.js';
import { UI_SOUNDS } from '../hud/sounds.js';

const feelJson = JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));
// the pink-road numbers (C:/Projects/glassroad-audio/pink-road/chart.json): first downbeat 1.147 s, 134.84 bpm
const BAR = 240 / 134.84;
const PINK = normalizeChart({ bpm: 134.84, downbeatS: 1.147, sections: [], downbeats: [0, 1, 2, 3, 4].map((k) => 1.147 + k * BAR) });
const goS = feelJson.juice.countdownPulses * 60 / feelJson.clock.bpm;

test('goDownbeat: the last bar line at or before the natural go, else the first', () => {
  assert.ok(Math.abs(goDownbeat(PINK, goS) - 1.147) < 1e-9, 'solo: 1.875 s of count lands on the first bar');
  assert.ok(Math.abs(goDownbeat(PINK, 3) - (1.147 + BAR)) < 1e-9, 'online: 3 s from countAt lands on bar 2');
  assert.ok(Math.abs(goDownbeat(PINK, 0.5) - 1.147) < 1e-9, 'a count shorter than the pickup starts the song part way in');
  assert.ok(Math.abs(goDownbeat(PINK, 1.147) - 1.147) < 1e-9);
  // no downbeats list: every four beats from downbeatS
  const plain = normalizeChart({ bpm: 120, downbeatS: 0.5, sections: [] });
  assert.ok(Math.abs(goDownbeat(plain, 3) - 2.5) < 1e-9);
  assert.ok(Math.abs(goDownbeat(plain, 0.2) - 0.5) < 1e-9);
  assert.ok(Math.abs(goDownbeat(null, 1) - 0) < 1e-9);
  // the wait under the song is always under a bar, and never negative
  for (const g of [0.1, 0.9, 1.875, 2.5, 3, 4.2]) {
    const d = goDownbeat(PINK, g);
    assert.ok(g - d < BAR + 1e-9);
  }
});

test('alignStart: a future start waits, a past one starts part way in, both hear posNow now', () => {
  const lat = 0.03;
  const wait = alignStart(10, lat, -0.7);
  assert.ok(Math.abs(wait.startT - 10.67) < 1e-9);
  assert.equal(wait.when, wait.startT); assert.equal(wait.offset, 0);
  const late = alignStart(10, lat, 2.5, 230);
  assert.equal(late.when, 10);
  assert.ok(Math.abs(late.offset - 2.53) < 1e-9);
  // the heard position at now (songTime: now - startT - latency) is posNow
  assert.ok(Math.abs((10 - late.startT - lat) - 2.5) < 1e-9);
  assert.ok(Math.abs((10.67 + lat + 0.7 - wait.startT - lat) - 0.7) < 1e-9);
  const wrap = alignStart(10, 0, 250, 100);
  assert.ok(Math.abs(wrap.offset - 50) < 1e-9, 'a loop wraps the offset');
});

function fakeCtx() {
  const starts = [];
  const param = (v = 0) => {
    const p = { value: v };
    for (const k of ['setValueAtTime', 'linearRampToValueAtTime', 'setTargetAtTime', 'cancelScheduledValues']) p[k] = () => p;
    return p;
  };
  const node = (extra = {}) => ({ connect() {}, disconnect() {}, stop() {}, ...extra });
  const ctx = {
    state: 'running', currentTime: 5, outputLatency: 0.02,
    createGain: () => node({ gain: param(1) }),
    createBufferSource: () => node({ buffer: null, loop: false, start(when = 0, offset = 0) { starts.push([when, offset]); } }),
    decodeAudioData: () => Promise.resolve({ duration: 230 }),
  };
  return { ctx, starts };
}

test('silent chart preparation creates no voice; playback hears zero at GO and reuses the decode', async () => {
  const { ctx, starts } = fakeCtx(); let decodes = 0;
  ctx.decodeAudioData = async () => { decodes++; return { duration: 265 }; };
  const chart = { bpm: 96, sections: [], audio: 'mix.mp3' };
  const fetchFn = async () => ({ ok: true, json: async () => chart, arrayBuffer: async () => new ArrayBuffer(8) });
  const music = createMusic(ctx, ctx.createGain(), createFeel(feelJson), { fetchFn });
  assert.equal(await music.playChart('https://x.test/chart.json', { prepareOnly: true }), true);
  assert.equal(music.playing, false); assert.equal(starts.length, 0);
  ctx.currentTime = 30;
  await music.playChart('https://x.test/chart.json', { align: () => -2 });
  assert.equal(decodes, 1); assert.deepEqual(starts, [[31.98, 0]]);
  ctx.currentTime = 31; assert.equal(music.songS, 0);
  ctx.currentTime = 32; assert.ok(Math.abs(music.songS) < 1e-9);
  ctx.currentTime = 33; assert.ok(Math.abs(music.songS - 1) < 1e-9);
  music.dispose();
});

test('personally selected recording also prepares silently and begins at GO', async () => {
  const { ctx, starts } = fakeCtx();
  const music = createMusic(ctx, ctx.createGain(), createFeel(feelJson));
  const file = { arrayBuffer: async () => new ArrayBuffer(8) };
  await music.playFile(file, { prepareOnly: true });
  assert.equal(starts.length, 0); assert.equal(music.playing, false);
  await music.playFile(file, { align: () => -2 });
  assert.deepEqual(starts, [[6.98, 0]]);
  music.dispose();
});

test('music: playChart with align schedules go on the downbeat; without align it starts at once (no change)', async () => {
  const feel = createFeel(feelJson);
  const chart = { bpm: 134.84, downbeatS: 1.147, sections: [{ id: 'a', startS: 0 }], downbeats: [1.147, 1.147 + BAR] };
  const fetchFn = async (url) => ({ ok: true, json: async () => chart, arrayBuffer: async () => new ArrayBuffer(8) });
  const { ctx, starts } = fakeCtx();
  const music = createMusic(ctx, ctx.createGain(), feel, { fetchFn });
  // solo at race.start(): goInS = the whole count, so the song waits goS - 1.147 s
  const goIn = goS;
  assert.equal(await music.playChart('https://x.test/c.json', { align: (song) => goDownbeat(song, goS) - goIn }), true);
  const [when, offset] = starts.at(-1);
  assert.ok(Math.abs(when - (5 - 0.02 + (goS - 1.147))) < 1e-9, 'starts ' + when);
  assert.equal(offset, 0);
  assert.equal(music.songS, 0, 'nothing heard yet');
  // at go (goIn seconds later) the player hears the downbeat
  ctx.currentTime = 5 + goIn;
  assert.ok(Math.abs(music.songS - 1.147) < 1e-9, 'heard ' + music.songS);
  // a late decode: go was 1 s ago, the song starts 1 s past the downbeat at once
  ctx.currentTime = 20;
  await music.playChart('https://x.test/c.json', { align: (song) => goDownbeat(song, goS) + 1 });
  const [w2, o2] = starts.at(-1);
  assert.equal(w2, 20);
  assert.ok(Math.abs(o2 - (1.147 + 1 + 0.02)) < 1e-9);
  assert.ok(Math.abs(music.songS - 2.147) < 1e-9);
  // no align: the old start at once from 0
  ctx.currentTime = 30;
  await music.playChart('https://x.test/c.json');
  assert.deepEqual(starts.at(-1), [30, 0]);
  // an align that throws or answers nothing falls back to the plain start
  ctx.currentTime = 40;
  await music.playChart('https://x.test/c.json', { align: () => { throw new Error('x'); } });
  assert.deepEqual(starts.at(-1), [40, 0]);
  music.dispose();
});

test('S4: every hud sound has a synth figure and a recorded plan on shipped files (deny stays synth)', () => {
  for (const name of UI_SOUNDS) {
    assert.ok(UI_CUES[name], 'synth ' + name);
    const plan = sfxPlan(name, {}, {});
    if (name === 'menu-deny') { assert.equal(plan, null); continue; }
    assert.ok(Array.isArray(plan) && plan.length, 'plan ' + name);
    for (const x of plan) assert.ok(SFX_BANK[x.id], name + ' -> ' + x.id);
    assert.equal(resolvePlan(plan, (id) => !!SFX_BANK[id]), 'sample');
    assert.equal(resolvePlan(plan, () => false), 'synth');
  }
  // the lobby bell is the race bell, lower and quieter
  const tick = sfxPlan('lobby-tick')[0];
  assert.equal(tick.id, 'countdown'); assert.ok(tick.rate < 1 && tick.vol < 1);
});
