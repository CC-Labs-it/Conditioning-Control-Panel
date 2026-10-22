/* audio/chart.js - reads a song chart (handoff-1022/MUSIC-ROAD-BRIEF.md section 3) at a song time.
 *
 * Pure: no audio, no DOM. normalizeChart(raw) fills every optional field, sorts every list and turns the
 * hit pairs into flat arrays, so the per-frame queries are binary searches with no allocation. Every
 * query takes the song position tS (seconds from the start of the audio file); the one clock is the
 * song (audio/music.js songS), never sim or frame time. A chart with no beats list falls back to bpm and
 * downbeatS, so a plain { bpm, sections } chart still drives the beat.
 */

export const MOODS = Object.freeze(['intro', 'build', 'drop', 'groove', 'break', 'peak', 'outro']);
const HIT_KINDS = ['kick', 'snare', 'hats'];
const ENERGY_KINDS = ['mix', 'drums', 'bass', 'vocals'];

const num = (x, d = 0) => (Number.isFinite(+x) ? +x : d);
const byStart = (a, b) => a.startS - b.startS;

/** Last index with arr[i] <= t (arr sorted ascending), -1 before the first. */
export function lastAtOrBefore(arr, t) {
  let lo = 0, hi = arr.length - 1, best = -1;
  while (lo <= hi) {
    const mid = (lo + hi) >> 1;
    if (arr[mid] <= t) { best = mid; lo = mid + 1; } else hi = mid - 1;
  }
  return best;
}

/** Last index with list[i].startS <= t, -1 before the first. */
function lastStartAtOrBefore(list, t) {
  let lo = 0, hi = list.length - 1, best = -1;
  while (lo <= hi) {
    const mid = (lo + hi) >> 1;
    if (list[mid].startS <= t) { best = mid; lo = mid + 1; } else hi = mid - 1;
  }
  return best;
}

/** A clean chart: every list present and sorted, hits as { t: Float64Array, v: Float32Array }. */
export function normalizeChart(raw) {
  const c = raw && typeof raw === 'object' ? raw : {};
  const bpm = num(c.bpm) > 0 ? num(c.bpm) : 96;
  const downbeatS = num(c.downbeatS);
  const times = (a) => (Array.isArray(a) ? a.map((x) => num(x, NaN)).filter(Number.isFinite).sort((x, y) => x - y) : []);
  const beats = times(c.beats), downbeats = times(c.downbeats);
  const sections = (Array.isArray(c.sections) ? c.sections : []).map((s, i) => ({
    id: String(s.id || 's' + (i + 1)),
    startS: num(s.startS ?? s.start), endS: num(s.endS ?? s.end, Infinity),
    mood: MOODS.includes(s.mood) ? s.mood : 'groove',
    energy: Math.max(0, Math.min(1, num(s.energy, 0.5))),
    loop: Math.max(0, Math.min(1, num(s.loop, 0))),
  })).sort(byStart);
  const hits = {};
  for (const k of HIT_KINDS) {
    const pairs = (c.hits && Array.isArray(c.hits[k]) ? c.hits[k] : [])
      .map((p) => (Array.isArray(p) ? [num(p[0], NaN), num(p[1], 1)] : [num(p, NaN), 1]))
      .filter((p) => Number.isFinite(p[0])).sort((a, b) => a[0] - b[0]);
    hits[k] = { t: Float64Array.from(pairs, (p) => p[0]), v: Float32Array.from(pairs, (p) => Math.max(0, Math.min(1, p[1]))) };
  }
  const e = c.energy && typeof c.energy === 'object' ? c.energy : {};
  const energy = { fps: num(e.fps) > 0 ? num(e.fps) : 25 };
  for (const k of ENERGY_KINDS) energy[k] = Uint8Array.from(Array.isArray(e[k]) ? e[k] : [], (x) => Math.max(0, Math.min(255, num(x))));
  const timed = (a, f) => (Array.isArray(a) ? a.map(f).filter((x) => Number.isFinite(x.startS)).sort(byStart) : []);
  const lines = timed(c.lines, (l, i) => ({ i: num(l.i, i), startS: num(l.startS, NaN), endS: num(l.endS), text: String(l.text || ''), tag: l.tag || null }));
  const words = timed(c.words, (w) => ({ w: String(w.w || ''), startS: num(w.startS, NaN), endS: num(w.endS ?? w.startS), line: num(w.line, -1), bubble: !!w.bubble, gate: !!w.gate }));
  const echoes = timed(c.echoes, (x) => ({ line: num(x.line, -1), startS: num(x.startS, NaN), endS: num(x.endS ?? x.startS), pan: Math.max(-1, Math.min(1, num(x.pan))), text: String(x.text || '') }));
  // lane A's added fields: a drop trigger's strength (0-1), a drop swell's hitS (the drop itself; startS is the swell's entry)
  const triggers = timed(c.triggers, (x) => ({ startS: num(x.startS, NaN), kind: x.kind === 'drop' ? 'drop' : 'word', w: x.w ? String(x.w) : '',
    strength: Math.max(0, Math.min(1, num(x.strength, 1))) }));
  const sfx = timed(c.sfx, (x) => ({ startS: num(x.startS, NaN), id: String(x.id || ''), kind: String(x.kind || ''), hitS: num(x.hitS, num(x.startS, NaN)) }));
  const durationS = num(c.durationS) > 0 ? num(c.durationS)
    : Math.max(beats[beats.length - 1] || 0, sections.length ? sections[sections.length - 1].endS : 0, 1);
  return { id: String(c.id || 'song'), title: String(c.title || ''), audio: c.audio || null, version: num(c.version, 1), durationS, bpm, downbeatS,
    beats, downbeats, sections, hits, energy, lines, words, echoes, triggers, sfx,
    wordStarts: Float64Array.from(words, (w) => w.startS), triggerStarts: Float64Array.from(triggers, (x) => x.startS),
    echoStarts: Float64Array.from(echoes, (x) => x.startS), sfxStarts: Float64Array.from(sfx, (x) => x.startS) };
}

/** Beat at tS: { index, phase 0..1, lenS } from the beats list, else from bpm and downbeatS. */
export function beatOf(c, tS, out = { index: 0, phase: 0, lenS: 0.5 }) {
  return gridOf(c.beats, 60 / c.bpm, c.downbeatS, tS, out);
}

/** Bar at tS: { index, phase 0..1, lenS } from the downbeats list, else four beats from downbeatS. */
export function barOf(c, tS, out = { index: 0, phase: 0, lenS: 2 }) {
  return gridOf(c.downbeats, 240 / c.bpm, c.downbeatS, tS, out);
}

function gridOf(list, stepS, originS, tS, out) {
  const i = lastAtOrBefore(list, tS);
  if (i >= 0 && i < list.length - 1) {
    const a = list[i], b = list[i + 1];
    out.index = i; out.lenS = b - a; out.phase = (tS - a) / (b - a);
    return out;
  }
  // before the first or after the last listed point: keep the grid going at the nominal step
  const base = i >= 0 ? list[i] : list.length ? list[0] : originS;
  const pos = (tS - base) / stepS, whole = Math.floor(pos);
  out.index = (i >= 0 ? i : 0) + whole; out.lenS = stepS; out.phase = pos - whole;
  return out;
}

/** The section playing at tS (the last one started), or null with no sections. */
export function sectionOf(c, tS) {
  const i = lastStartAtOrBefore(c.sections, tS);
  return c.sections[Math.max(0, i)] || null;
}

/** The next downbeat at or after tS (the bar line a mode change waits for). */
export function nextBarS(c, tS) {
  const list = c.downbeats;
  const i = lastAtOrBefore(list, tS - 1e-6) + 1;
  if (i < list.length) return list[i];
  const b = barOf(c, tS);
  return tS + (1 - b.phase) * b.lenS;
}

/**
 * Hit envelope at tS for one kind: the strongest recent hit, each rising over attackS and decaying with
 * time constant decayS. Hits up to attackS ahead are counted (the rise leads the sound a hair). 0..1.
 */
export function hitEnv(c, kind, tS, attackS = 0.012, decayS = 0.12) {
  const h = c.hits[kind];
  if (!h || !h.t.length) return 0;
  let i = lastAtOrBefore(h.t, tS + attackS), best = 0;
  for (let k = 0; i >= 0 && k < 6; i--, k++) {
    const dt = tS - h.t[i];
    if (dt > decayS * 6) break;
    const e = dt < 0 ? 1 + dt / attackS : Math.exp(-dt / decayS);
    const v = h.v[i] * Math.max(0, e);
    if (v > best) best = v;
  }
  return best;
}

/** Energy (0..1) of one stem curve at tS, linearly read between frames; 0 when the curve is empty. */
export function energyOf(c, kind, tS) {
  const a = c.energy[kind];
  if (!a || !a.length) return 0;
  const x = Math.max(0, tS * c.energy.fps), i = Math.floor(x);
  if (i >= a.length - 1) return a[a.length - 1] / 255;
  const f = x - i;
  return (a[i] * (1 - f) + a[i + 1] * f) / 255;
}

/** Seconds since the last event in a sorted start list (starts) at or before tS, Infinity if none. */
export function sinceLast(starts, tS) {
  const i = lastAtOrBefore(starts, tS);
  return i < 0 ? Infinity : tS - starts[i];
}

/** Index of the next event in a sorted start list strictly after tS, or -1. */
export function nextIndex(starts, tS) {
  const i = lastAtOrBefore(starts, tS) + 1;
  return i < starts.length ? i : -1;
}
