/* render/spiralMath.js - the spiral gates' numbers and maths (polish 3, SPIRALS lane). No three.js.
 *
 * Every spiral comes from the Loom (owner law): the real arcademy/engine/loom/loomField.js and its
 * seeded "surprise me" (seeded.js, the Loom's own randomParams2 with the dice handed in), imported by
 * relative path, never forked. loadLoom() is the ONE place that names that path; a page served without
 * the arcademy tree beside it (dev/serve.mjs roots at glassroad/) gets null and the gates draw from the
 * stored params as written.
 *
 * What this file adds on top of the Loom is only the 3D placement. A gate is the Loom field stood up
 * across the road: field x is the road's right (frame b), field y (down on the Loom canvas) is minus the
 * road's up (frame n), so a driver looking down the road sees the field exactly as the Loom draws it,
 * clockwise for direction 1 and reading inward. Particles are sampled ON the covered bands of the field
 * at phase 0 (the band rule of the Loom shader, mirrored by loomCover for the tests) and the GPU turns
 * them with the Loom's own loop timing: the angular styles rotate rigidly by the layer rotation, the
 * tunnel style's bands ride the radius. Pulse zoom, wobble and hue cycles follow the Loom phase.
 */

const TAU = Math.PI * 2;
const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));

import * as SPECTRUM_MOD from './spectrum.js';

/** Lane look table (render numbers live here, never in logic). Gentle halves turn and twinkle. */
export const SPIRAL_LOOK = Object.freeze({
  radiusM: 20, upM: 3.5, holeFrac: 0.06, jitterM: 1.4,
  perGate: Object.freeze({ high: 8000, mid: 4000, phone: 2000 }),
  farShare: 0.25, activeM: 400, drawM: 900,
  // review (polish 3): the particles sit ON the Loom arms (armFill of each covered band, centred), thin and
  // bright, with almost no halo between them, so a dark gap shows between arms and the spiral reads from 80 m
  sizeM: Object.freeze([0.2, 0.5]), sizeRimGain: 0.25, armFill: 0.55,
  glitterShare: 0.08, glitterSize: 1.5, haloShare: 0.02, haloBand: 0.04,
  alpha: 0.75, layer2Alpha: 0.85, haloAlpha: 0.35, gain: 1.5, glitterGain: 2.0, whiteMix: 0.3, sliceGapM: 8, sliceAlpha: 0.3, radialFloor: 0.35,
  twinkleHz: Object.freeze([0.5, 2.2]), twinkleAmp: 0.7,
  gentleTwinkleMul: 0.5, gentleTurnMul: 0.5, gentlePartMul: 0.6, gentleAlphaMul: 0.75,
  turnMul: 1, nearM: 2.5, nearSpanM: 4, farFadeM: 900,
  // the road window: particles over the road corridor (road half width + corridorSideM across, up to
  // corridorUpM above the surface, soft over corridorSoftM) fade to corridorAlpha, so the road and the next
  // turn stay readable through the gate from the kart's eye
  corridorSideM: 1.5, corridorUpM: 7, corridorSoftM: 3, corridorAlpha: 0.1,
  layer2Share: 0.35,
  slots: 4, partM: 8, partInnerM: 2.5, partRadiusM: 10, partLeadM: 22, partTrailM: 30, swirlShare: 0.7,
  burst: Object.freeze({
    count: Object.freeze({ high: 420, mid: 260, phone: 140 }), lifeS: 1.1, riseS: 0.06, r0M: Object.freeze([0.5, 1.6]),
    speedMps: Object.freeze([2.5, 7]), sizeM: Object.freeze([0.03, 0.09]), aheadM: 1.2, refusedMul: 0.4, gentleCountMul: 0.5,
    alpha: 0.9, gain: 1.8,
  }),
  fallbackLoopMs: 1800,
  /** Full spectrum threads for seeded gates when render/spectrum.js is missing. */
  palette: Object.freeze(['#ff2fb3', '#c13cff', '#7a3cff', '#00e5ff', '#00ffa8', '#9dff2f', '#ffd21f', '#ff7a1a', '#ff2f4f', '#2f6bff']),
});

/** The Loom modules, or null when the arcademy tree is not served beside glassroad. */
export async function loadLoom() {
  try {
    const field = await import('../../arcademy/engine/loom/loomField.js');
    let seeded = null;
    try { seeded = await import('../../arcademy/engine/loom/seeded.js'); } catch (e) { seeded = null; }
    return { field, seeded };
  } catch (e) {
    return null;
  }
}

/** render/spectrum.js, the one colour system (static import since the polish 3 integration). */
export async function loadSpectrum() { return SPECTRUM_MOD; }

/** Threads for a seeded gate in a section: spectrum.js SPECTRUM[section], else its propColour, else the lane palette. */
export function paletteFor(section, spectrum, L = SPIRAL_LOOK) {
  const out = [];
  try {
    const set = spectrum && spectrum.SPECTRUM && spectrum.SPECTRUM[section];
    if (Array.isArray(set)) for (const c of set) { const h = toHex(c, null); if (h && !out.includes(h)) out.push(h); }
    else if (spectrum && typeof spectrum.propColour === 'function') {
      for (let i = 0; i < 10; i++) { const h = toHex(spectrum.propColour(i, section), null); if (h && !out.includes(h)) out.push(h); }
    }
  } catch (e) { out.length = 0; }
  return out.length >= 3 ? out : L.palette.slice();
}

/** Seeded 0..1 stream (mulberry32): the same seed always weaves the same gate. */
export function makeRng(seed = 1) {
  let a = (seed >>> 0) || 1;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

/** Coerce a colour (hex string, 0xRRGGBB, [r,g,b] 0..1, {r,g,b}) to '#rrggbb'. */
export function toHex(c, fb = '#ff69b4') {
  if (typeof c === 'string' && /^#[0-9a-f]{6}$/i.test(c)) return c.toLowerCase();
  let r, g, b;
  if (typeof c === 'number' && c >= 0) { r = (c >> 16) & 255; g = (c >> 8) & 255; b = c & 255; }
  else if (Array.isArray(c) && c.length >= 3) [r, g, b] = c.map((v) => Math.round(clamp(v, 0, 1) * 255));
  else if (c && typeof c === 'object' && 'r' in c) [r, g, b] = [c.r, c.g, c.b].map((v) => Math.round(clamp(v, 0, 1) * 255));
  else return fb;
  return '#' + [r, g, b].map((v) => v.toString(16).padStart(2, '0')).join('');
}

export function hexRgb(h) {
  const n = parseInt(toHex(h).slice(1), 16);
  return [((n >> 16) & 255) / 255, ((n >> 8) & 255) / 255, (n & 255) / 255];
}

/** The Loom params for one gate: its stored Loom preset, or the seeded surprise-me with the gate's seed. */
export function gateParams(gate, loom, palette = SPIRAL_LOOK.palette) {
  const field = loom && loom.field, seeded = loom && loom.seeded;
  let q;
  if (gate && gate.seed != null && seeded && seeded.seededParams2) {
    q = seeded.seededParams2(makeRng(gate.seed), { palette: palette.map((c) => toHex(c)), centerpiece: false });
  } else {
    const raw = (gate && gate.seed != null && !gate.loom) ? { layer: { colors: palette.slice(0, 3).map((c) => toHex(c)) } } : (gate && gate.loom) || {};
    q = field && field.normalizeParams2 ? field.normalizeParams2(raw) : storedParams(raw);
  }
  // handedness law: both layers turn clockwise on screen and read inward
  q = { ...q, layer: { ...q.layer, direction: 1 }, layer2: { ...q.layer2, direction: 1 } };
  return q;
}

/** No Loom on the page: the stored params as written, with only the shape the GPU path reads. */
function storedParams(raw) {
  const lay = (l, on) => ({
    arms: clamp(Math.round((l && l.arms) || 4), 1, 12), turns: clamp((l && l.turns) || 2, 0.5, 6), duty: clamp((l && l.duty) || 0.5, 0.2, 0.8),
    style: (l && l.style) || 'log', direction: l && l.direction === -1 ? -1 : 1,
    colors: (l && Array.isArray(l.colors) && l.colors.length ? l.colors : ['#ff69b4']).slice(0, 6).map((c) => toHex(c)),
    bandMode: l && l.bandMode === 'gradient' ? 'gradient' : 'hard', speedMul: clamp(Math.round((l && l.speedMul) || 1), 1, 3), enabled: on,
  });
  return {
    schema: 2, speed: clamp(Math.round(raw.speed || 3), 1, 5), layer: lay(raw.layer, true),
    layer2: lay(raw.layer2, !!(raw.layer2 && raw.layer2.enabled)), glow: clamp(raw.glow || 0, 0, 1),
    pulse: { amp: clamp((raw.pulse && raw.pulse.amp) || 0, 0, 0.25), cycles: (raw.pulse && raw.pulse.cycles) || 1 },
    wobble: { amp: clamp((raw.wobble && raw.wobble.amp) || 0, 0, 0.35), freq: (raw.wobble && raw.wobble.freq) || 3, cycles: (raw.wobble && raw.wobble.cycles) || 1 },
    hueCycles: clamp(Math.round(raw.hueCycles || 0), 0, 2),
  };
}

/** One seamless Loom loop in seconds (the Loom's own loopMs2 when it is on the page). */
export function loopS(params, loom) {
  const f = loom && loom.field;
  return (f && f.loopMs2 ? f.loopMs2(params) : SPIRAL_LOOK.fallbackLoopMs) / 1000;
}

/** The Loom span rule: the smallest rotation that maps a layer onto itself (1 to 6 threads). */
export function spanRad(layer) {
  const n = Math.max(1, Math.min(layer.colors.length, 6));
  return (TAU / layer.arms) * (layer.arms % n === 0 ? n : layer.arms);
}

/** Layer rotation at loop phase 0..1 (the Loom's layerRotationRad). */
export function layerRot(layer, phase) {
  return layer.direction * phase * spanRad(layer) * layer.speedMul;
}

export const STYLE_INDEX = Object.freeze({ log: 0, arch: 1, ribbon: 2, golden: 3, tunnel: 4, petal: 5 });
const GOLD_K = 1.9459101;

/** Field radius 0..1 -> band parameter t, per style (the Loom shader's mapping). */
export function tOfR(rc, style) {
  const r = clamp(rc, 0, 1), s = STYLE_INDEX[style] ?? 0;
  if (s === 0) return Math.pow(r, 0.5714286);
  if (s === 1) return r;
  if (s === 2) return 0.5 - Math.sin(Math.asin(1 - 2 * r) / 3);
  return Math.log(1 + r * 6) / GOLD_K;
}

/** Inverse of tOfR. */
export function rOfT(t, style) {
  const x = clamp(t, 0, 1), s = STYLE_INDEX[style] ?? 0;
  if (s === 0) return Math.pow(x, 1.75);
  if (s === 1) return x;
  if (s === 2) return (1 - Math.sin(3 * Math.asin(0.5 - x))) / 2;
  return (Math.exp(x * GOLD_K) - 1) / 6;
}

/** Tunnel helix shear snapped to whole colour cycles (the Loom shader's rule). */
export function helixOf(layer) {
  const count = layer.colors.length;
  return Math.floor((layer.turns - 0.5) / count + 0.5) * count;
}

const fract = (x) => x - Math.floor(x);

/**
 * The Loom band rule at field point (rc, th) for a layer at rotation rot and wobble wob (no anti alias):
 * { cov: 0|1, idx } where idx is the colour thread (hard bands). Mirrors loomField's layer shader.
 */
export function loomCover(layer, rc, th, rot = 0, wob = 0) {
  const s = STYLE_INDEX[layer.style] ?? 0, t = tOfR(rc, layer.style), A = layer.arms, C = layer.colors.length;
  if (s <= 3) {
    const a = th - rot - TAU * layer.turns * t + wob, qf = fract(a / TAU), pos = qf * A, k = Math.floor(pos);
    return { cov: pos - k < layer.duty ? 1 : 0, idx: k % C };
  }
  if (s === 4) {
    const pos = t * A - rot * A / TAU + helixOf(layer) * th / TAU + wob * 0.5;
    return { cov: fract(pos) < layer.duty ? 1 : 0, idx: ((Math.floor(pos) % C) + C) % C };
  }
  const a = th - rot - TAU * layer.turns * t * 0.35 + wob, m = 0.5 + 0.5 * Math.cos(a * A);
  const petal = Math.floor(fract(a / TAU) * A + 0.5);
  return { cov: m >= 1 - layer.duty && rc > 0.03 ? 1 : 0, idx: petal % C };
}

/** Thread colour for a layer: hard picks thread idx, gradient blends along g (the Loom's blend). */
function threadRgb(layer, g, hard) {
  const C = layer.colors.length;
  if (hard || layer.bandMode !== 'gradient' || C < 2) return hexRgb(layer.colors[((Math.floor(g) % C) + C) % C]);
  const i0 = Math.floor(g), f = g - i0, a = hexRgb(layer.colors[((i0 % C) + C) % C]), b = hexRgb(layer.colors[(i0 + 1) % C]);
  return [a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f, a[2] + (b[2] - a[2]) * f];
}

/**
 * Sample one particle on a layer's covered bands at phase 0, wobble 0.
 * @returns {{ mode: 0|1, x: number, th: number, rc: number, rgb: number[], halo: boolean }}
 *   mode 0 angular (x = rc, the GPU adds the rotation to th), 1 radial tunnel (x = t, the GPU adds rot/2pi to t)
 */
export function sampleOnLayer(layer, rnd, L = SPIRAL_LOOK, glow = 0) {
  const s = STYLE_INDEX[layer.style] ?? 0, A = layer.arms, D = layer.duty, C = layer.colors.length;
  const hole = L.holeFrac, rArea = () => Math.sqrt(hole * hole + rnd() * (1 - hole * hole));
  const halo = glow > 0 && rnd() < glow * L.haloShare;
  // glitter crowds the middle of each band (still inside it) so the arms read as lines at speed
  const fill = L.armFill ?? 1;
  const band = () => (halo ? D + rnd() * L.haloBand : D * (0.5 + 0.5 * fill * (rnd() + rnd() - 1)));
  if (s <= 3) {
    const rc = rArea(), t = tOfR(rc, layer.style), k = Math.floor(rnd() * A), f = band();
    const qf = (k + f) / A, th = TAU * qf + TAU * layer.turns * t;
    const rgb = threadRgb(layer, layer.bandMode === 'gradient' ? fract(qf * A / C) * C : k, layer.bandMode !== 'gradient');
    return { mode: 0, x: rc, th, rc, rgb, halo };
  }
  if (s === 4) {
    const h = helixOf(layer), tIn = tOfR(hole, 'tunnel');
    for (let tries = 0; tries < 12; tries++) {
      const th = (rnd() * 2 - 1) * Math.PI, t0 = tIn + rnd() * (1 - tIn), j = Math.floor(t0 * A + h * th / TAU);
      const pos = j + band(), t = (pos - h * th / TAU) / A;
      if (t < tIn || t > 1) continue;
      const rgb = threadRgb(layer, layer.bandMode === 'gradient' ? fract(pos / C) * C : j, layer.bandMode !== 'gradient');
      return { mode: 1, x: t, th, rc: rOfT(t, 'tunnel'), rgb, halo };
    }
    const t = tIn + rnd() * (1 - tIn);
    return { mode: 1, x: t, th: 0, rc: rOfT(t, 'tunnel'), rgb: hexRgb(layer.colors[0]), halo: true };
  }
  // petal: a rose mask that the turns shear into spiral petals
  const rc = Math.max(0.05, rArea()), t = tOfR(rc, layer.style), k = Math.floor(rnd() * A);
  const span = Math.acos(clamp(1 - 2 * (halo ? Math.min(0.95, D + L.haloBand) : D), -1, 1));
  const phi = (rnd() + rnd() - 1) * span * (halo ? 1 : fill), a = (TAU * k + phi) / A;
  const th = a + TAU * layer.turns * t * 0.35;
  const rgb = threadRgb(layer, layer.bandMode === 'gradient' ? fract(a / TAU * A / C) * C : k, layer.bandMode !== 'gradient');
  return { mode: 0, x: rc, th, rc, rgb, halo };
}

/**
 * Particle buffers for one gate (n at the high budget; any prefix is a fair subset, so a tier draws the
 * first Q of them). Layout per particle, 3 x vec4:
 *   a0 = (x, th, z metres along the road, layer + 2 * glitter)
 *   a1 = (r, g, b, size metres)
 *   a2 = (twinkle phase, twinkle Hz, brightness, mode 0 angular / 1 radial)
 */
export function buildParticles(params, gate, n, seed = 1, L = SPIRAL_LOOK) {
  const rnd = makeRng(seed), a0 = new Float32Array(n * 4), a1 = new Float32Array(n * 4), a2 = new Float32Array(n * 4);
  const two = !!(params.layer2 && params.layer2.enabled), depth = Math.max(0, gate.depthM || 0), K = sliceCount(depth, L);
  for (let i = 0; i < n; i++) {
    const l2 = two && rnd() < L.layer2Share, layer = l2 ? params.layer2 : params.layer;
    const p = sampleOnLayer(layer, rnd, L, params.glow || 0);
    const glitter = rnd() < L.glitterShare;
    // a deep gate is a short tunnel of spiral slices (a filled volume reads as dust from the front)
    const z = depth > 0 ? ((K > 1 ? (i % K) / (K - 1) : 0.5) - 0.5) * Math.max(0, depth - L.jitterM) + (rnd() - 0.5) * L.jitterM : (rnd() - 0.5) * L.jitterM;
    let [r, g, b] = p.rgb;
    if (glitter) { r += (1 - r) * L.whiteMix; g += (1 - g) * L.whiteMix; b += (1 - b) * L.whiteMix; }
    const size = (L.sizeM[0] + rnd() * (L.sizeM[1] - L.sizeM[0])) * (1 + L.sizeRimGain * p.rc) * (glitter ? L.glitterSize : 1);
    const bright = (p.halo ? L.haloAlpha : 1) * (l2 ? L.layer2Alpha : 1);
    a0.set([p.x, p.th, z, (l2 ? 1 : 0) + (glitter ? 2 : 0)], i * 4);
    a1.set([r, g, b, size], i * 4);
    a2.set([rnd(), L.twinkleHz[0] + rnd() * (L.twinkleHz[1] - L.twinkleHz[0]), bright, p.mode], i * 4);
  }
  return { a0, a1, a2, n };
}

/** Spiral slices in a gate of this depth (a thin gate is one). */
export function sliceCount(depthM, L = SPIRAL_LOOK) {
  return depthM > 0 ? Math.max(2, Math.round(depthM / L.sliceGapM) + 1) : 1;
}

/** Alpha scale for a gate: stacked slices add up when seen down the axis, so each is thinner. */
export function depthAlpha(depthM, L = SPIRAL_LOOK) {
  return 1 / (1 + (sliceCount(depthM, L) - 1) * L.sliceAlpha);
}

/** The Loom's luma-preserving hue rotation as three rows (loomField rotateHue). */
export function hueRows(rad) {
  const c = Math.cos(rad), s = Math.sin(rad);
  return [
    [0.213 + c * 0.787 - s * 0.213, 0.715 - c * 0.715 - s * 0.715, 0.072 - c * 0.072 + s * 0.928],
    [0.213 - c * 0.213 + s * 0.143, 0.715 + c * 0.285 + s * 0.140, 0.072 - c * 0.072 - s * 0.283],
    [0.213 - c * 0.213 - s * 0.787, 0.715 - c * 0.715 + s * 0.715, 0.072 + c * 0.928 + s * 0.072],
  ];
}

/**
 * The per-frame Loom state of a gate at loop phase 0..1: layer rotations, pulse zoom, wobble phase
 * (radians) and hue rotation, every one a whole number of cycles per loop like the Loom.
 */
export function loomState(params, phase) {
  const two = !!(params.layer2 && params.layer2.enabled);
  return {
    rot1: layerRot(params.layer, phase), rot2: two ? layerRot(params.layer2, phase) : 0,
    zoom: 1 + params.pulse.amp * Math.sin(TAU * phase * params.pulse.cycles),
    wobPhase: TAU * phase * params.wobble.cycles,
    hue: params.hueCycles > 0 ? TAU * params.hueCycles * phase : 0,
  };
}

/** Wobble at field radius rc (the Loom's: amp sin(2pi (freq rc + phase cycles))). */
export function wobbleAt(params, rc, wobPhase) {
  return params.wobble.amp > 0.001 ? params.wobble.amp * Math.sin(TAU * params.wobble.freq * clamp(rc, 0, 1) + wobPhase) : 0;
}

/**
 * One particle's gate-local point at a Loom state, before parting: [u right, v up, z along the road]
 * in metres. Mirrors the GPU position node (spirals3d.js).
 */
export function particleLocal(a0, a2, i, params, gate, st) {
  const x = a0[i * 4], th0 = a0[i * 4 + 1], z = a0[i * 4 + 2], layer = (a0[i * 4 + 3] % 2) >= 1 ? 1 : 0, mode = a2[i * 4 + 3];
  const rot = layer ? st.rot2 : st.rot1, arms = (layer ? params.layer2 : params.layer).arms;
  let rc, th;
  if (mode > 0.5) {
    const pre = rOfT(fract(x + rot / TAU), 'tunnel');
    const t = fract(x + rot / TAU - wobbleAt(params, pre, st.wobPhase) * 0.5 / arms);
    rc = rOfT(t, 'tunnel'); th = th0;
  } else {
    rc = x; th = th0 + rot - wobbleAt(params, x, st.wobPhase);
  }
  const depth = gate.depthM || 0, twist = depth > 0 ? (gate.helixTurns || 0) * TAU / depth : 0;
  th += twist * z;
  const R = gate.radiusM * st.zoom * rc * (1 + (gate.taper || 0) * (depth > 0 ? z / depth : 0));
  return [R * Math.cos(th), -R * Math.sin(th), z, rc];
}

/**
 * The road window share at gate-local u (metres right of the gate axis) and height h above the road
 * surface, for a road of half width halfW: corridorAlpha inside the window, 1 outside. Mirrors the GPU node.
 */
export function corridorFade(u, h, halfW, L = SPIRAL_LOOK) {
  const inU = 1 - sstep(halfW + L.corridorSideM, halfW + L.corridorSideM + L.corridorSoftM, Math.abs(u));
  const inH = 1 - sstep(L.corridorUpM, L.corridorUpM + L.corridorSoftM, h);
  return 1 - (1 - L.corridorAlpha) * inU * inH;
}

/** Gate-local [u, v, z] -> world, with a gate frame { c, t, b, n }. */
export function toWorld(gf, l) {
  return [0, 1, 2].map((k) => gf.c[k] + gf.b[k] * l[0] + gf.n[k] * l[1] + gf.t[k] * l[2]);
}

/** Gate frames on a track: the road frame at s = f * length, centre lifted upM along the road normal. */
export function gateFrames(track, gates, L = SPIRAL_LOOK) {
  return gates.map((g) => {
    const s = (((g.f % 1) + 1) % 1) * track.lengthM, f = track.frameAt(s), up = g.upM ?? L.upM;
    return {
      s, w: f.w, t: f.t.slice(), b: f.b.slice(), n: f.n.slice(),
      c: [f.p[0] + f.n[0] * up, f.p[1] + f.n[1] * up, f.p[2] + f.n[2] * up],
    };
  });
}

const dot3 = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const sstep = (e0, e1, x) => { const t = clamp((x - e0) / (e1 - e0), 0, 1); return t * t * (3 - 2 * t); };

/** Signed distance of a point along the gate axis (negative = still approaching). */
export function axialOf(gf, p) {
  return dot3([p[0] - gf.c[0], p[1] - gf.c[1], p[2] - gf.c[2]], gf.t);
}

/** Distance of a point from the gate axis. */
export function lateralOf(gf, p) {
  const d = [p[0] - gf.c[0], p[1] - gf.c[1], p[2] - gf.c[2]], a = dot3(d, gf.t);
  return Math.hypot(d[0] - gf.t[0] * a, d[1] - gf.t[1] * a, d[2] - gf.t[2] * a);
}

/**
 * The curtain: how far a particle at world P moves for a kart at world K with slot weight w, along
 * the gate axis T. Ahead of the kart (the particles it is about to reach) the parting opens over
 * partLeadM, behind it the curtain closes over partTrailM. Out from the kart's line plus a swirl.
 * Mirrors the GPU node. @returns {number[]} world offset
 */
export function partOffset(P, K, w, T, L = SPIRAL_LOOK, mul = 1) {
  const d = [P[0] - K[0], P[1] - K[1], P[2] - K[2]], da = dot3(d, T);
  const dl = [d[0] - T[0] * da, d[1] - T[1] * da, d[2] - T[2] * da], dist = Math.hypot(dl[0], dl[1], dl[2]) + 1e-3;
  const wAx = da >= 0 ? 1 - sstep(0, L.partLeadM, da) : 1 - sstep(0, L.partTrailM, -da);
  const wLat = 1 - sstep(L.partInnerM, L.partRadiusM, dist);
  const push = w * wAx * wLat * L.partM * mul;
  const dir = [dl[0] / dist, dl[1] / dist, dl[2] / dist];
  const sw = [T[1] * dir[2] - T[2] * dir[1], T[2] * dir[0] - T[0] * dir[2], T[0] * dir[1] - T[1] * dir[0]];
  return [0, 1, 2].map((k) => (dir[k] + sw[k] * L.swirlShare) * push);
}

/**
 * The karts that move a gate's particles this frame: within the axial window and near the disc, the
 * player first, then the nearest along the axis, at most L.slots. @returns {number[]} kart indices
 */
export function pickSlots(gf, gate, karts, L = SPIRAL_LOOK, player = 0) {
  const half = (gate.depthM || 0) / 2 + L.jitterM;
  const lo = -half - L.partLeadM, hi = half + L.partTrailM, reach = gate.radiusM + L.partRadiusM;
  const cand = [];
  for (let i = 0; i < karts.length; i++) {
    const p = karts[i];
    if (!p) continue;
    const a = axialOf(gf, p);
    if (a < lo || a > hi || lateralOf(gf, p) > reach) continue;
    cand.push([i === player ? -1 : Math.abs(a), i]);
  }
  cand.sort((x, y) => x[0] - y[0]);
  return cand.slice(0, L.slots).map((c) => c[1]);
}

/** True when a point crossed the gate plane forward this frame inside the disc. */
export function crossed(prevAxial, axial, lateral, radiusM) {
  return prevAxial < 0 && axial >= 0 && axial - prevAxial < 60 && lateral <= radiusM;
}

/** Particles per gate for a tier, and the share drawn for a gate past activeM. */
export function budgetFor(tier, distM = 0, L = SPIRAL_LOOK) {
  const n = L.perGate[tier] ?? L.perGate.high;
  if (distM > L.drawM) return 0;
  return distM > L.activeM ? Math.round(n * L.farShare) : n;
}

/** The camera burst: count for the tier, cut when photosafe refused it or Gentle is on. */
export function burstCount(tier, { granted = true, gentle = false } = {}, L = SPIRAL_LOOK) {
  const B = L.burst, n = B.count[tier] ?? B.count.high;
  return Math.round(n * (granted ? 1 : B.refusedMul) * (gentle ? B.gentleCountMul : 1));
}

/** Burst brightness 0..1 at age s: a rise (Gentle: at least riseMs) then a smooth fall to 0 at lifeS. */
export function burstEnvelope(age, { gentle = false, riseMs = 300, fadeMs = 600 } = {}, L = SPIRAL_LOOK) {
  const B = L.burst, rise = gentle ? Math.max(B.riseS, riseMs / 1000) : B.riseS, life = gentle ? Math.max(B.lifeS, rise + fadeMs / 1000) : B.lifeS;
  if (!(age >= 0) || age >= life) return 0;
  if (age < rise) return sstep(0, rise, age);
  return 1 - sstep(rise, life, age);
}

/** Twinkle multiplier of one particle (1 - amp * wave); a gate's mean stays flat because phases spread. */
export function twinkle(t, phase, hz, amp) {
  return 1 - amp * (0.5 + 0.5 * Math.sin(TAU * (hz * t + phase)));
}

/** Gate layout check: the brief's corridors (f), a sane size, a Loom source per gate. */
export function checkLayout(layout) {
  const errs = [];
  if (!layout || !Array.isArray(layout.gates)) return ['no gates'];
  for (const g of layout.gates) {
    if (!(g.f >= 0 && g.f < 1)) errs.push(g.id + ': f');
    if (!(g.radiusM >= 15 && g.radiusM <= 25)) errs.push(g.id + ': radius ' + g.radiusM);
    if (!(g.loom || g.seed != null)) errs.push(g.id + ': no Loom source');
    if (g.depthM != null && !(g.depthM >= 0 && g.depthM <= 40)) errs.push(g.id + ': depth');
  }
  return errs;
}
