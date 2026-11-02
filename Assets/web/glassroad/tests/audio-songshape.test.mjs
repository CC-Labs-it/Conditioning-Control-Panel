/* Polish 6 (BEAT): the song shape model (audio/songShape.js), pure. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { normalizeChart } from '../audio/chart.js';
import { SHAPE_DEFAULTS, shapeParams, barsFromChart, buildShape, shapeFromChart, shapeFromAnalysis, beatOrder,
  beatEnv, beatShakeAt, analyzeBuffer, moodGate } from '../audio/songShape.js';
import { createFeel } from '../core/feel.js';
import { readFileSync } from 'node:fs';

const feelJson = JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));
const P = SHAPE_DEFAULTS;

/** A 120 bpm chart: 8 intro, 16 drop, 4 break, 16 peak, 4 outro bars (2 s each). */
function chart() {
  const plan = [['intro', 8, 0.2], ['drop', 16, 0.8], ['break', 4, 0.3], ['peak', 16, 0.9], ['outro', 4, 0.2]];
  const sections = [], beats = [], downbeats = [];
  let bar = 0;
  for (const [mood, n, energy] of plan) {
    sections.push({ id: mood + bar, startS: bar * 2, endS: (bar + n) * 2, mood, energy });
    bar += n;
  }
  for (let b = 0; b < bar; b++) { downbeats.push(b * 2); for (let k = 0; k < 4; k++) beats.push(b * 2 + k * 0.5); }
  return normalizeChart({ bpm: 120, downbeatS: 0, beats, downbeats, sections, durationS: bar * 2 });
}

test('songShape: feel numbers match the defaults and every key is read', () => {
  const p = shapeParams(createFeel(feelJson));
  assert.deepEqual(Object.keys(p).sort(), Object.keys(SHAPE_DEFAULTS).sort());
  for (const k of Object.keys(p)) assert.equal(p[k], SHAPE_DEFAULTS[k], k);
  assert.deepEqual(shapeParams(null), { ...SHAPE_DEFAULTS });
});

test('songShape: beat order is downbeat, half bar, then the rest; moods gate', () => {
  assert.deepEqual(beatOrder(4), [0, 2, 1, 3]);
  assert.deepEqual(beatOrder(3), [0, 2, 1]);
  assert.deepEqual(beatOrder(1), [0]);
  assert.equal(moodGate('drop', P), 1); assert.equal(moodGate('break', P), 0); assert.equal(moodGate(null, P), -1);
});

test('songShape: nothing before the rise, the onset lands on the first drop downbeat', () => {
  const sh = shapeFromChart(chart(), P);
  assert.equal(sh.onsetBar, 8); assert.equal(sh.onsetS, 16);
  for (let i = 0; i < sh.beats.length && sh.beats[i] < 16; i++) assert.equal(sh.amp[i], 0);
  const b8 = sh.bars[8];
  assert.equal(b8.shaken, 1, 'downbeat only at the onset');
  const i16 = sh.beats.indexOf(16);
  assert.ok(sh.amp[i16] > 0 && sh.amp[i16 + 1] === 0);
});

test('songShape: some beats, growing to almost every beat; subtle first, bigger by the half', () => {
  const sh = shapeFromChart(chart(), P);
  const drop = sh.bars.slice(8, 24), peak = sh.bars.slice(28, 44);
  assert.ok(drop[0].frac < 0.3 && drop[15].frac > 0.8, drop[0].frac + ' ' + drop[15].frac);
  for (let i = 2; i < drop.length; i++) assert.ok(drop[i].frac >= drop[i - 1].frac - 1e-9);
  const early = drop.slice(0, 4).reduce((s, b) => s + b.shaken, 0), late = peak.slice(4, 12).reduce((s, b) => s + b.shaken, 0);
  assert.ok(early <= 6, 'early ' + early);
  assert.ok(late >= 8 * 3, 'late ' + late);   // almost every beat
  assert.ok(late < 8 * 4, 'never every single beat');
  assert.ok(drop[0].ampM < 0.6 * P.ampHalfM, 'subtle start ' + drop[0].ampM);
  assert.ok(peak[8].ampM > 0.8 * P.ampHalfM, 'noticeable by the half ' + peak[8].ampM);
  for (const b of sh.bars) assert.ok(b.ampM <= P.ampHalfM + 1e-9);
});

test('songShape: breaks ease back over bars, the outro tapers to rest', () => {
  const sh = shapeFromChart(chart(), P);
  const g = sh.bars.map((b) => b.gate);
  assert.ok(g[24] > 0 && g[24] < g[23], 'first break bar eases, never cuts');
  assert.ok(g[25] < g[24] && g[27] < 0.05);
  assert.ok(g[28] > g[27], 'the peak brings it back');
  assert.ok(sh.bars[47].ampM < 1e-3 && sh.bars[47].shaken === 0);
});

test('songShape: a dip rises into the beat and falls after it (IN and OUT)', () => {
  assert.equal(beatEnv(-P.inS, P.inS, P.outS), 0);
  assert.equal(beatEnv(0, P.inS, P.outS), 1);
  assert.equal(beatEnv(P.outS, P.inS, P.outS), 0);
  let prev = 0, maxStep = 0;
  for (let d = -0.05; d < 0.25; d += 0.001) { const v = beatEnv(d, P.inS, P.outS); maxStep = Math.max(maxStep, Math.abs(v - prev)); prev = v; }
  assert.ok(maxStep < 0.06, 'no hard step ' + maxStep);
  const sh = shapeFromChart(chart(), P);
  const i = sh.amp.findIndex((a) => a > 0), t = sh.beats[i];
  assert.ok(Math.abs(beatShakeAt(sh, t, P) - sh.amp[i]) < 1e-9);
  assert.equal(beatShakeAt(sh, t + 0.3, P), 0);   // rest between beats
  assert.equal(beatShakeAt(sh, NaN, P), 0); assert.equal(beatShakeAt(null, 1, P), 0);
});

test('songShape: no moods, the loudness alone finds the rise', () => {
  const bars = [];
  for (let b = 0; b < 40; b++) bars.push({ startS: b * 2, endS: b * 2 + 2, beats: [0, 0.5, 1, 1.5].map((x) => b * 2 + x), mood: null, raw: b < 12 || (b >= 26 && b < 30) ? 0.2 : 0.9 });
  const sh = buildShape(bars, 80, P);
  assert.equal(sh.onsetBar, 12);
  assert.ok(sh.bars[27].gate < sh.bars[25].gate, 'a quiet stretch eases');
  assert.ok(sh.bars[33].frac > sh.bars[13].frac);
});

test('songShape: a chart with only bpm and sections still shapes', () => {
  const c = normalizeChart({ bpm: 120, downbeatS: 0, durationS: 64, sections: [{ startS: 0, mood: 'intro', energy: 0.1 }, { startS: 16, mood: 'drop', energy: 0.9 }, { startS: 56, mood: 'outro', energy: 0.1 }] });
  const bars = barsFromChart(c);
  assert.equal(bars.length, 32); assert.equal(bars[0].beats.length, 4);
  const sh = buildShape(bars, 64, P);
  assert.equal(sh.onsetS, 16);
});

/** A click track: a 60 Hz kick on every beat (downbeats louder), quiet first 16 s. */
function clicks(bpm, sr, seconds, offsetS) {
  const x = new Float32Array(sr * seconds), beat = 60 / bpm;
  for (let k = 0, t = offsetS; t < seconds; k++, t += beat) {
    const i0 = Math.round(t * sr), loud = (t < 16 ? 0.25 : 1) * (k % 4 === 0 ? 1 : 0.6);
    for (let j = 0; j < sr * 0.08 && i0 + j < x.length; j++) x[i0 + j] += loud * Math.sin(2 * Math.PI * 60 * j / sr) * Math.exp(-j / (sr * 0.03));
  }
  return x;
}

test('songShape: analysis finds tempo, beat phase, downbeat and the loud part of a picked file', () => {
  const sr = 8000, x = clicks(128, sr, 48, 0.3);
  const an = analyzeBuffer([x, x], sr);
  assert.ok(Math.abs(an.bpm - 128) < 0.3, 'bpm ' + an.bpm);
  const beat = 60 / 128;
  const ph = ((an.beats[0] - 0.3) / beat) % 1, err = Math.min(ph, 1 - ph) * beat;
  assert.ok(err < 0.05, 'phase error ' + err);   // onset frames are 10 ms; ONSET_LAG_S is tuned on real kicks, not clicks
  const d = ((an.downbeats[3] - 0.3) / (4 * beat)) % 1;
  assert.ok(Math.min(d, 1 - d) * 4 * beat < 0.05, 'downbeat ' + an.downbeats[3]);
  const sh = shapeFromAnalysis(an, P);
  assert.ok(sh.onsetS > 14 && sh.onsetS < 19, 'onset ' + sh.onsetS);
  assert.equal(analyzeBuffer([], sr), null); assert.equal(analyzeBuffer([new Float32Array(100)], sr), null);
});
