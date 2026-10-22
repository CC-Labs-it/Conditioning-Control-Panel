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
  // scatter (owner 2026-10-22: colossal, fewer): count, size multiplier on artScatterKinds, ranges, no-clip margins
  scatter: 34, scatterScaleMul: 3, scatterSpinMul: 0.5, scatterSideM: [16, 160], scatterUpM: [-90, 110], scatterMarginM: 6, scatterAirM: 16,
  scatterUpright: Object.freeze(['chess-', 'slot-cabinet']), scatterTiltRad: 0.3,
  // soft shading (materials/colossus.js): wrap, floor, rim, sheen, aerial haze
  wrap: 0.45, shadeFloor: 0.3, shadeTint: '#6a4f9c', rimGain: 0.5, rimPow: 2.6, specGain: 0.45, specPow: 28,
  hazeM: 900, hazeMax: 0.55, hazeLowM: 160, hazeLowMax: 0.3,
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
 * Seeded floating scatter. kinds: LOOK.artScatterKinds rows ({ id, w, scale, spin }) already
 * filtered to loaded models; rowOf(id) gives boundRadiusM. Sizes are scaled by scatterScaleMul;
 * avoid: placed colossi (capsules). Returns [{ k, p, R, scale, rot, spin }].
 */
export function placeScatter(track, kinds, rowOf, seed, { want, avoid = [], W = WORLD_DEFAULTS, samples = sampleTrack(track, W.stepM) } = {}) {
  if (!kinds.length) return [];
  let st = (seed >>> 0) % 2147483646 + 1;
  const rnd = (a, b) => { st = (st * 16807) % 2147483647; return a + (st / 2147483647) * (b - a); };
  const total = kinds.reduce((a, k) => a + k.w, 0);
  const pick = () => { let x = rnd(0, total); for (const k of kinds) { if ((x -= k.w) <= 0) return k; } return kinds[0]; };
  const n = want ?? W.scatter, placed = [], L = track.lengthM, mulS = W.scatterScaleMul;
  for (let i = 0; i < n * 10 && placed.length < n; i++) {
    const k = pick(), scale = rnd(k.scale[0], k.scale[1]) * mulS, R = rowOf(k.id).boundRadiusM * scale;
    const f = track.frameAt(rnd(0, L)), side = rnd(0, 1) < 0.5 ? -1 : 1;
    const off = f.w / 2 + R + rnd(W.scatterSideM[0], W.scatterSideM[1]), up = rnd(W.scatterUpM[0], W.scatterUpM[1]);
    const p = [0, 1, 2].map((j) => f.p[j] + f.b[j] * side * off + f.n[j] * up);
    if (!scatterClear(p, R, samples, W)) continue;
    if (placed.some((x) => len(sub(x.p, p)) < x.R + R)) continue;
    const ball = { B: p, top: p, r: R };
    if (avoid.some((c) => !capsulesApart(ball, c, W.scatterMarginM))) continue;
    // chess pieces and cabinets float upright (a tumbling piece loses its silhouette); the rest tumble
    const upright = W.scatterUpright.some((pre) => k.id.startsWith(pre)), tl = W.scatterTiltRad;
    const rot = upright ? [rnd(-tl, tl), rnd(0, 6.28), rnd(-tl, tl)] : [rnd(0, 6.28), rnd(0, 6.28), rnd(0, 6.28)];
    placed.push({ k, p, R, scale, rot, upright, spin: rnd(-1, 1) * k.spin });
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
