import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createFeel } from '../core/feel.js';
import { createBus } from '../core/events.js';
import { createAudio } from '../audio/bus.js';
import { B1_CUES, UI_CUES, BOT_ECHO } from '../audio/cues-b1.js';
import { AUDIO_CUES } from '../audio/cues.js';
import { beatAt, sectionAt, chartAudioUrl, validChart, createMusic } from '../audio/music.js';
import { allocVoices, rivalVoice, distanceLevel, racerDetuneCents, eventLevel, rollTickTimes, ladderSemis, MIX } from '../audio/mix.js';

const feelJson = JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));
const c1 = readFileSync(new URL('../CONTRACT-B1.md', import.meta.url), 'utf8');
const B1_EVENTS = [...c1.slice(c1.indexOf('## 4. Events')).matchAll(/^\| `([a-z-]+)` \|/gm)].map((m) => m[1]);

function fakeCtx() {
  const log = { nodes: 0, started: 0, sets: [], panners: 0 };
  const param = (v = 0) => {
    const p = { value: v };
    for (const k of ['setValueAtTime', 'linearRampToValueAtTime', 'exponentialRampToValueAtTime', 'setTargetAtTime', 'cancelScheduledValues']) {
      p[k] = (val) => { log.sets.push([k, val]); if (typeof val === 'number' && k !== 'cancelScheduledValues') p.value = val; return p; };
    }
    return p;
  };
  const node = (extra = {}) => { log.nodes++; return { connect() {}, disconnect() {}, start() { log.started++; }, stop() {}, ...extra }; };
  const ctx = {
    state: 'suspended', currentTime: 0, sampleRate: 8000, destination: node(),
    createGain: () => node({ gain: param(1) }),
    createBiquadFilter: () => node({ type: 'lowpass', frequency: param(350), Q: param(1) }),
    createOscillator: () => node({ type: 'sine', frequency: param(440), detune: param(0) }),
    createBufferSource: () => node({ buffer: null, loop: false }),
    createStereoPanner: () => { log.panners++; return node({ pan: param(0) }); },
    createBuffer: (_c, len) => ({ getChannelData: () => new Float32Array(len) }),
    decodeAudioData: (bytes) => Promise.resolve({ duration: 30, bytes }),
    resume: async () => { ctx.state = 'running'; },
    suspend: async () => { ctx.state = 'suspended'; },
    close() { ctx.state = 'closed'; },
  };
  return { ctx, log };
}

test('beat phase from a chart: bpm, downbeat, before the first downbeat', () => {
  const chart = { bpm: 120, downbeatS: 0.5, sections: [] };
  assert.deepEqual(beatAt(chart, 0.5), { bpm: 120, phase: 0, index: 0 });
  const b = beatAt(chart, 0.75);
  assert.equal(b.index, 0); assert.ok(Math.abs(b.phase - 0.5) < 1e-9);
  const c = beatAt(chart, 2.5);
  assert.equal(c.index, 4); assert.ok(Math.abs(c.phase) < 1e-9);
  const pre = beatAt(chart, 0.25);
  assert.equal(pre.index, -1); assert.ok(Math.abs(pre.phase - 0.5) < 1e-9);
  assert.equal(beatAt(null, 1.25).bpm, 96, 'no chart: the slice bpm');
});

test('sections and the audio url come from the chart', () => {
  const chart = { bpm: 96, downbeatS: 0, sections: [{ id: 'induction', startS: 0 }, { id: 'deepening', startS: 40 }, { id: 'release', startS: 90 }] };
  assert.equal(sectionAt(chart, 10), 0); assert.equal(sectionAt(chart, 40), 1); assert.equal(sectionAt(chart, 200), 2);
  assert.equal(chartAudioUrl('https://x.test/tracks/preview.json', chart), 'https://x.test/tracks/preview.mp3');
  assert.equal(chartAudioUrl('https://x.test/tracks/chart.json?v=2', { audio: 'mix.ogg' }), 'https://x.test/tracks/mix.ogg');
  assert.ok(validChart(chart)); assert.ok(!validChart({ bpm: 0, sections: [] })); assert.ok(!validChart(null));
});

test('voice allocation: nearest n, the player never, a near bot keeps its slot', () => {
  const slots = [-1, -1, -1];
  const r = (i, distM) => ({ i, distM, pan: 0, speedPct: 0.5, boosting: false });
  allocVoices(slots, [r(0, 0), r(4, 30), r(2, 5), r(7, 60), r(3, 12)]);
  assert.deepEqual([...slots].sort(), [2, 3, 4]);
  const where4 = slots.indexOf(4), where2 = slots.indexOf(2);
  allocVoices(slots, [r(2, 6), r(4, 25), r(7, 8)]);            // 3 left, 7 came close
  assert.equal(slots.indexOf(4), where4, 'kept');
  assert.equal(slots.indexOf(2), where2, 'kept');
  assert.ok(slots.includes(7)); assert.ok(!slots.includes(3));
  allocVoices(slots, []);
  assert.deepEqual(slots, [-1, -1, -1]);
});

test('rival voices: quieter with distance, silent past far, detune fixed per racer', () => {
  const feel = createFeel(feelJson);
  const near = rivalVoice({ i: 2, distM: 3, speedPct: 0.8 }, feel);
  const mid = rivalVoice({ i: 2, distM: 40, speedPct: 0.8 }, feel);
  const far = rivalVoice({ i: 2, distM: 90, speedPct: 0.8 }, feel);
  assert.ok(near.gain > mid.gain && mid.gain > 0 && far.gain === 0);
  assert.ok(near.gain <= MIX.rivalGain);
  assert.ok(rivalVoice({ i: 2, distM: 3, speedPct: 1 }, feel).hz > rivalVoice({ i: 2, distM: 3, speedPct: 0 }, feel).hz);
  const cents = [1, 2, 3, 4, 5, 6, 7].map((i) => racerDetuneCents(i, 70));
  assert.equal(new Set(cents).size, 7, 'each bot sounds like itself');
  assert.ok(cents.every((c) => Math.abs(c) <= 70));
  assert.equal(racerDetuneCents(3, 70), racerDetuneCents(3, 70));
  assert.equal(distanceLevel(NaN, 4, 80), 0);
});

test('event level: the player full, a near bot quieter and panned, a far bot silent', () => {
  const near = new Map([[3, { i: 3, distM: 20, pan: -0.5 }]]);
  assert.deepEqual(eventLevel({ racer: 0 }, near, 45, 0.5), { me: true, level: 1, pan: 0 });
  assert.equal(eventLevel({ victim: 5, by: 0 }, near, 45, 0.5).me, true);
  assert.equal(eventLevel({ kind: 'spiral' }, near, 45, 0.5).me, true, 'a local event (effect-start)');
  const b = eventLevel({ racer: 3 }, near, 45, 0.5);
  assert.ok(!b.me && b.level > 0 && b.level < 0.5 && b.pan === -0.5);
  assert.equal(eventLevel({ racer: 6 }, near, 45, 0.5).level, 0);
});

test('roll ticks slow down inside the roll; the pop ladder climbs on the pentatonic', () => {
  const t = rollTickTimes(9, 1);
  assert.equal(t.length, 9); assert.equal(t[0], 0); assert.ok(t[8] < 1);
  for (let k = 2; k < t.length; k++) assert.ok(t[k] - t[k - 1] > t[k - 1] - t[k - 2]);
  assert.deepEqual([0, 1, 2, 3, 4, 5].map(ladderSemis), [0, 2, 4, 7, 9, 12]);
});

test('every B1 cue is a CONTRACT-B1 event; bots echo only one-shot Phase A cues', () => {
  for (const k of Object.keys(B1_CUES)) assert.ok(B1_EVENTS.includes(k) || AUDIO_CUES[k], k);
  for (const k of BOT_ECHO) assert.ok(AUDIO_CUES[k], k);
  assert.ok(!BOT_ECHO.includes('edge-enter') && !BOT_ECHO.includes('drop'));
  // polish 5: the lobby sounds join the menu blips; none of them is a bus event name
  assert.ok(Object.keys(UI_CUES).every((k) => k.startsWith('menu-') || ['seat-in', 'seat-out', 'ready-on', 'ready-off', 'lobby-tick'].includes(k)));
  for (const k of Object.keys(UI_CUES)) assert.ok(!B1_EVENTS.includes(k) && !AUDIO_CUES[k] && !B1_CUES[k], k);
});

test('audio B1: events for the player play, far bots stay silent, near bots play, effects duck', async () => {
  const feel = createFeel(feelJson), bus = createBus();
  const { ctx, log } = fakeCtx();
  const audio = createAudio(feel, bus, { win: null, makeCtx: () => ctx });
  await audio.start();
  const pay = { racer: 0, victim: 0, by: 3, a: 0, b: 2, kind: 'spiral', id: 'surge', mode: 'self', verdict: 'hit',
    dv: 12, state: 'in', passed: 2, from: 2, to: 1, durationS: 2, me: { place: 1 }, name: 'hop', p: {} };
  for (const name of Object.keys(B1_CUES)) {
    const n = log.started;
    bus.emit(name, pay);
    if (!['racer-event', 'effect-end'].includes(name)) assert.ok(log.started > n, name + ' plays');
  }
  const far = log.started;
  bus.emit('bump', { a: 4, b: 5, dv: 12 });
  bus.emit('item-hit', { victim: 4, by: 5, kind: 'flash', verdict: 'hit' });
  assert.equal(log.started, far, 'far bots are silent');
  audio.update(0.5, {}, [{ i: 4, distM: 10, pan: 0.4, speedPct: 0.7, boosting: false }]);
  const n2 = log.started, p2 = log.panners;
  bus.emit('bump', { a: 4, b: 5, dv: 12 });
  bus.emit('racer-event', { racer: 4, name: 'wall', p: { dv: 12 } });
  bus.emit('racer-event', { racer: 4, name: 'drop', p: {} });
  assert.ok(log.started > n2 && log.panners > p2, 'a near bot plays, panned');
  const sets = log.sets.length;
  bus.emit('effect-start', { kind: 'melt', durationS: 2, by: 3 });
  assert.ok(log.sets.slice(sets).some(([k, v]) => k === 'setTargetAtTime' && v === MIX.effectDuck), 'ducked');
  audio.setVolumes({ master: 0.5, music: 0, sfx: 0.25 });
  assert.ok(log.sets.some(([k, v]) => k === 'setTargetAtTime' && Math.abs(v - MIX.masterGain * 0.5) < 1e-9));
  const s3 = log.started;
  audio.cue('menu-ok');
  assert.ok(log.started > s3);
  audio.reset(0);
  audio.dispose();
});

test('rival engines: the nearest bots get voices, made once', async () => {
  const feel = createFeel(feelJson), bus = createBus();
  const { ctx, log } = fakeCtx();
  const audio = createAudio(feel, bus, { win: null, makeCtx: () => ctx });
  await audio.start();
  const s0 = log.started;
  const rows = [1, 2, 3, 4].map((i) => ({ i, distM: i * 10, pan: 0, speedPct: 0.6, boosting: i === 1 }));
  audio.update(0.5, {}, rows);
  const made = log.started - s0;
  assert.equal(made, 2 * feelJson.ai.audibleKarts, 'two oscillators per voice');
  audio.update(0.5, {}, rows);
  assert.equal(log.started - s0, made, 'no new voices');
  audio.dispose();
});

test('music: a chart plays and loops with its beat; a picked file uses the feel bpm; stop', async () => {
  const feel = createFeel(feelJson);
  const { ctx } = fakeCtx(); ctx.state = 'running';
  const files = {
    'https://x.test/c/preview.json': { bpm: 120, downbeatS: 0.5, sections: [{ id: 'induction', startS: 0 }, { id: 'deepening', startS: 10 }] },
  };
  const fetchFn = async (url) => ({
    ok: url in files || url.endsWith('.mp3'),
    json: async () => files[url], arrayBuffer: async () => new ArrayBuffer(8),
  });
  const out = ctx.createGain();
  const music = createMusic(ctx, out, feel, { fetchFn });
  assert.equal(music.beat, null);
  assert.equal(await music.playChart('https://x.test/c/missing.json'), false);
  assert.equal(await music.playChart('https://x.test/c/preview.json'), true);
  assert.ok(music.playing); assert.equal(music.chart.bpm, 120);
  ctx.currentTime = 10.75;
  assert.equal(music.beat.bpm, 120); assert.ok(Math.abs(music.beat.phase - 0.5) < 1e-9);
  assert.equal(music.sectionIdx, 1);
  ctx.currentTime = 40.75;                                        // looped: 30 s buffer
  assert.ok(Math.abs(music.timeS - 10.75) < 1e-9);
  assert.equal(await music.playFile({ arrayBuffer: async () => new ArrayBuffer(4) }), true);
  assert.equal(music.chart, null); assert.equal(music.beat.bpm, feelJson.clock.bpm);
  assert.equal(await music.playFile({ arrayBuffer: async () => new ArrayBuffer(4) }, { bpm: 140 }), true);
  assert.equal(music.beat.bpm, 140);
  music.setVolume(0.3);
  music.stop();
  assert.equal(music.playing, false); assert.equal(music.beat, null);
  assert.equal(await music.playFile(null), false);
  music.dispose();
});

test('music: course cancellation preserves playback and one decoded personal File while rejecting late charts',async()=>{
  const {ctx,log}=fakeCtx();ctx.state='running';let reads=0,decodes=0,finishFetch;
  const originalDecode=ctx.decodeAudioData;
  ctx.decodeAudioData=bytes=>{decodes++;return originalDecode(bytes);};
  const music=createMusic(ctx,ctx.createGain(),createFeel(feelJson),{fetchFn:()=>new Promise(resolve=>{finishFetch=resolve;})});
  const file={arrayBuffer:async()=>{reads++;return new ArrayBuffer(4);}};
  assert.equal(await music.playFile(file),true);
  const late=music.playChart('https://example.test/old-course.json'),started=log.started;
  music.cancelPending();assert.equal(music.playing,true);assert.equal(ctx.state,'running');
  finishFetch({ok:true,json:async()=>({bpm:120,sections:[]})});
  assert.equal(await late,false);assert.equal(log.started,started,'a cancelled chart cannot replace the recording');
  music.stop();assert.equal(await music.playFile(file,{bpm:140}),true);
  assert.equal(reads,1);assert.equal(decodes,1);assert.equal(music.beat.bpm,140);
  music.dispose();
});
