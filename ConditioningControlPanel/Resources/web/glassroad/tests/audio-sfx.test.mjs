import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, statSync } from 'node:fs';
import { createFeel } from '../core/feel.js';
import { createBus } from '../core/events.js';
import { createAudio } from '../audio/bus.js';
import { SFX_BANK } from '../audio/sfx-bank.js';
import { MIX } from '../audio/mix.js';
import {
  SFX_GAIN, engineLayers, enginePitchSemis, engineCutHz, semisToRate, sfxPlan, resolvePlan, jitterRate,
  popRungNext, BOT_SAMPLE_ECHO,
} from '../audio/sfxmap.js';
import { BOT_ECHO } from '../audio/cues-b1.js';

const feelJson = JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));
const feel = createFeel(feelJson);
const near = (a, b, eps = 1e-6) => Math.abs(a - b) < eps;

test('engine layers: idle at rest, cruise in the middle, air at the top, a boost leans on air', () => {
  const rest = engineLayers(0, {}, feel);
  assert.ok(near(rest.idle, 1) && near(rest.cruise, 0) && near(rest.air, 0));
  const mid = engineLayers(0.5, {}, feel);
  assert.ok(near(mid.idle, 0) && near(mid.cruise, 1) && mid.air > 0 && mid.air < 0.2);
  const top = engineLayers(1, {}, feel);
  assert.ok(near(top.air, 1) && near(top.cruise, 1 - MIX.engCruiseDip));
  assert.ok(engineLayers(0.5, { boosting: true }, feel).air > mid.air);
  assert.ok(engineLayers(1.3, {}, feel).air <= 1, 'over-speed clamps');
  for (let p = 0; p <= 1.3001; p += 0.05) {
    const w = engineLayers(p, { boosting: p > 0.8 }, feel);
    for (const k of ['idle', 'cruise', 'air']) assert.ok(w[k] >= 0 && w[k] <= 1, k + ' at ' + p);
    assert.ok(w.idle + w.cruise + w.air > 0.6, 'never a hole in the crossfade at ' + p.toFixed(2));
  }
});

test('engine pitch: rises with speed, never travels more than the cap, never a whine', () => {
  let lo = Infinity, hi = -Infinity, prev = -Infinity;
  for (let p = 0; p <= 1.3001; p += 0.02) {
    const s = enginePitchSemis(p, {}, feel);
    assert.ok(s >= prev - 1e-9, 'monotonic'); prev = s;
    for (const fl of [{}, { boosting: true }, { drifting: true, driftTier: 3 }, { airborne: true, boosting: true, drifting: true, driftTier: 3 }]) {
      const x = enginePitchSemis(p, fl, feel); lo = Math.min(lo, x); hi = Math.max(hi, x);
    }
  }
  assert.ok(near(enginePitchSemis(0, {}, feel), MIX.engSemisLow));
  assert.ok(near(enginePitchSemis(1, {}, feel), MIX.engSemisHigh));
  assert.ok(hi - lo <= MIX.engPitchTravelSemis + 1e-9 && hi - lo <= 4 + 1e-9, 'travel ' + (hi - lo));
  assert.ok(near(semisToRate(12), 2) && near(semisToRate(0), 1));
  assert.ok(semisToRate(hi) < 1.3, 'top rate stays under a major third and a bit');
  assert.ok(engineCutHz(1, {}, feel) > engineCutHz(0.3, {}, feel) && engineCutHz(1, { boosting: true }, feel) <= 20000);
});

test('fallback selection: a plan plays from samples only when every file loaded', () => {
  const all = () => true, none = () => false, noBump = (id) => id !== 'bump';
  assert.equal(resolvePlan(null, all), 'synth', 'no plan: the synth');
  assert.equal(resolvePlan([], none), 'sample', 'silent on purpose stays silent');
  assert.equal(resolvePlan([{ id: 'hop' }], all), 'sample');
  assert.equal(resolvePlan([{ id: 'hop' }], none), 'synth', 'a failed file falls back');
  assert.equal(resolvePlan([{ id: 'item-box' }, { id: 'bump' }], noBump), 'synth', 'one missing file takes the whole cue to the synth');
});

test('sample plans: laps, drift tiers, landings, bumps, items, bot echoes', () => {
  const ids = (pl) => (pl || []).map((x) => x.id);
  assert.deepEqual(ids(sfxPlan('lap', { lap: 1 }, { laps: 3 })), ['lap']);
  assert.deepEqual(ids(sfxPlan('lap', { lap: 2 }, { laps: 3 })), ['final-lap']);
  assert.deepEqual(sfxPlan('lap', { lap: 3 }, { laps: 3 }), [], 'the finish plays on the last line');
  for (const t of [1, 2, 3]) assert.deepEqual(ids(sfxPlan('drift-tier', { tier: t })), ['drift-tick-' + t]);
  assert.deepEqual(sfxPlan('drift-release', { tier: 0 }), []);
  assert.deepEqual(sfxPlan('land', { tier: 0 }), []);
  assert.deepEqual(ids(sfxPlan('land', { tier: 3 })), ['land-heavy']);
  assert.deepEqual(sfxPlan('bump', { dv: 0.5 }, { feel }), [], 'below contact.cueMinDvMps');
  assert.equal(sfxPlan('bump', { dv: 40 }, { feel })[0].vol, 1);
  assert.deepEqual(sfxPlan('boost-start', { kind: 'pad' }), [], 'the pad event plays its own');
  assert.equal(sfxPlan('item-fire', { racer: 3, target: 0, mode: 'throw' }), null, 'incoming warning stays synth');
  assert.equal(sfxPlan('item-hit', { victim: 0, verdict: 'glass' }), null);
  const box = sfxPlan('item-box', { racer: 0 }, { feel });
  assert.equal(box.filter((x) => x.id === 'roll-tick').length, MIX.rollTicks);
  assert.deepEqual(ids(sfxPlan('racer-event', { name: 'hop', p: {} })), ['hop']);
  assert.deepEqual(sfxPlan('racer-event', { name: 'drop', p: {} }), [], 'held cues never echo');
  assert.deepEqual(BOT_SAMPLE_ECHO, BOT_ECHO);
  assert.equal(sfxPlan('effect-start', { kind: 'pink' }), null, 'beds stay synth');
});

test('bubble pops climb the ladder inside the streak and start over after it', () => {
  const st = { rung: -1, t: -1e9 };
  const rate = (t) => sfxPlan('bubble-pop', { racer: 0 }, { feel, now: t, st })[0].rate;
  const a = rate(0), b = rate(0.3), c = rate(0.6);
  assert.ok(near(a, 1) && b > a && c > b);
  assert.ok(near(rate(10), 1), 'reset after the streak');
  assert.equal(popRungNext({ rung: 6, t: 0 }, 0.1, 1.5, 8), 7);
  assert.equal(popRungNext({ rung: 7, t: 0 }, 0.1, 1.5, 8), 7, 'top rung holds');
  for (let k = 0; k < 20; k++) assert.ok(rate(20 + k * 0.1) <= 2 + 1e-9, 'never past an octave');
});

test('jitter: a fixed small rate step per play on impacts only', () => {
  const r = [0, 1, 2, 3, 4, 5].map((k) => jitterRate('bump', k));
  assert.ok(r.every((x) => x > 0.96 && x < 1.04) && new Set(r).size > 3);
  assert.equal(jitterRate('countdown', 3), 1);
  assert.equal(jitterRate('bump', 7), jitterRate('bump', 1));
});

test('the shipped bank: every planned id ships, files exist as mp3, loops sit inside the file, under 4 MB', () => {
  const planned = new Set();
  const events = [['countdown'], ['go'], ['launch-boost'], ['drift-start'], ['drift-tier', { tier: 1 }], ['drift-tier', { tier: 2 }],
    ['drift-tier', { tier: 3 }], ['drift-release', { tier: 2 }], ['boost-start', { kind: 'drift' }], ['hop'], ['land', { tier: 1 }],
    ['land', { tier: 3 }], ['wall', { dv: 10 }], ['bump', { dv: 9 }], ['pad'], ['lap', { lap: 1 }], ['lap', { lap: 2 }], ['finish'],
    ['item-box', { racer: 0 }], ['item-roll', { racer: 0, id: 'pink' }], ['item-fire', { racer: 0, mode: 'throw' }],
    ['item-fire', { racer: 0, mode: 'lob' }], ['item-fire', { racer: 0, mode: 'drop' }], ['item-hit', { victim: 0, verdict: 'hit' }],
    ['bubble-pop', { racer: 2 }], ['overtake', { racer: 0 }], ['menu-move'], ['menu-ok'], ['menu-back']];
  for (const [n, p] of events) for (const x of sfxPlan(n, p || {}, { feel, laps: 3 }) || []) planned.add(x.id);
  for (const k of ['idle', 'cruise', 'air']) planned.add('engine-' + k);
  let total = 0;
  for (const id of planned) {
    assert.ok(SFX_BANK[id], 'bank has ' + id);
    assert.ok(SFX_GAIN[id] > 0, 'gain for ' + id);
  }
  for (const [id, b] of Object.entries(SFX_BANK)) {
    const url = new URL('../audio/sfx/' + b.file, import.meta.url);
    const head = readFileSync(url).subarray(0, 3);
    assert.ok(head.toString('latin1') === 'ID3' || (head[0] === 0xff && (head[1] & 0xe0) === 0xe0), id + ' is mp3');
    total += statSync(url).size;
    if (b.loopStartS !== undefined) assert.ok(b.loopStartS > 0 && b.loopEndS > b.loopStartS && b.loopEndS < b.durS, id + ' loop points');
  }
  assert.ok(total <= 4.2e6, 'shipped audio ' + total);
});

/** A WebAudio stand-in with buffers: counts oscillators and buffer sources apart. */
function fakeCtx() {
  const log = { osc: 0, buf: 0, loops: 0, sets: 0 };
  const param = (v = 0) => {
    const p = { value: v };
    for (const k of ['setValueAtTime', 'linearRampToValueAtTime', 'exponentialRampToValueAtTime', 'setTargetAtTime', 'cancelScheduledValues']) {
      p[k] = (val) => { log.sets++; if (typeof val === 'number' && k !== 'cancelScheduledValues') p.value = val; return p; };
    }
    return p;
  };
  const node = (extra = {}) => ({ connect() {}, disconnect() {}, start() {}, stop() {}, ...extra });
  const ctx = {
    state: 'suspended', currentTime: 0, sampleRate: 8000, destination: node(),
    createGain: () => node({ gain: param(1) }),
    createBiquadFilter: () => node({ type: 'lowpass', frequency: param(350), Q: param(1) }),
    createStereoPanner: () => node({ pan: param(0) }),
    createOscillator: () => node({ type: 'sine', frequency: param(440), detune: param(0), start() { log.osc++; } }),
    createBufferSource: () => {
      const s = node({ buffer: null, loop: false, playbackRate: param(1) });
      s.start = () => { if (s.buffer && s.buffer.decoded) { log.buf++; if (s.loop) log.loops++; } };
      return s;
    },
    createBuffer: (_c, len) => ({ getChannelData: () => new Float32Array(len) }),
    decodeAudioData: async () => ({ decoded: true, duration: 6 }),
    resume: async () => { ctx.state = 'running'; },
    suspend: async () => { ctx.state = 'suspended'; },
    close() { ctx.state = 'closed'; },
  };
  return { ctx, log };
}

test('audio with files: cues play recorded sounds, a failed file falls back to the synth, engines loop', async () => {
  const bus = createBus();
  const { ctx, log } = fakeCtx();
  const asked = [];
  const fetchFn = async (url) => { asked.push(url); return { ok: !url.endsWith('/bump.mp3'), arrayBuffer: async () => new ArrayBuffer(8) }; };
  const audio = createAudio(feel, bus, { win: null, makeCtx: () => ctx, fetchFn, sfxBase: 'https://x.test/sfx/' });
  await audio.start();
  assert.equal(await audio.samplesReady, Object.keys(SFX_BANK).length - 1);
  assert.equal(asked.length, Object.keys(SFX_BANK).length);
  assert.deepEqual(audio.sfx.failed, ['bump']);
  assert.ok(audio.sfx.engine, 'the recorded engine took over');
  assert.equal(log.loops, 3, 'three engine loops');
  let o = log.osc, b = log.buf;
  bus.emit('hop', { speed: 10 });
  assert.equal(log.osc, o, 'no oscillator for a recorded cue'); assert.equal(log.buf, b + 1);
  bus.emit('bump', { a: 0, b: 2, dv: 9 });
  assert.ok(log.osc > o, 'bump.mp3 failed: the synth plays it');
  o = log.osc; b = log.buf;
  bus.emit('item-box', { racer: 0 });
  assert.equal(log.buf, b + 1 + MIX.rollTicks); assert.equal(log.osc, o);
  b = log.buf;
  audio.cue('menu-ok');
  assert.equal(log.buf, b + 1);
  const s0 = log.sets;
  audio.update(0.8, { boosting: true });
  assert.ok(log.sets > s0, 'the loops glide');
  o = log.osc;
  audio.update(0.5, {}, [1, 2, 3].map((i) => ({ i, distM: i * 8, pan: 0.2, speedPct: 0.7, boosting: false })));
  assert.equal(log.osc, o, 'rivals use the recorded loops, not saws');
  assert.equal(log.loops, 3 + 2 * feelJson.ai.audibleKarts);
  audio.setLaps(2);
  b = log.buf;
  bus.emit('lap', { lap: 1 });
  assert.equal(log.buf, b + 1, 'final lap sound');
  audio.dispose();
});

test('audio with no files (offline, node): every cue still sounds through the synth', async () => {
  const bus = createBus();
  const { ctx, log } = fakeCtx();
  const audio = createAudio(feel, bus, { win: null, makeCtx: () => ctx, fetchFn: async () => { throw new Error('offline'); } });
  await audio.start();
  assert.equal(await audio.samplesReady, 0);
  assert.equal(audio.sfx.engine, false);
  const o = log.osc;
  bus.emit('hop', { speed: 10 });
  assert.ok(log.osc > o);
  audio.dispose();
});
