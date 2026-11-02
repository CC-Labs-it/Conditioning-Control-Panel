/* meta/ghost.js - the Time Trial ghost (CONTRACT-LOGBOOK.md sections 1 and 7).
 *
 * The recorder samples the player's kart at GHOST_HZ (10) on the race clock: world position, orientation,
 * track s and speedPct, enough to draw a kart (NET.md kart record units: centimetres, quat components
 * scaled to i16). encode packs the channels as zigzag varints of SECOND-order deltas (a kart at a steady
 * speed on a smooth bend costs about a byte a channel), base64 inside a small JSON wrapper. sample(ghost, t)
 * interpolates between samples (positions linear, the quat normalised linear; the recorder keeps the quat
 * on one hemisphere so a lerp never flips). One best per course in localStorage `glassroad.ghost.v1.<track>`,
 * only a finished trial, only when faster, never over GHOST_MAX_CHARS. A ghost of the course's own laps (fullLaps)
 * keeps that key; any other lap count (playtest 2026-10-28: solo races default to 1 lap) has its own,
 * `<track>.l<laps>`, so neither replaces the other. Without fullLaps every ghost shares the plain key (the old rule).
 * Pure: no DOM, no three.js; storage always in try/catch.
 */
import { S } from '../sim/state.js';

export const GHOST_V = 1;
export const GHOST_HZ = 10;
export const GHOST_KEY_PREFIX = 'glassroad.ghost.v1.';
export const GHOST_MAX_CHARS = 200000;
export const GHOST_MAX_SAMPLES = 20 * 60 * GHOST_HZ;   // 20 minutes
const CH = 9;                                          // x y z (cm), qx qy qz qw (/Q), s (cm), speedPct (/100)
const Q = 32767;
const ID_RE = /^[a-z0-9][a-z0-9_-]{0,31}$/;

export const ghostKey = (track, laps = 0, fullLaps = 0) => GHOST_KEY_PREFIX + track +
  (laps > 0 && fullLaps > 0 && laps !== fullLaps ? '.l' + (laps | 0) : '');

/** A recorder for one run: tick(raceTimeS, st) every sim step while racing, lap(cumulativeS) on each lap line. */
export function createGhostRecorder({ track, laps, hz = GHOST_HZ } = {}) {
  const rows = [], splits = [];
  let next = 0, last = null;
  return {
    get count() { return rows.length / CH; },
    tick(t, st) {
      if (!(t >= next - 1e-6) || rows.length / CH >= GHOST_MAX_SAMPLES) return false;
      let qx = st[S.quatX], qy = st[S.quatY], qz = st[S.quatZ], qw = st[S.quatW];
      if (last && qx * last[0] + qy * last[1] + qz * last[2] + qw * last[3] < 0) { qx = -qx; qy = -qy; qz = -qz; qw = -qw; }
      last = [qx, qy, qz, qw];
      rows.push(Math.round(st[S.posX] * 100), Math.round(st[S.posY] * 100), Math.round(st[S.posZ] * 100),
        Math.round(qx * Q), Math.round(qy * Q), Math.round(qz * Q), Math.round(qw * Q),
        Math.round(st[S.s] * 100), Math.round(Math.max(0, Math.min(2, st[S.speedPct])) * 100));
      next = rows.length / CH / hz;
      return true;
    },
    lap(cumS) { if (cumS > 0) splits.push(cumS); },
    /** @returns the ghost ({ v, track, laps, timeS, hz, splits, n, rows }) or null with too little to draw */
    finish(timeS) {
      const n = rows.length / CH;
      if (!(timeS > 0) || n < 2) return null;
      return { v: GHOST_V, track, laps, timeS, hz, splits: splits.slice(), n, rows: Int32Array.from(rows) };
    },
  };
}

// ---- encode / decode ----------------------------------------------------------------------------
function pushVar(out, v) {
  let z = v < 0 ? -2 * v - 1 : 2 * v;            // zigzag
  while (z >= 128) { out.push((z % 128) | 128); z = Math.floor(z / 128); }
  out.push(z);
}
function toB64(bytes) {
  let s = '';
  for (let i = 0; i < bytes.length; i += 0x8000) s += String.fromCharCode.apply(null, bytes.slice(i, i + 0x8000));
  return btoa(s);
}

/** @returns {string} a compact JSON string */
export function encodeGhost(g) {
  const bytes = [];
  for (let c = 0; c < CH; c++) {
    let p1 = 0, p2 = 0;
    for (let i = 0; i < g.n; i++) {
      const v = g.rows[i * CH + c], pred = i === 0 ? 0 : i === 1 ? p1 : 2 * p1 - p2;
      pushVar(bytes, v - pred);
      p2 = p1; p1 = v;
    }
  }
  return JSON.stringify({ v: GHOST_V, track: g.track, laps: g.laps, timeS: round3(g.timeS), hz: g.hz,
    splits: g.splits.map(round3), n: g.n, d: toB64(bytes) });
}
const round3 = (x) => Math.round(x * 1000) / 1000;

/** @returns the ghost, or null for anything malformed */
export function decodeGhost(str) {
  try {
    const o = JSON.parse(str);
    if (!o || o.v !== GHOST_V || !ID_RE.test(o.track) || !(o.timeS > 0) || !(o.hz > 0 && o.hz <= 60)) return null;
    const n = o.n | 0;
    if (n < 2 || n > GHOST_MAX_SAMPLES || !(o.laps >= 1) || typeof o.d !== 'string') return null;
    const bin = atob(o.d), rows = new Int32Array(n * CH);
    let k = 0;
    for (let c = 0; c < CH; c++) {
      let p1 = 0, p2 = 0;
      for (let i = 0; i < n; i++) {
        let z = 0, mul = 1, b;
        do { if (k >= bin.length) return null; b = bin.charCodeAt(k++); z += (b & 127) * mul; mul *= 128; } while (b & 128);
        const d = z % 2 ? -(z + 1) / 2 : z / 2, pred = i === 0 ? 0 : i === 1 ? p1 : 2 * p1 - p2, v = pred + d;
        rows[i * CH + c] = v; p2 = p1; p1 = v;
      }
    }
    if (k !== bin.length) return null;
    const splits = Array.isArray(o.splits) ? o.splits.filter((x) => x > 0).slice(0, 64) : [];
    return { v: GHOST_V, track: o.track, laps: o.laps | 0, timeS: o.timeS, hz: o.hz, splits, n, rows };
  } catch (e) { return null; }
}

// ---- playback -----------------------------------------------------------------------------------
/** The ghost at race time t: { x, y, z, qx, qy, qz, qw, s, speedPct, done } (positions in metres). */
export function sample(g, t, out = {}) {
  const f = Math.max(0, Math.min(g.n - 1, t * g.hz)), i = Math.min(g.n - 2, Math.floor(f)), u = f - i;
  const a = i * CH, b = a + CH, r = g.rows, L = (c) => r[a + c] + (r[b + c] - r[a + c]) * u;
  out.x = L(0) / 100; out.y = L(1) / 100; out.z = L(2) / 100;
  let qx = L(3), qy = L(4), qz = L(5), qw = L(6);
  const m = Math.hypot(qx, qy, qz, qw) || 1;
  if (m < 1e-6) { qx = qy = qz = 0; qw = 1; }
  out.qx = qx / m; out.qy = qy / m; out.qz = qz / m; out.qw = m < 1e-6 ? 1 : qw / m;
  // s wraps at the lap line: no lerp across the wrap
  out.s = (Math.abs(r[b + 7] - r[a + 7]) > 50000 ? r[a + 7] : L(7)) / 100;
  out.speedPct = L(8) / 100;
  out.done = t > g.timeS;
  return out;
}

/** Writes a sample into a kart state (sim/state.js layout) for the renderer. */
export function toState(p, st) {
  st[S.posX] = p.x; st[S.posY] = p.y; st[S.posZ] = p.z;
  st[S.quatX] = p.qx; st[S.quatY] = p.qy; st[S.quatZ] = p.qz; st[S.quatW] = p.qw;
  st[S.s] = p.s; st[S.speedPct] = p.speedPct; st[S.grounded] = 1;
  return st;
}

/** The gap at lap line `lap` (1 based): your cumulative time minus the ghost's, or null without a split. */
export function lapGap(g, lap, cumS) {
  const at = g && g.splits[lap - 1];
  return at > 0 && cumS > 0 ? cumS - at : null;
}

/** "+0.42" / "-1.07" */
export const formatGap = (d) => (d < 0 ? '-' : '+') + Math.abs(d).toFixed(2);

// ---- storage ------------------------------------------------------------------------------------
export function loadGhost(track, storage = defaultStorage(), laps = 0, fullLaps = 0) {
  try { const s = storage && storage.getItem(ghostKey(track, laps, fullLaps)); return s ? decodeGhost(s) : null; } catch (e) { return null; }
}

/** Keeps only the best finished trial per course (and laps). @returns {boolean} true when written */
export function saveBestGhost(g, storage = defaultStorage(), fullLaps = 0) {
  if (!g || !ID_RE.test(g.track) || !(g.timeS > 0)) return false;
  try {
    if (!storage) return false;
    const old = loadGhost(g.track, storage, g.laps, fullLaps);
    if (old && old.laps === g.laps && old.timeS <= g.timeS) return false;
    const str = encodeGhost(g);
    if (str.length > GHOST_MAX_CHARS) return false;
    storage.setItem(ghostKey(g.track, g.laps, fullLaps), str);
    return true;
  } catch (e) { return false; }
}

function defaultStorage() { try { return globalThis.localStorage || null; } catch (e) { return null; } }
