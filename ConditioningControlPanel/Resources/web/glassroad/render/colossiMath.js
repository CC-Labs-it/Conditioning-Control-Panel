/* render/colossiMath.js - the pure half of the WORLD lane (no three.js, so node --test runs it):
 * hero colossus placement against the road, the scatter no-clip rule, the shadow frustum fit and the
 * phone tier's projected capsule shadows.
 *
 * A body is modelled as a capsule: its axis runs from the base B along D for len metres (the model's
 * +Y, base-centre origin per art/ART.md) and r is the model's widest half extent times its scale, so
 * the test is conservative for every rotation about the axis.
 *
 * The road tube: for every road sample (every stepM metres along s, each stretch on its own, so a
 * body over one stretch is also checked against a stretch that passes under or beside it) a body
 * point clashes when it sits within w/2 + sideM + r across, and between underM + r below and
 * clearM + r above the surface. clearM (14 m) keeps every overhead body high over the karts and the
 * chase camera; sideM keeps standing bodies off the road edge so they never wall the next turn.
 */
import { LOOK } from './look.js';

/** WORLD lane numbers; render/look.js `LOOK.world` overrides any key (same names). */
export const WORLD_DEFAULTS = Object.freeze({
  // colossi: clearance rules and the fitter
  clearM: 14, sideM: 10, underM: 8, stepM: 4, axisStepM: 5, fitStepM: 3, fitTries: 50, fitPadM: 3,colossusGapM: 10, colossusSpinRadps: 0.012,
  // scatter (polish 3, owner "i cannot see any model"): a dense corridor the chase camera sees, count per
  // tier, sizes as model heights in metres (scatterKinds), the gap off the road edge (biased near), heights
  // beside the road, a share crossing overhead, and the no-clip margins
  scatter: 70, scatterByTier: Object.freeze({ high: 70, mid: 45, phone: 25 }), scatterScaleMul: 1, scatterSpinMul: 0.5,
  scatterSideM: [10, 120], scatterSideBias: 1.6, scatterUpM: [-12, 55], scatterOverShare: 0.2, scatterOverUpM: [4, 34],
  scatterMarginM: 6, scatterAirM: 16, scatterGapM: 4, scatterTries: 30,
  scatterUpright: Object.freeze(['chess-', 'slot-cabinet']), scatterTiltRad: 0.3,
  // poses: cards, watches and discs face a camera scatterFaceLeadM back down the road and swing about it;
  // upright pieces lean their top toward that camera (more when they float above the eye) and turn slowly
  scatterFaceLeadM: 60, scatterEyeM: 3, scatterSwingRad: 0.55, scatterTopTilt: 0.5, scatterLowTilt: 0.15,
  scatterKinds: Object.freeze([
    { id: 'card-drop', w: 3, h: [10, 20], spin: 0.35, pose: 'face' }, { id: 'card-lens', w: 3, h: [10, 20], spin: 0.35, pose: 'face' },
    { id: 'card-orbit', w: 3, h: [10, 20], spin: 0.35, pose: 'face' }, { id: 'card-petal', w: 3, h: [10, 20], spin: 0.35, pose: 'face' },
    // chess: only the silhouettes that read as chess at once (king, queen, knight, rook); pawns and bishops
    // read as upright shafts from the road, so they stay out of the scatter (polish 3 review)
    { id: 'chess-king-lod0', w: 2.2, h: [16, 30], spin: 0.2, pose: 'upright' }, { id: 'chess-queen-lod0', w: 2, h: [15, 28], spin: 0.2, pose: 'upright' },
    { id: 'chess-knight-lod0', w: 2.4, h: [12, 24], spin: 0.2, pose: 'upright' }, { id: 'chess-rook-lod0', w: 2, h: [12, 22], spin: 0.2, pose: 'upright' },
    { id: 'pocket-watch', w: 2.4, h: [9, 16], spin: 0.3, pose: 'face' }, { id: 'spiral-disc', w: 2, h: [9, 16], spin: 0.45, pose: 'face' },
    { id: 'slot-cabinet-lod0', w: 1.4, h: [10, 16], spin: 0.1, pose: 'upright' }, { id: 'bubble', w: 1, h: [6, 12], spin: 0.15, pose: 'tumble' },
  ]),
  // placement corridors (BRIEF-POLISH3 fixed contract, lap fraction f = s / length): spiral gates and the
  // media tunnel keep a 40 m window (half 20 m) clear, each billboard a 15 m window (half 7.5 m)
  corridorGates: Object.freeze([0.14, 0.31, 0.47, 0.69, 0.86, 0.97]), corridorGateHalfM: 20,
  corridorTunnel: Object.freeze([0.55, 0.61]), corridorTunnelPadM: 20,
  corridorBoards: Object.freeze([0.06, 0.2, 0.25, 0.38, 0.42, 0.52, 0.64, 0.76, 0.8, 0.92]), corridorBoardHalfM: 7.5,
  // soft shading (materials/colossus.js): wrap, floor, rim, sheen, aerial haze (polish 3: full colour inside
  // hazeNearM, no low haze) and the prop tint: a saturated hue over the paint's luminance, mixed in whole
  // (tintMix 1) with a high floor so the sculpted paint never shows through as a skin tone
  wrap: 0.45, shadeFloor: 0.42, shadeTint: '#6a4f9c', rimGain: 0.5, rimPow: 2.6, specGain: 0.45, specPow: 28,
  hazeM: 900, hazeMax: 0.4, hazeNearM: 300, hazeFarM: 1600, hazeLowM: 160, hazeLowMax: 0,
  tintMix: 1, tintFloor: 0.55, tintGain: 0.7, propRimGain: 1.25, propRimPow: 2.2, propRimWhite: 0.45,
  colossusRimGain: 1.1,
  // chess pieces never wear a warm hue (red, orange, gold, tan, pink read as skin): hues inside chessWarmDeg
  // (from, to, wrapping through 0) are skipped; every chessGlassEvery-th piece is white glass (0: none)
  chessWarmDeg: Object.freeze([315, 75]), chessGlassEvery: 0, chessGlass: '#eef2ff',
  // shadows (render/shadows.js): per tier map size and half extent, frustum lead, light distance
  shadowMap: Object.freeze({ high: 2048, mid: 1024 }), shadowHalfM: Object.freeze({ high: 95, mid: 80 }),
  shadowAheadM: 30, shadowDistM: 900, shadowFarM: 1900, shadowRadius: 20, shadowBlurSamples: 12, shadowBias: -0.0001, shadowNormalBias: 0.03,
  shadowOpacity: 0.5, shadowColour: '#2a1450', scatterCasts: true,
  // contact blobs and the phone tier's projected colossus shadows
  blobR: 1.5, blobAlpha: 0.55, blobMapAlpha: 0.4, blobFadeUpM: 4, spotAlpha: 0.38, spotStepM: 1.5, spotCols: 8,
});

export function worldLook() {
  return { ...WORLD_DEFAULTS, ...((LOOK && LOOK.world) || {}) };
}

/** Scatter count for a tier name (high, mid, phone); an unknown tier gets the high count. */
export function scatterCount(tierName, W = WORLD_DEFAULTS) {
  const t = W.scatterByTier || {};
  return t[tierName] ?? t.high ?? W.scatter;
}

/**
 * Which placement corridor s falls in ('gate', 'tunnel', 'board') or null. padM widens every window
 * (a body's own reach along the road). Distances wrap round the lap.
 */
export function corridorHit(s, L, W = WORLD_DEFAULTS, padM = 0) {
  const d = (f) => { const x = (((s - f * L) % L) + L) % L; return Math.min(x, L - x); };
  for (const f of W.corridorGates || []) if (d(f) < W.corridorGateHalfM + padM) return 'gate';
  const tn = W.corridorTunnel;
  if (tn) {
    const a = tn[0] * L - W.corridorTunnelPadM - padM, b = tn[1] * L + W.corridorTunnelPadM + padM;
    const sw = ((s % L) + L) % L;
    if (sw > a && sw < b) return 'tunnel';
  }
  for (const f of W.corridorBoards || []) if (d(f) < W.corridorBoardHalfM + padM) return 'board';
  return null;
}

/** The lap minus every corridor: [[s0, s1], ...] in order, scanned every metre. */
export function freeSpans(L, W = WORLD_DEFAULTS) {
  const out = [];
  let a = null;
  for (let s = 0; s <= L; s += 1) {
    const ok = s < L && !corridorHit(s, L, W);
    if (ok && a === null) a = s;
    if (!ok && a !== null) { out.push([a, s]); a = null; }
  }
  return out;
}

/* ---- colour: render/spectrum.js (SPECTRUM lane) when it exists, else a local full-spectrum set ---- */
import * as SPEC from './spectrum.js';
/** Saturated fallback: magenta, pink and violet lead (about 40 percent), the rest of the wheel follows. */
export const PROP_FALLBACK = Object.freeze(['#ff2fb4', '#19e3ff', '#8a3dff', '#ffc928', '#ff4f9a', '#7dff3a',
  '#2f6bff', '#ff7a1a', '#c23dff', '#00d9a3', '#ff3040', '#ff5ce1']);

/** Any colour shape (css hex, 0xRRGGBB, [r, g, b] 0..1 or 0..255, { r, g, b }) to '#rrggbb', or null. */
export function hexOf(c) {
  const h2 = (v) => Math.max(0, Math.min(255, Math.round(v))).toString(16).padStart(2, '0');
  const rgb = (r, g, b) => { const k = Math.max(r, g, b) > 1 ? 1 : 255; return '#' + h2(r * k) + h2(g * k) + h2(b * k); };
  if (typeof c === 'string') {
    const t = c.trim().toLowerCase();
    if (t.length === 7 && t[0] === '#' && !Number.isNaN(parseInt(t.slice(1), 16))) return t;
    if (t.length === 4 && t[0] === '#' && !Number.isNaN(parseInt(t.slice(1), 16))) return '#' + [...t.slice(1)].map((x) => x + x).join('');
    return null;
  }
  if (typeof c === 'number' && Number.isFinite(c)) return '#' + ((c >>> 0) & 0xffffff).toString(16).padStart(6, '0');
  if (Array.isArray(c) && c.length >= 3) return rgb(c[0], c[1], c[2]);
  if (c && typeof c === 'object' && 'r' in c) return rgb(c.r, c.g, c.b);
  return null;
}

/** The colour of prop i in a section (palette id): render/spectrum.js propColour, else PROP_FALLBACK. */
export function propColour(i, section) {
  try {
    if (SPEC && typeof SPEC.propColour === 'function') {
      const c = hexOf(SPEC.propColour(i, section));
      if (c) return c;
    }
  } catch { /* fall back */ }
  const n = PROP_FALLBACK.length;
  return PROP_FALLBACK[((i % n) + n) % n];
}

/** True when a '#rrggbb' hue falls in the warm band chess pieces never wear (W.chessWarmDeg, wraps). */
export function isWarmHue(hex, W = WORLD_DEFAULTS) {
  const { h, s } = hueSat(hex), [a, b] = W.chessWarmDeg || WORLD_DEFAULTS.chessWarmDeg;
  if (s < 0.25) return false;
  return a > b ? (h >= a || h <= b) : (h >= a && h <= b);
}

/**
 * The colour of chess piece i in a section: the section's spectrum picks with every warm hue skipped,
 * every chessGlassEvery-th piece white glass. Never a skin, tan or flesh tone.
 */
export function chessColour(i, section, W = WORLD_DEFAULTS) {
  const n = Math.floor(Number(i) || 0), every = W.chessGlassEvery ?? WORLD_DEFAULTS.chessGlassEvery;
  if (every > 0 && ((n % every) + every) % every === every - 1) return W.chessGlass || WORLD_DEFAULTS.chessGlass;
  for (let k = 0; k < 40; k++) {
    const c = propColour(n + k * 7, section);
    if (!isWarmHue(c, W)) return c;
  }
  return '#19e3ff';
}

/** Hue (0..360), saturation and value (0..1) of a '#rrggbb' colour. */
export function hueSat(hex) {
  const n = parseInt(hex.slice(1), 16), r = ((n >> 16) & 255) / 255, g = ((n >> 8) & 255) / 255, b = (n & 255) / 255;
  const mx = Math.max(r, g, b), mn = Math.min(r, g, b), d = mx - mn;
  let h = 0;
  if (d > 0) h = mx === r ? ((g - b) / d) % 6 : mx === g ? (b - r) / d + 2 : (r - g) / d + 4;
  return { h: (h * 60 + 360) % 360, s: mx > 0 ? d / mx : 0, v: mx };
}

const sub = (a, b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
const add = (a, b) => [a[0] + b[0], a[1] + b[1], a[2] + b[2]];
const mul = (a, k) => [a[0] * k, a[1] * k, a[2] * k];
const dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const cross = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
const len = (a) => Math.hypot(a[0], a[1], a[2]);
const norm = (a) => { const l = len(a) || 1; return [a[0] / l, a[1] / l, a[2] / l]; };
const clamp01 = (x) => Math.min(1, Math.max(0, x));
export const V3 = Object.freeze({ sub, add, mul, dot, cross, len, norm });

/** Road samples every stepM metres: plain copies of frameAt, so callers can keep them. */
export function sampleTrack(track, stepM = 4) {
  const n = Math.max(8, Math.ceil(track.lengthM / stepM)), out = [];
  for (let i = 0; i < n; i++) {
    const s = (i * track.lengthM) / n, f = track.frameAt(s);
    out.push({ s, p: [...f.p], t: [...f.t], n: [...f.n], b: [...f.b], w: f.w });
  }
  out.stepM = track.lengthM / n;
  return out;
}

/** Point P in a sample's frame: a along, l lateral (+ right), h up from the surface. */
export function localOf(P, f) {
  const r = sub(P, f.p);
  return { a: dot(r, f.t), l: dot(r, f.b), h: dot(r, f.n) };
}

/**
 * Model size row -> capsule radius and scale for a target height. row: art manifest row
 * (sizeM [w, h, d], boundRadiusM). radius = the wider half extent across the axis.
 */
export function bodySize(row, heightM) {
  const sz = row && row.sizeM ? row.sizeM : [1, 1, 1];
  const scale = heightM / (sz[1] || 1);
  return { scale, r: Math.max(sz[0], sz[2]) / 2 * scale };
}

/**
 * A colossus spec (tracks/<id>.colossi.json) -> its axis.
 * kind 'arch': leans across the road; the axis passes over the road at s (lateral crossLatM) crossUpM
 * above the surface, the base on `side` (-1 left, +1 right), baseShare of the body below the crossing.
 * kind 'tower': stands beside; base offM across on `side`, upM along the road normal (negative = below).
 * leanDeg tilts from the road's up toward the road (negative leans away); yawDeg swings the lean
 * toward +t (diagonal). spinDeg turns the model about its own axis.
 */
export function axisOf(spec, frame, row) {
  const f = frame, side = spec.side < 0 ? -1 : 1;
  const { scale, r } = bodySize(row, spec.heightM);
  const lean = (spec.leanDeg || 0) * Math.PI / 180, yaw = (spec.yawDeg || 0) * Math.PI / 180;
  const across = mul(f.b, -side);
  const dirH = norm(add(mul(across, Math.cos(yaw)), mul(f.t, Math.sin(yaw))));
  const D = norm(add(mul(f.n, Math.cos(lean)), mul(dirH, Math.sin(lean))));
  const L = spec.heightM;
  let B;
  if (spec.kind === 'arch') {
    const C = add(add(f.p, mul(f.n, spec.crossUpM || 0)), mul(f.b, spec.crossLatM || 0));
    B = sub(C, mul(D, L * (spec.baseShare ?? 0.45)));
  } else {
    B = add(add(f.p, mul(f.b, side * (spec.offM || 0))), mul(f.n, spec.upM || 0));
  }
  // model basis: Y along D, X across it (from t, or b when D runs along t), then the spin
  const ref = Math.abs(dot(D, f.t)) > 0.9 ? f.b : f.t;
  let X = norm(cross(D, ref));
  let Z = cross(X, D);
  const sp = (spec.spinDeg || 0) * Math.PI / 180, c = Math.cos(sp), s = Math.sin(sp);
  [X, Z] = [add(mul(X, c), mul(Z, -s)), add(mul(X, s), mul(Z, c))];
  return { id: spec.id, B, D, len: L, r, scale, X, Y: D, Z, top: add(B, mul(D, L)) };
}

/**
 * Clearance of a capsule against every road sample.
 * Returns { ok, minAboveM, crosses }: minAboveM = the lowest body surface over the road span
 * (Infinity when nothing overhangs), crosses = the axis passes directly over the road.
 */
export function bodyClear(axis, samples, W = WORLD_DEFAULTS) {
  const stepA = W.axisStepM, nA = Math.max(1, Math.ceil(axis.len / stepA));
  const half = (samples.stepM || W.stepM) / 2;
  let ok = true, minAboveM = Infinity, crosses = false;
  for (let k = 0; k <= nA; k++) {
    const P = add(axis.B, mul(axis.D, (axis.len * k) / nA));
    for (const f of samples) {
      const q = localOf(P, f);
      if (Math.abs(q.a) > half + axis.r) continue;
      const span = f.w / 2 + axis.r;
      if (Math.abs(q.a) <= half && Math.abs(q.l) < f.w / 2 && q.h > 0) crosses = true;
      if (Math.abs(q.l) < span && q.h > 0) minAboveM = Math.min(minAboveM, q.h - axis.r);
      if (Math.abs(q.l) < span + W.sideM && q.h > -(axis.r + W.underM) && q.h < axis.r + W.clearM) ok = false;
    }
  }
  return { ok, minAboveM, crosses };
}

/** Closest distance between segments p0-p1 and q0-q1. */
export function segDist(p0, p1, q0, q1) {
  const d1 = sub(p1, p0), d2 = sub(q1, q0), r = sub(p0, q0);
  const a = dot(d1, d1), e = dot(d2, d2), f = dot(d2, r);
  let s, t;
  if (a < 1e-9 && e < 1e-9) return len(r);
  if (a < 1e-9) { s = 0; t = clamp01(f / e); }
  else {
    const c = dot(d1, r);
    if (e < 1e-9) { t = 0; s = clamp01(-c / a); }
    else {
      const b = dot(d1, d2), den = a * e - b * b;
      s = den > 1e-9 ? clamp01((b * f - c * e) / den) : 0;
      t = (b * s + f) / e;
      if (t < 0) { t = 0; s = clamp01(-c / a); } else if (t > 1) { t = 1; s = clamp01((b - c) / a); }
    }
  }
  return len(sub(add(p0, mul(d1, s)), add(q0, mul(d2, t))));
}

/** Two capsules (axisOf shapes, or { B, top, r } spheres with B = top) keep gap metres apart. */
export function capsulesApart(x, y, gap = 0) {
  return segDist(x.B, x.top, y.B, y.top) >= x.r + y.r + gap;
}

/**
 * Place one spec: an arch rises (crossUpM += fitStepM) and a tower steps out (offM += fitStepM)
 * until it clears the road and the bodies already placed. Returns the axis with fit info, or null.
 */
export function fitColossus(spec, track, row, samples, placed = [], W = WORLD_DEFAULTS) {
  const f = track.frameAt(((spec.s % track.lengthM) + track.lengthM) % track.lengthM);
  for (let k = 0; k <= W.fitTries; k++) {
    const sp = spec.kind === 'arch'
      ? { ...spec, crossUpM: (spec.crossUpM || 0) + k * W.fitStepM }
      : { ...spec, offM: (spec.offM || 0) + k * W.fitStepM };
    const ax = axisOf(sp, f, row);
    // fitted with a padded radius, so a finer sampling of the same layout still clears
    if (!bodyClear({ ...ax, r: ax.r + W.fitPadM }, samples, W).ok) continue;
    const c = bodyClear(ax, samples, W);
    if (placed.some((o) => !capsulesApart(ax, o, W.colossusGapM))) continue;
    return { ...ax, spec: sp, steps: k, minAboveM: c.minAboveM, crosses: c.crosses };
  }
  return null;
}

/** Every spec in order; rowOf(id) gives the art row (a missing row skips that colossus). */
export function placeColossi(specs, track, rowOf, W = WORLD_DEFAULTS, samples = sampleTrack(track, W.stepM)) {
  const placed = [];
  for (const spec of specs || []) {
    const row = rowOf(spec.id);
    if (!row) continue;
    const ax = fitColossus(spec, track, row, samples, placed, W);
    if (ax) placed.push(ax);
  }
  return placed;
}

/** The scatter no-clip rule for a sphere of radius R at P (the old tube test, every stretch). */
export function scatterClear(P, R, samples, W = WORLD_DEFAULTS) {
  return bodyClear({ B: P, D: [0, 1, 0], len: 0, r: R }, samples,
    { ...W, sideM: W.scatterMarginM, underM: W.scatterMarginM, clearM: W.scatterAirM }).ok;
}

/**
 * The body a scatter piece occupies, as a capsule (B, D, len, r, top). Upright and facing pieces turn
 * only about their own axis Y, so a capsule along Y with the widest half extent is exact for every
 * turn; a tumbling piece is a sphere of its bound radius round its origin.
 */
export function scatterBody(pose, p, Y, row, scale) {
  const sz = row && row.sizeM ? row.sizeM : [1, 1, 1];
  if (pose === 'tumble') {
    const r = ((row && row.boundRadiusM) || Math.max(...sz)) * scale;
    return { B: p, D: [0, 1, 0], len: 0, r, top: p };
  }
  const L = sz[1] * scale, r = Math.hypot(sz[0], sz[2]) / 2 * scale;
  return { B: p, D: Y, len: L, r, top: add(p, mul(Y, L)) };
}

/** Rotate unit v (perpendicular to unit axis a) about a by angle. */
function turnAbout(v, a, ang) {
  return add(mul(v, Math.cos(ang)), mul(cross(a, v), Math.sin(ang)));
}

/**
 * Model basis for a placed piece at turn angle ang: { X, Y, Z } (Y up the model, the model's front -Z
 * faces F). Facing pieces swing round their axis by scatterSwingRad * sin(ang) so the face never goes
 * edge on for long; upright pieces turn all the way round (a chess silhouette reads from every side).
 * Tumbling pieces return null (the renderer turns them by Euler angles).
 */
export function scatterBasis(x, ang = 0, W = WORLD_DEFAULTS) {
  if (x.pose === 'tumble') return null;
  const a = x.pose === 'face' ? W.scatterSwingRad * Math.sin(ang) : ang;
  const F = norm(turnAbout(x.F, x.Y, a));
  const Z = mul(F, -1), Y = x.Y, X = cross(Y, Z);
  return { X, Y, Z };
}

/**
 * Seeded floating scatter in the corridor the chase camera sees (polish 3). kinds: rows
 * ({ id, w, h: [min, max] model height in metres, spin, pose }) already filtered to loaded models;
 * rowOf(id) gives sizeM and boundRadiusM. One piece per stretch of length L / want, so the lap never
 * goes empty; pieces stay out of the placement corridors (spiral gates, the media tunnel, billboards).
 * A share floats over the road above scatterAirM; the rest hang beside it scatterSideM off the edge,
 * biased near. Facing pieces turn to a camera scatterFaceLeadM back down the road; upright pieces lean
 * their top toward it. avoid: placed colossi (capsules). sectionOf(s) names the palette for the colour.
 * Returns [{ k, p, R, scale, pose, Y, F, body, s, over, colour, rot, spin, ang, upright }].
 */
export function placeScatter(track, kinds, rowOf, seed, { want, avoid = [], W = WORLD_DEFAULTS, samples = sampleTrack(track, W.stepM), sectionOf = null } = {}) {
  if (!kinds.length) return [];
  let st = (seed >>> 0) % 2147483646 + 1;
  const rnd = (a, b) => { st = (st * 16807) % 2147483647; return a + (st / 2147483647) * (b - a); };
  const total = kinds.reduce((a, k) => a + k.w, 0);
  const pick = () => { let x = rnd(0, total); for (const k of kinds) { if ((x -= k.w) <= 0) return k; } return kinds[0]; };
  const n = want ?? W.scatter, placed = [], L = track.lengthM;
  const Wc = { ...W, sideM: W.scatterMarginM, underM: W.scatterMarginM, clearM: W.scatterAirM };
  // the free road (outside every corridor), so the strata spread over what is left
  const free = freeSpans(L, W), freeLen = free.reduce((a, [x, y]) => a + y - x, 0);
  const atFree = (u) => { let x = u * freeLen; for (const [a, b] of free) { if (x <= b - a) return a + x; x -= b - a; } return free.length ? free[free.length - 1][1] : u * L; };
  for (let i = 0; i < n; i++) {
    for (let tries = 0; tries < W.scatterTries; tries++) {
      const k = pick(), row = rowOf(k.id), sz = row.sizeM || [1, 1, 1];
      const h = rnd(k.h ? k.h[0] : 10, k.h ? k.h[1] : 20) * (W.scatterScaleMul || 1), scale = h / (sz[1] || 1);
      // the lateral reach scatterBody gives this pose (a tumbling piece is its bound sphere)
      const rLat = k.pose === 'tumble' ? (row.boundRadiusM || Math.max(...sz)) * scale : Math.hypot(sz[0], sz[2]) / 2 * scale;
      const s = atFree((i + rnd(0, 1)) / n);
      if (corridorHit(s, L, W, rLat)) continue;
      const f = track.frameAt(s), over = rnd(0, 1) < W.scatterOverShare;
      let lat, up, side;
      if (over) {
        side = 0; lat = rnd(-0.5, 0.5) * f.w;
        up = W.scatterAirM + rLat + W.scatterMarginM + rnd(W.scatterOverUpM[0], W.scatterOverUpM[1]);
      } else {
        side = rnd(0, 1) < 0.5 ? -1 : 1;
        const gap = W.scatterSideM[0] + (W.scatterSideM[1] - W.scatterSideM[0]) * Math.pow(rnd(0, 1), W.scatterSideBias);
        lat = side * (f.w / 2 + gap + rLat);
        up = rnd(W.scatterUpM[0], W.scatterUpM[1]);
      }
      const p = [0, 1, 2].map((j) => f.p[j] + f.b[j] * lat + f.n[j] * up);
      // the camera a lead back down the road, at eye height: faces turn to it, tops lean toward it
      const cf = track.frameAt((((s - W.scatterFaceLeadM) % L) + L) % L);
      const eye = add(cf.p, mul(cf.n, W.scatterEyeM));
      const mid = add(p, mul(f.n, h / 2));
      const toCam = norm(sub(eye, mid));
      let Y;
      if (k.pose === 'upright') {
        const above = up + h / 2 > W.scatterEyeM + 4;
        Y = norm(add(f.n, mul(toCam, above ? W.scatterTopTilt : W.scatterLowTilt)));
      } else {
        // a facing piece stands up the road normal, its face square to the camera ray
        const flat = sub(toCam, mul(f.n, dot(toCam, f.n)));
        Y = norm(sub(f.n, mul(norm(flat), dot(toCam, f.n) * 0.6)));
      }
      let F = sub(toCam, mul(Y, dot(toCam, Y)));
      F = len(F) < 1e-4 ? norm(cross(Y, f.b)) : norm(F);
      const body = scatterBody(k.pose, p, Y, row, scale);
      if (!bodyClear(body, samples, Wc).ok) continue;
      if (placed.some((x) => !capsulesApart(x.body, body, W.scatterGapM))) continue;
      if (avoid.some((c) => !capsulesApart(body, c, W.scatterMarginM))) continue;
      const R = (row.boundRadiusM || 1) * scale;
      const tl = W.scatterTiltRad;
      const rot = [rnd(0, 6.28), rnd(0, 6.28), rnd(0, 6.28)];
      const section = sectionOf ? sectionOf(s) : null;
      placed.push({ k, p, R, scale, h, pose: k.pose || 'tumble', Y, F, body, s, over, side, rot, tilt: tl,
        upright: k.pose === 'upright', spin: rnd(-1, 1) * k.spin, ang: rnd(0, 6.28),
        colour: k.id.startsWith('chess-') ? chessColour(placed.length, section, W) : propColour(placed.length, section) });
      break;
    }
  }
  return placed;
}

/**
 * Directional shadow frustum that follows the player. centre: the point to cover (player plus a
 * lead along the view); dir: toward the lamp. The centre is snapped to whole shadow texels in the
 * light's own x / y (the basis three's lookAt builds with up = +Y), so a moving player never makes
 * shadow edges crawl. Returns { target, pos, right, up, texel }.
 */
export function shadowFit(centre, dir, halfM, mapSize, distM) {
  const L = norm(dir);
  let right = cross([0, 1, 0], L);
  right = len(right) < 1e-6 ? [1, 0, 0] : norm(right);
  const up = cross(L, right), texel = (2 * halfM) / mapSize;
  const cx = Math.round(dot(centre, right) / texel) * texel, cy = Math.round(dot(centre, up) / texel) * texel, cz = dot(centre, L);
  const target = add(add(mul(right, cx), mul(up, cy)), mul(L, cz));
  return { target, pos: add(target, mul(L, distM)), right, up, texel };
}

/**
 * Shadow mode from the tier's Q.shadows ('map' | 'blob' | 'off', PERF lane) and the options switch.
 * Before Q.shadows exists: phone = 'blob', anything else 'map'. Options off keeps the cheap blobs.
 */
export function shadowMode(qShadows, tierName, enabled = true) {
  const tier = qShadows || (tierName === 'phone' ? 'blob' : 'map');
  if (tier === 'off') return 'off';
  return enabled ? tier : 'blob';
}

/** Map size and half extent for a tier (phone gets the mid size, used only if it steps up). */
export function shadowSize(tierName, W = WORLD_DEFAULTS) {
  const t = tierName === 'high' ? 'high' : 'mid';
  return { map: W.shadowMap[t], halfM: W.shadowHalfM[t] };
}

/** True when world point P falls inside the fitted ortho box (half extent halfM, depth 0..farM). */
export function inShadowBox(P, fit, halfM, farM) {
  const r = sub(P, fit.pos), L = sub(fit.target, fit.pos), d = len(L), Ln = mul(L, 1 / d);
  const z = dot(r, Ln);
  return Math.abs(dot(r, fit.right)) <= halfM && Math.abs(dot(r, fit.up)) <= halfM && z >= 0 && z <= farM;
}

/**
 * Phone tier: how much of a capsule's shadow lands on road point X (0 none, 1 core). The ray from X
 * toward the lamp is tested against the axis; the edge is soft over the outer 35% of the radius.
 */
export function capsuleShade(X, dir, axis) {
  const Ln = norm(dir), far = add(X, mul(Ln, 4000));
  const d = segDist(X, far, axis.B, axis.top);
  const t = clamp01((d - axis.r * 0.65) / (axis.r * 0.5));
  return 1 - t * t * (3 - 2 * t);
}

/**
 * s ranges (on the samples) where a capsule's shadow reaches the road: [[s0, s1], ...] merged,
 * each padded by one sample. Used to lay the phone tier's shadow strips.
 */
export function shadeRanges(axis, dir, samples) {
  const hits = [];
  for (const f of samples) {
    let any = false;
    for (const x of [-0.5, 0, 0.5]) {
      if (capsuleShade(add(f.p, mul(f.b, x * f.w)), dir, axis) > 0) { any = true; break; }
    }
    if (any) hits.push(f.s);
  }
  const step = samples.stepM || 4, out = [];
  for (const s of hits) {
    const last = out[out.length - 1];
    if (last && s - last[1] <= step * 1.5) last[1] = s;
    else out.push([s, s]);
  }
  return out.map(([a, b]) => [a - step, b + step]);
}
