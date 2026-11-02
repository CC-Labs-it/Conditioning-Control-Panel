/* audio/songShape.js - where a song finds its feet: the beat shake plan (polish 6, lane BEAT).
 *
 * Pure: no audio, no DOM, no three. The song is cut into bars, each bar gets an intensity (0..1 across the
 * song) and, from a chart, a mood. The shake waits for the song's rise (the first drop or peak loud enough,
 * held for a few bars; without moods the intensity alone), then shakes on SOME beats (downbeats first),
 * growing over beatShake.rampBars rhythm bars to almost every beat. Its size starts subtle and grows to
 * beatShake.ampHalfM by the song's half. Breaks, quiet bars and the last beatShake.outroBars bars ease it back.
 * buildShape() bakes all of it into one amplitude per beat (0 = no shake), so the per-frame read is a binary
 * search on the song clock (music.songS) and an envelope: beatShakeAt().
 *
 * Two sources feed the same model: shapeFromChart(song) (audio/chart.js normalised chart: beats, downbeats,
 * sections, energy curves) and shapeFromAnalysis(analyzeBuffer(...)) for a song with no chart (a player's own
 * file): a one-time pass over the decoded samples for the tempo (phase coherence of the low-band onsets, no
 * tempo prior), the beat phase, the downbeat and the per-bar energy.
 * The live music voice registers itself with setLiveMusic(); fx/shake.js reads it with liveMusic().
 */
import { lastAtOrBefore } from './chart.js';

/** Fallback numbers; the game reads beatShake.* from feel.json (shapeParams). */
export const SHAPE_DEFAULTS = Object.freeze({
  ampStartM: 0.03, ampHalfM: 0.065, energyMix: 0.35, fracStart: 0.25, fracMax: 0.9, rampBars: 16,
  onsetLevel: 0.55, onsetHoldBars: 2, gateGroove: 0.6, gateBuild: 0.4, gateLo: 0.35, gateHi: 0.7,
  riseK: 0.7, fallK: 0.65, outroBars: 4, accentOff: 0.7, inS: 0.03, outS: 0.16, duckInS: 0.04, duckOutS: 0.35,
});

/** The model's numbers from feel (beatShake group), each falling back to SHAPE_DEFAULTS. */
export function shapeParams(feel) {
  const p = {};
  for (const k of Object.keys(SHAPE_DEFAULTS)) {
    let v;
    try { v = feel ? feel.get('beatShake.' + k) : undefined; } catch (e) { v = undefined; }
    p[k] = Number.isFinite(v) ? v : SHAPE_DEFAULTS[k];
  }
  return p;
}

const sstep = (x) => (x <= 0 ? 0 : x >= 1 ? 1 : x * x * (3 - 2 * x));
const clamp01 = (x) => (x < 0 ? 0 : x > 1 ? 1 : x);

/** Mood weight of a bar: how much rhythm part it is (null mood: none, the intensity decides). */
export function moodGate(mood, P) {
  switch (mood) {
    case 'drop': case 'peak': return 1;
    case 'groove': return P.gateGroove;
    case 'build': return P.gateBuild;
    case 'intro': case 'break': case 'outro': return 0;
    default: return -1;
  }
}

/** Value at fraction q (0..1) of a sorted copy of xs. */
function quantile(xs, q) {
  if (!xs.length) return 0;
  const s = Array.from(xs).sort((a, b) => a - b);
  const i = Math.min(s.length - 1, Math.max(0, Math.round(q * (s.length - 1))));
  return s[i];
}

/**
 * Bars of a normalised chart: { startS, endS, beats: number[], mood, raw } with raw the bar loudness (the mix
 * and drum curves' mean, blended with the section energy; the section energy alone without curves).
 */
export function barsFromChart(song) {
  if (!song) return [];
  let downs = song.downbeats && song.downbeats.length ? Array.from(song.downbeats) : [];
  const beats = song.beats && song.beats.length ? Array.from(song.beats) : [];
  const dur = song.durationS > 0 ? song.durationS : 0;
  if (!downs.length) {
    // no list: every four beats from the beats list, else from bpm and downbeatS
    if (beats.length) for (let i = 0; i < beats.length; i += 4) downs.push(beats[i]);
    else { const bar = 240 / (song.bpm > 0 ? song.bpm : 96); for (let t = song.downbeatS || 0; t < dur; t += bar) downs.push(t); }
  }
  const beatList = beats.length ? beats : gridBeats(downs, dur);
  const curves = ['mix', 'drums'].filter((k) => song.energy && song.energy[k] && song.energy[k].length);
  const fps = song.energy && song.energy.fps > 0 ? song.energy.fps : 25;
  const bars = [];
  for (let b = 0; b < downs.length; b++) {
    const startS = downs[b], endS = b + 1 < downs.length ? downs[b + 1] : Math.max(dur, startS + (b ? startS - downs[b - 1] : 2));
    if (endS - startS < 0.05) continue;
    const i0 = lastAtOrBefore(beatList, startS - 1e-6) + 1;
    const bb = [];
    for (let i = i0; i < beatList.length && beatList[i] < endS - 1e-6; i++) bb.push(beatList[i]);
    if (!bb.length) bb.push(startS);
    const sec = sectionAt(song.sections, startS + 0.01);
    const secE = sec ? sec.energy : 0.5;
    let raw = secE;
    if (curves.length) {
      let sum = 0, n = 0;
      for (const k of curves) {
        const a = song.energy[k], f0 = Math.floor(startS * fps), f1 = Math.min(a.length, Math.ceil(endS * fps));
        for (let f = f0; f < f1; f++) { sum += a[f] / 255; n++; }
      }
      raw = n ? 0.65 * (sum / n) + 0.35 * secE : secE;
    }
    bars.push({ startS, endS, beats: bb, mood: sec ? sec.mood : null, raw });
  }
  return bars;
}

function sectionAt(list, t) {
  let s = null;
  if (list) for (const x of list) { if (x.startS <= t) s = x; else break; }
  return s;
}

/** Four even beats per bar from a downbeat list (a chart with no beats list). */
function gridBeats(downs, dur) {
  const out = [];
  for (let b = 0; b < downs.length; b++) {
    const a = downs[b], e = b + 1 < downs.length ? downs[b + 1] : Math.max(dur, a + (b ? a - downs[b - 1] : 2));
    for (let k = 0; k < 4; k++) out.push(a + (e - a) * k / 4);
  }
  return out;
}

/** Bars of a buffer analysis (analyzeBuffer): no moods, the bar energy is the raw loudness. */
export function barsFromAnalysis(an) {
  if (!an || !an.downbeats || !an.downbeats.length) return [];
  const bars = [];
  for (let b = 0; b < an.downbeats.length; b++) {
    const startS = an.downbeats[b], endS = b + 1 < an.downbeats.length ? an.downbeats[b + 1] : startS + 4 * 60 / an.bpm;
    const bb = [];
    const i0 = lastAtOrBefore(an.beats, startS - 1e-6) + 1;
    for (let i = i0; i < an.beats.length && an.beats[i] < endS - 1e-6; i++) bb.push(an.beats[i]);
    if (!bb.length) bb.push(startS);
    bars.push({ startS, endS, beats: bb, mood: null, raw: an.barEnergy[b] || 0 });
  }
  return bars;
}

/** Order the beats of a bar take their turn: downbeat, half bar, then the rest (4: 0, 2, 1, 3). */
export function beatOrder(n) {
  const out = [0];
  for (let i = 2; i < n; i += 2) out.push(i);
  for (let i = 1; i < n; i += 2) out.push(i);
  return out.slice(0, Math.max(n, 0));
}

/**
 * Bake the plan. @returns {{ beats: Float64Array, amp: Float32Array, onsetS: number, onsetBar: number,
 * bars: {startS, mood, level, gate, frac, ampM, shaken, of}[], durationS }}
 */
export function buildShape(bars, durationS, P = SHAPE_DEFAULTS) {
  const n = bars.length;
  const raws = bars.map((b) => b.raw);
  const lo = quantile(raws, 0.1), hi = quantile(raws, 0.9), span = Math.max(hi - lo, 1e-3);
  const level = raws.map((r) => clamp01((r - lo) / span));
  const hasMood = bars.some((b) => b.mood && moodGate(b.mood, P) >= 0);
  const rhythmic = (b) => {
    const g = hasMood ? moodGate(bars[b].mood, P) : -1;
    return (hasMood ? g >= 1 : true) && level[b] >= P.onsetLevel;
  };
  // the rise: the first run of onsetHoldBars rhythm bars
  let onsetBar = -1;
  const hold = Math.max(1, Math.round(P.onsetHoldBars));
  for (let b = 0; b + hold <= n && onsetBar < 0; b++) {
    let ok = true;
    for (let k = 0; k < hold && ok; k++) ok = rhythmic(b + k);
    if (ok) onsetBar = b;
  }
  const dur = durationS > 0 ? durationS : n ? bars[n - 1].endS : 0;
  const onsetS = onsetBar >= 0 ? bars[onsetBar].startS : Infinity;
  const halfS = Math.max(dur / 2, onsetS + 8 * (n ? (bars[n - 1].endS - bars[0].startS) / n : 2));
  const beatT = [], beatA = [], out = [];
  let gs = 0, act = 0, carry = 0.5;   // a half carry: the first rhythm bar lands its downbeat
  for (let b = 0; b < n; b++) {
    const bar = bars[b];
    let g = 0;
    if (onsetBar >= 0 && b >= onsetBar) {
      const mg = hasMood ? moodGate(bar.mood, P) : -1;
      const lg = sstep((level[b] - P.gateLo) / Math.max(P.gateHi - P.gateLo, 1e-3));
      g = mg >= 0 ? mg * (0.5 + 0.5 * lg) : lg;
      const fromEnd = n - 1 - b;
      g *= sstep(fromEnd / Math.max(P.outroBars, 1e-3));
    }
    gs += (g - gs) * (g > gs ? P.riseK : P.fallK);
    if (gs < 0.02 && g === 0) gs = 0;
    act += gs;
    const frac = gs * (P.fracStart + (P.fracMax - P.fracStart) * sstep(act / Math.max(P.rampBars, 1)));
    const grow = Number.isFinite(onsetS) ? sstep((bar.startS - onsetS) / Math.max(halfS - onsetS, 1e-3)) : 0;
    const ampM = gs * (P.ampStartM + (P.ampHalfM - P.ampStartM) * grow) * (1 - P.energyMix + P.energyMix * level[b]);
    const k = bar.beats.length;
    let shaken = 0;
    if (frac > 0) {
      const want = frac * k + carry;
      shaken = Math.min(k, Math.floor(want + 1e-9));
      carry = want - shaken;
    } else carry = 0.5;
    const order = beatOrder(k), on = new Uint8Array(k);
    for (let i = 0; i < shaken; i++) on[order[i]] = 1;
    for (let i = 0; i < k; i++) {
      beatT.push(bar.beats[i]);
      beatA.push(on[i] ? ampM * (i === 0 ? 1 : P.accentOff) : 0);
    }
    out.push({ startS: bar.startS, mood: bar.mood, level: level[b], gate: gs, frac, ampM, shaken, of: k });
  }
  return { beats: Float64Array.from(beatT), amp: Float32Array.from(beatA), onsetS, onsetBar, bars: out, durationS: dur };
}

/** The plan for a normalised chart (audio/chart.js). */
export function shapeFromChart(song, P = SHAPE_DEFAULTS) {
  return buildShape(barsFromChart(song), song ? song.durationS : 0, P);
}

/** The plan for a buffer analysis (analyzeBuffer). */
export function shapeFromAnalysis(an, P = SHAPE_DEFAULTS) {
  return buildShape(barsFromAnalysis(an), an ? an.durationS : 0, P);
}

/** One beat's envelope at d seconds from its beat: rises over inS before it, falls over outS after. 0..1. */
export function beatEnv(d, inS, outS) {
  if (d < 0) return d <= -inS ? 0 : sstep(1 + d / Math.max(inS, 1e-4));
  return d >= outS ? 0 : 1 - sstep(d / Math.max(outS, 1e-4));
}

/** The beat shake at song time tS, metres (the strongest of the beat now rising and the one falling). */
export function beatShakeAt(shape, tS, P = SHAPE_DEFAULTS) {
  if (!shape || !shape.beats.length || !Number.isFinite(tS)) return 0;
  const i = lastAtOrBefore(shape.beats, tS + P.inS);
  let best = 0;
  for (let k = i; k >= 0 && k >= i - 2; k--) {
    const a = shape.amp[k];
    if (!(a > 0)) continue;
    const v = a * beatEnv(tS - shape.beats[k], P.inS, P.outS);
    if (v > best) best = v;
  }
  return best;
}

/**
 * One-time analysis of decoded samples (a song with no chart). channels: Float32Array[] (any count), sr Hz.
 * @returns {{ bpm, beats: Float64Array, downbeats: Float64Array, barEnergy: Float32Array, durationS }}
 */
export function analyzeBuffer(channels, sr, { fps = 100, minBpm = 70, maxBpm = 190 } = {}) {
  const chs = (channels || []).filter((c) => c && c.length);
  if (!chs.length || !(sr > 0)) return null;
  const len = chs[0].length, hop = Math.max(1, Math.round(sr / fps)), frames = Math.floor(len / hop);
  if (frames < fps * 4) return null;
  const rate = sr / hop;
  const rms = new Float32Array(frames), low = new Float32Array(frames);
  const kLow = 1 - Math.exp(-2 * Math.PI * 150 / sr);   // one-pole low pass at 150 Hz: the kick band
  let lp = 0;
  const nc = chs.length;
  for (let f = 0; f < frames; f++) {
    let s2 = 0, l2 = 0;
    for (let j = f * hop, e = j + hop; j < e; j++) {
      let x = 0;
      for (let c = 0; c < nc; c++) x += chs[c][j];
      x /= nc;
      lp += (x - lp) * kLow;
      s2 += x * x; l2 += lp * lp;
    }
    rms[f] = Math.sqrt(s2 / hop); low[f] = Math.sqrt(l2 / hop);
  }
  // onset strength: the rise of the log loudness, the kick band counted double
  const on = new Float32Array(frames);
  for (let f = 1; f < frames; f++) {
    const dl = Math.log(1e-4 + low[f]) - Math.log(1e-4 + low[f - 1]);
    const dr = Math.log(1e-4 + rms[f]) - Math.log(1e-4 + rms[f - 1]);
    on[f] = 2 * Math.max(0, dl) + Math.max(0, dr);
  }
  let mean = 0;
  for (let f = 0; f < frames; f++) mean += on[f];
  mean /= frames;
  for (let f = 0; f < frames; f++) on[f] -= mean;
  // tempo: phase coherence of the onsets at each candidate beat rate (no prior), the half-time harmonic helping
  const coh = (bpm) => {
    const w = 2 * Math.PI * bpm / 60 / rate, cw = Math.cos(w), sw = Math.sin(w);
    let re = 0, im = 0, zr = 1, zi = 0;   // z = e^{-iwf}, rotated one frame at a time (no trig in the loop)
    for (let f = 0; f < frames; f++) {
      re += on[f] * zr; im += on[f] * zi;
      const nr = zr * cw + zi * sw; zi = zi * cw - zr * sw; zr = nr;
      if ((f & 1023) === 1023) { const m = Math.hypot(zr, zi); zr /= m; zi /= m; }
    }
    return { mag: Math.hypot(re, im), arg: Math.atan2(im, re) };
  };
  const score = (bpm) => coh(bpm).mag + 0.5 * coh(bpm * 2).mag + 0.25 * coh(bpm / 2).mag;
  let best = minBpm, bestS = -1;
  for (let b = minBpm; b <= maxBpm; b += 0.5) { const s = score(b); if (s > bestS) { bestS = s; best = b; } }
  let fine = best, fineS = -1;
  for (let b = best - 0.6; b <= best + 0.6; b += 0.02) { const s = coh(b).mag; if (s > fineS) { fineS = s; fine = b; } }
  const bpm = fine;
  // beat phase: the onsets peak where w*f = -arg (the sum is sum on[f] e^{-iwf})
  const c = coh(bpm), period = rate * 60 / bpm;
  let f0 = (-c.arg / (2 * Math.PI)) * period;
  f0 = ((f0 % period) + period) % period;
  // beat tracking: a made song drifts, so each beat looks for its onset near the prediction and the period
  // follows the found beats slowly (held within 5% of the global tempo); a quiet stretch keeps the grid
  const beats = [], win = Math.max(2, Math.round(period * 0.12)), sig = win * 0.5;
  let p = period, f = f0, prevT = -1;
  while (f < frames) {
    const c0 = Math.round(f);
    let bestF = f, bestV = 0;
    for (let d = -win; d <= win; d++) {
      const g = c0 + d;
      if (g <= 0 || g >= frames) continue;
      const v = (on[g] + mean) * Math.exp(-0.5 * (d / sig) * (d / sig));
      if (v > bestV) { bestV = v; bestF = g; }
    }
    const t = bestV > 2 * mean ? f + (bestF - f) * 0.7 : f;
    beats.push(t / rate);
    if (prevT >= 0) p += (Math.min(Math.max(t - prevT, period * 0.95), period * 1.05) - p) * 0.1;
    prevT = t; f = t + p;
  }
  // downbeat: the beat slot of four where the song's loudness steps up (phrases and sections start on a bar),
  // the kick-band onset breaking ties
  const slot = [0, 0, 0, 0], meanRms = (a, b) => {
    let sum = 0, n = 0;
    for (let g = Math.max(0, a); g < Math.min(frames, b); g++) { sum += rms[g]; n++; }
    return n ? sum / n : 0;
  };
  const span = Math.round(2 * period);
  for (let i = 0; i < beats.length; i++) {
    const g0 = Math.round(beats[i] * rate);
    let m = 0;
    for (let d = -3; d <= 3; d++) { const g = g0 + d; if (g > 0 && g < frames) m = Math.max(m, low[g] - low[g - 1]); }
    slot[i % 4] += 4 * Math.max(0, meanRms(g0, g0 + span) - meanRms(g0 - span, g0)) + 0.25 * Math.max(0, m);
  }
  let first = 0;
  for (let k = 1; k < 4; k++) if (slot[k] > slot[first]) first = k;
  // the onset frames trail the hit by about ONSET_LAG_S (frame size and the kick-band low pass)
  for (let i = 0; i < beats.length; i++) beats[i] = Math.max(0, beats[i] - ONSET_LAG_S);
  const downbeats = [];
  for (let i = first; i < beats.length; i += 4) downbeats.push(beats[i]);
  const barEnergy = new Float32Array(downbeats.length);
  for (let b = 0; b < downbeats.length; b++) {
    const a = Math.floor(downbeats[b] * rate), e = Math.min(frames, b + 1 < downbeats.length ? Math.floor(downbeats[b + 1] * rate) : a + Math.round(4 * period));
    let s = 0;
    for (let f = a; f < e; f++) s += rms[f];
    barEnergy[b] = e > a ? s / (e - a) : 0;
  }
  return { bpm, beats: Float64Array.from(beats), downbeats: Float64Array.from(downbeats), barEnergy, durationS: len / sr };
}

/** How far the analysis' onset frames trail the hit they mark, seconds (measured on the pink road against its chart). */
export const ONSET_LAG_S = 0.035;

/* The live music voice (audio/music.js registers itself); fx/shake.js reads it. */
let live = null;
/** @param {{ songS: number, song: object|null, analysis: object|null, playing: boolean }|null} m */
export function setLiveMusic(m) { live = m || null; }
export function liveMusic() { return live; }
