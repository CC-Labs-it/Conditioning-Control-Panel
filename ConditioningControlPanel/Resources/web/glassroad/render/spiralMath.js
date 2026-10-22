/* render/spiralMath.js - the spiral gates' numbers and maths (polish 3 and 4, SPIRALS lane). No three.js.
 *
 * Every spiral comes from the Loom (owner law): the real arcademy/engine/loom/loomField.js and its
 * seeded "surprise me" (seeded.js, the Loom's own randomParams2 with the dice handed in), imported by
 * relative path, never forked. Since polish 4 the band maths (span rule, layer rotation, style mapping,
 * tunnel helix, band rule, pulse, wobble and hue rotation) are the Loom's own named exports too; this
 * file only adds the 3D placement, the particle budget and the gate screen's timing.
 *
 * A gate is the Loom field stood up across the road: field x is the road's right (frame b), field y
 * (down on the Loom canvas) is minus the road's up (frame n), so a driver looking down the road sees the
 * field exactly as the Loom draws it, clockwise for direction 1 and reading inward. Particles are sampled
 * ON the covered bands of the field at phase 0 and the GPU turns them with the Loom's own loop timing:
 * the angular styles rotate rigidly by the layer rotation, the tunnel style's bands ride the radius.
 * Three kinds: thin bright cores on the arm centre lines, soft wide glow sprites under them (the halo
 * that makes an arm read from 100 m), and a little glitter. An aura disc behind each gate dims what is
 * behind it (dark gaps between arms) and wears a coloured rim.
 */

import {
  normalizeParams2, loopMs2, LOOM_STYLES, LOOM_GOLDEN_K, loomSpanRad, loomLayerRotationRad, loomHueRows, loomStyleT, loomStyleR,
  loomHelix, loomBandAt, loomPulseZoom, loomWobble, loomHueRad,
} from '../../arcademy/engine/loom/loomField.js';
import * as SEEDED from '../../arcademy/engine/loom/seeded.js';
import * as SPECTRUM_MOD from './spectrum.js';

const TAU = Math.PI * 2;
const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));

export { LOOM_GOLDEN_K };

/** Lane look table (render numbers live here, never in logic). Gentle halves turn, twinkle and contrast. */
export const SPIRAL_LOOK = Object.freeze({
  radiusM: 20, upM: 3.5, holeFrac: 0.06, jitterM: 0.6,
  perGate: Object.freeze({ high: 12000, mid: 6000, phone: 3000 }),
  farShare: 0.3, activeM: 400, drawM: 900,
  // polish 4 (owner: "dense, with a glow and an aura, so we spot the arms at a glance"): cores sit on the
  // arm centre line (armFill of each band) sized to the band's real width across the arm, a soft glow
  // sprite layer under them (glowShare of the budget, glowSpan of the band width, never past glowMaxM)
  sizeM: Object.freeze([0.16, 0.42]), coreSpan: 0.42, sizeRimGain: 0.2, armFill: 0.32,
  glitterShare: 0.03, glitterSize: 1.6, haloShare: 0, haloBand: 0.04,
  glowShare: 0.16, glowSpan: 1.35, glowMinM: 0.7, glowMaxM: 4.5, glowAlpha: 0.16, glowGain: 1.0, glowNearM: 14,
  alpha: 0.9, layer2Alpha: 0.8, haloAlpha: 0.35, gain: 1.25, glitterGain: 1.8, whiteMix: 0.3, coreWhite: 0.15,
  sliceGapM: 8, sliceAlpha: 0.3, frontShare: 0.5, radialFloor: 0.35,
  twinkleHz: Object.freeze([0.5, 2.2]), twinkleAmp: 0.45,
  gentleTwinkleMul: 0.5, gentleTurnMul: 0.5, gentlePartMul: 0.6, gentleAlphaMul: 0.75,
  turnMul: 1, nearM: 2.5, nearSpanM: 4, farFadeM: 900,
  // the aura disc behind a gate (auraBackM past its far face): dims what is behind it by auraDim (so the
  // gaps between arms read dark against a bright sky), tinted auraTint of the thread colours, with an
  // additive rim of auraRing at r = 1 (auraRingW wide) and a faint inner glow; the road window is cut out
  // and the whole disc fades out within auraNearM of the camera (the gate screen takes over there)
  aura: Object.freeze({ outer: 1.22, backM: 1.5, dim: 0.7, tint: 0.05, ring: 0.8, ringW: 0.06, inner: 0.03, nearM: 10, nearSpanM: 20, gentleMul: 0.5 }),
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
  // the gate screen (render/gateScreen.js): the 2D Loom over the whole view after a pass. riseS at least
  // 0.3 s, holdS, fallS; the mask is the share of the field shown from the centre (0) to the corners (1),
  // light over the road, strong at the edges; maxBandHz caps how often an arm sweeps a pixel (photosafe:
  // under 3 Hz); Gentle halves opacity and speed; a photosafe refusal plays it at refusedMul
  screen: Object.freeze({
    riseS: 0.4, holdS: 1.05, fallS: 0.75, peak: 1,
    mask: Object.freeze([[0, 0.36], [0.3, 0.42], [0.62, 0.7], [1, 0.88]]), centreY: 0.56,
    maxBandHz: 2.5, gentleAlphaMul: 0.5, gentleSpeedMul: 0.5, refusedMul: 0.5,
    backLong: Object.freeze({ high: 640, mid: 512, phone: 256 }), frameMs: Object.freeze({ high: 0, mid: 0, phone: 40 }), zIndex: 2,
  }),
  fallbackLoopMs: 1800,
  /** Full spectrum threads for seeded gates when render/spectrum.js is missing. */
  palette: Object.freeze(['#ff2fb3', '#c13cff', '#7a3cff', '#00e5ff', '#00ffa8', '#9dff2f', '#ffd21f', '#ff7a1a', '#ff2f4f', '#2f6bff']),
});

const LOOM_MODS = Object.freeze({ field: Object.freeze({ normalizeParams2, loopMs2 }), seeded: SEEDED });

/** The Loom modules (imported statically since polish 4; kept async for the callers). */
export async function loadLoom() { return LOOM_MODS; }

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

/**
 * The Loom params for one gate: its stored Loom preset, or the seeded surprise-me with the gate's seed.
 * `loom` may be null (the static Loom is used then); a seeded gate without seeded.js gets three palette threads.
 */
export function gateParams(gate, loom, palette = SPIRAL_LOOK.palette) {
  const field = (loom && loom.field) || LOOM_MODS.field, seeded = loom ? loom.seeded : null;
  let q;
  if (gate && gate.seed != null && seeded && seeded.seededParams2) {
    q = seeded.seededParams2(makeRng(gate.seed), { palette: palette.map((c) => toHex(c)), centerpiece: false });
  } else {
    const raw = (gate && gate.seed != null && !gate.loom) ? { layer: { colors: palette.slice(0, 3).map((c) => toHex(c)) } } : (gate && gate.loom) || {};
    q = field.normalizeParams2(raw);
  }
  // handedness law: both layers turn clockwise on screen and read inward
  q = { ...q, layer: { ...q.layer, direction: 1 }, layer2: { ...q.layer2, direction: 1 } };
  return q;
}

/** One seamless Loom loop in seconds (the Loom's own loopMs2). */
export function loopS(params, loom = LOOM_MODS) {
  const f = (loom && loom.field) || LOOM_MODS.field;
  return (f.loopMs2 ? f.loopMs2(params) : SPIRAL_LOOK.fallbackLoopMs) / 1000;
}

/** The Loom span rule: the smallest rotation that maps a layer onto itself (1 to 6 threads). */
export const spanRad = loomSpanRad;

/** Layer rotation at loop phase 0..1 (the Loom's layerRotationRad). */
export const layerRot = loomLayerRotationRad;

/** Style name -> the Loom's style index (LOOM_STYLES order, the shader's u_style). */
export const STYLE_INDEX = Object.freeze(Object.fromEntries(LOOM_STYLES.map((s, i) => [s, i])));

/** Field radius 0..1 -> band parameter t, per style (the Loom shader's mapping), and its inverse. */
export const tOfR = loomStyleT;
export const rOfT = loomStyleR;

/** Tunnel helix shear snapped to whole colour cycles (the Loom shader's rule). */
export const helixOf = loomHelix;

/** The Loom band rule at field point (rc, th): { cov: 0|1, idx } (the Loom's loomBandAt). */
export const loomCover = loomBandAt;

const fract = (x) => x - Math.floor(x);

/** Thread colour for a layer: hard picks thread idx, gradient blends along g (the Loom's blend). */
function threadRgb(layer, g, hard) {
  const C = layer.colors.length;
  if (hard || layer.bandMode !== 'gradient' || C < 2) return hexRgb(layer.colors[((Math.floor(g) % C) + C) % C]);
  const i0 = Math.floor(g), f = g - i0, a = hexRgb(layer.colors[((i0 % C) + C) % C]), b = hexRgb(layer.colors[(i0 + 1) % C]);
  return [a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f, a[2] + (b[2] - a[2]) * f];
}

/**
 * The distance between neighbouring arms of one layer, measured ACROSS the arms, at field radius rc
 * (field units; times the gate radius for metres). 1 / |grad pos| of the Loom's band coordinate: a tight
 * log spiral packs its arms far closer than the arc between them.
 */
export function bandSpacing(layer, rc) {
  const s = STYLE_INDEX[layer.style] ?? 0, r = clamp(rc, 0.02, 1), h = 1e-3;
  const dt = (loomStyleT(Math.min(1, r + h), layer.style) - loomStyleT(Math.max(0, r - h), layer.style)) / (Math.min(1, r + h) - Math.max(0, r - h));
  if (s === 4) {
    const g = Math.hypot(layer.arms * dt, loomHelix(layer) / (TAU * r));
    return 1 / Math.max(g, 1e-6);
  }
  const k = s === 5 ? 0.35 : 1;
  return TAU / (layer.arms * Math.hypot(1 / r, TAU * layer.turns * k * dt));
}

/**
 * Sample one particle on a layer's covered bands at phase 0, wobble 0. `glow` puts it on the band centre
 * line (the glow sprite layer); cores spread over the centred armFill share of the band.
 * @returns {{ mode: 0|1, x: number, th: number, rc: number, rgb: number[], halo: boolean }}
 *   mode 0 angular (x = rc, the GPU adds the rotation to th), 1 radial tunnel (x = t, the GPU adds rot/2pi to t)
 */
export function sampleOnLayer(layer, rnd, L = SPIRAL_LOOK, glow = 0, centre = false) {
  const s = STYLE_INDEX[layer.style] ?? 0, A = layer.arms, D = layer.duty;
  const hole = L.holeFrac, rArea = () => Math.sqrt(hole * hole + rnd() * (1 - hole * hole));
  const halo = !centre && glow > 0 && rnd() < glow * L.haloShare;
  const fill = centre ? 0 : (L.armFill ?? 1);
  const band = () => (halo ? D + rnd() * L.haloBand : D * (0.5 + 0.5 * fill * (rnd() + rnd() - 1)));
  if (s <= 3) {
    const rc = rArea(), t = tOfR(rc, layer.style), k = Math.floor(rnd() * A), f = band();
    const qf = (k + f) / A, th = TAU * qf + TAU * layer.turns * t;
    const rgb = threadRgb(layer, layer.bandMode === 'gradient' ? fract(qf * A / layer.colors.length) * layer.colors.length : k, layer.bandMode !== 'gradient');
    return { mode: 0, x: rc, th, rc, rgb, halo };
  }
  if (s === 4) {
    const h = helixOf(layer), tIn = tOfR(hole, 'tunnel'), C = layer.colors.length;
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
  const rc = Math.max(0.05, rArea()), t = tOfR(rc, layer.style), k = Math.floor(rnd() * A), C = layer.colors.length;
  const span = Math.acos(clamp(1 - 2 * (halo ? Math.min(0.95, D + L.haloBand) : D), -1, 1));
  const phi = (rnd() + rnd() - 1) * span * (halo ? 1 : fill), a = (TAU * k + phi) / A;
  const th = a + TAU * layer.turns * t * 0.35;
  const rgb = threadRgb(layer, layer.bandMode === 'gradient' ? fract(a / TAU * A / C) * C : k, layer.bandMode !== 'gradient');
  return { mode: 0, x: rc, th, rc, rgb, halo };
}

/** Kind flags packed in a0.w: layer 2 (+1), glitter (+2), glow sprite (+4). */
export const KIND = Object.freeze({ layer2: 1, glitter: 2, glow: 4 });

/**
 * Particle buffers for one gate (n at the high budget; any prefix is a fair subset, so a tier draws the
 * first Q of them). Layout per particle, 3 x vec4:
 *   a0 = (x, th, z metres along the road, layer + 2 * glitter + 4 * glow)
 *   a1 = (r, g, b, size metres)
 *   a2 = (twinkle phase, twinkle Hz, brightness, mode 0 angular / 1 radial)
 */
export function buildParticles(params, gate, n, seed = 1, L = SPIRAL_LOOK) {
  const rnd = makeRng(seed), a0 = new Float32Array(n * 4), a1 = new Float32Array(n * 4), a2 = new Float32Array(n * 4);
  const two = !!(params.layer2 && params.layer2.enabled), depth = Math.max(0, gate.depthM || 0), K = sliceCount(depth, L);
  const R = gate.radiusM || L.radiusM;
  for (let i = 0; i < n; i++) {
    const l2 = two && rnd() < L.layer2Share, layer = l2 ? params.layer2 : params.layer;
    const glow = rnd() < (L.glowShare || 0);
    const p = sampleOnLayer(layer, rnd, L, params.glow || 0, glow);
    const glitter = !glow && rnd() < L.glitterShare;
    // a deep gate is a short tunnel of spiral slices; the front slice (the one a driver meets first) holds
    // frontShare of the budget so the face reads as one clean spiral, the rest thin out behind it
    const slice = K > 1 ? (rnd() < L.frontShare ? 0 : Math.floor(rnd() * K)) : 0;
    const z = depth > 0 ? ((K > 1 ? slice / (K - 1) : 0.5) - 0.5) * Math.max(0, depth - L.jitterM) + (rnd() - 0.5) * L.jitterM : (rnd() - 0.5) * L.jitterM;
    let [r, g, b] = p.rgb;
    const white = glitter ? L.whiteMix : glow ? 0 : (L.coreWhite || 0);
    r += (1 - r) * white; g += (1 - g) * white; b += (1 - b) * white;
    // sized to the band's width across the arm (metres), so a tight inner coil keeps its dark gaps
    const bandM = layer.duty * bandSpacing(layer, p.rc) * R;
    const size = glow ? clamp(bandM * L.glowSpan, L.glowMinM, L.glowMaxM)
      : clamp(bandM * (L.coreSpan ?? 1) * (0.7 + 0.6 * rnd()), L.sizeM[0], L.sizeM[1]) * (1 + L.sizeRimGain * p.rc) * (glitter ? L.glitterSize : 1);
    const bright = (glow ? L.glowAlpha : p.halo ? L.haloAlpha : 1) * (l2 ? L.layer2Alpha : 1);
    a0.set([p.x, p.th, z, (l2 ? KIND.layer2 : 0) + (glitter ? KIND.glitter : 0) + (glow ? KIND.glow : 0)], i * 4);
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

/** The Loom's luma-preserving hue rotation as three rows (loomField loomHueRows). */
export const hueRows = loomHueRows;

/**
 * The per-frame Loom state of a gate at loop phase 0..1: layer rotations, pulse zoom, wobble phase
 * (radians, the GPU uniform) and hue rotation, every one a whole number of cycles per loop like the Loom.
 */
export function loomState(params, phase) {
  const two = !!(params.layer2 && params.layer2.enabled);
  return {
    phase,
    rot1: layerRot(params.layer, phase), rot2: two ? layerRot(params.layer2, phase) : 0,
    zoom: loomPulseZoom(params, phase),
    wobPhase: TAU * phase * params.wobble.cycles,
    hue: loomHueRad(params, phase),
  };
}

/** Wobble at field radius rc for a loomState (the Loom's loomWobble). */
export function wobbleAt(params, rc, st) {
  return loomWobble(params, rc, typeof st === 'number' ? st / (TAU * (params.wobble.cycles || 1)) : st.phase);
}

/**
 * One particle's gate-local point at a Loom state, before parting: [u right, v up, z along the road, rc]
 * in metres. Mirrors the GPU position node (spirals3d.js).
 */
export function particleLocal(a0, a2, i, params, gate, st) {
  const x = a0[i * 4], th0 = a0[i * 4 + 1], z = a0[i * 4 + 2], layer = (a0[i * 4 + 3] % 2) >= 1 ? 1 : 0, mode = a2[i * 4 + 3];
  const rot = layer ? st.rot2 : st.rot1, arms = (layer ? params.layer2 : params.layer).arms;
  let rc, th;
  if (mode > 0.5) {
    const pre = rOfT(fract(x + rot / TAU), 'tunnel');
    const t = fract(x + rot / TAU - wobbleAt(params, pre, st) * 0.5 / arms);
    rc = rOfT(t, 'tunnel'); th = th0;
  } else {
    rc = x; th = th0 + rot - wobbleAt(params, x, st);
  }
  const depth = gate.depthM || 0, twist = depth > 0 ? (gate.helixTurns || 0) * TAU / depth : 0;
  th += twist * z;
  const R = gate.radiusM * st.zoom * rc * (1 + (gate.taper || 0) * (depth > 0 ? z / depth : 0));
  return [R * Math.cos(th), -R * Math.sin(th), z, rc];
}

/**
 * The road window share at gate-local u (metres right of the gate axis) and height h above the road
 * surface, for a road of half width halfW: `inside` in the window (corridorAlpha by default), 1 outside.
 * Mirrors the GPU nodes (particles and aura).
 */
export function corridorFade(u, h, halfW, L = SPIRAL_LOOK, inside = L.corridorAlpha) {
  const inU = 1 - sstep(halfW + L.corridorSideM, halfW + L.corridorSideM + L.corridorSoftM, Math.abs(u));
  const inH = 1 - sstep(L.corridorUpM, L.corridorUpM + L.corridorSoftM, h);
  return 1 - (1 - inside) * inU * inH;
}

/**
 * The aura disc at field point (x right, y up, both in units of the gate radius): { dim, ring } where dim
 * is the share it darkens what is behind it and ring the additive rim brightness. Mirrors the GPU node.
 */
export function auraAt(x, y, { halfW = 6, upM = SPIRAL_LOOK.upM, radiusM = SPIRAL_LOOK.radiusM, gentle = false } = {}, L = SPIRAL_LOOK) {
  const A = L.aura, r = Math.hypot(x, y), g = gentle ? A.gentleMul : 1;
  const win = corridorFade(x * radiusM, y * radiusM + upM, halfW, L, 0);
  const disc = 1 - sstep(0.96, 1.04, r);
  const ring = Math.exp(-(((r - 1) / A.ringW) ** 2)) + A.inner * sstep(0.45, 1, r) * disc;
  return { dim: A.dim * disc * win * g, ring: A.ring * ring * g * (r <= A.outer ? 1 : 0) };
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

/** The gate screen's stages in seconds { rise, hold, fall, life } (Gentle: at least riseMs and fadeMs). */
export function screenTimes({ gentle = false, riseMs = 300, fadeMs = 600 } = {}, L = SPIRAL_LOOK) {
  const S = L.screen, rise = Math.max(0.3, S.riseS, gentle ? riseMs / 1000 : 0), fall = Math.max(S.fallS, gentle ? fadeMs / 1000 : 0);
  return { rise, hold: S.holdS, fall, life: rise + S.holdS + fall };
}

/** Gate screen opacity 0..1 at age s: a smooth rise, a hold, a smooth fall, 0 outside. */
export function screenEnvelope(age, opts = {}, L = SPIRAL_LOOK) {
  const T = screenTimes(opts, L);
  if (!(age >= 0) || age >= T.life) return 0;
  if (age < T.rise) return sstep(0, T.rise, age);
  if (age < T.rise + T.hold) return 1;
  return 1 - sstep(T.rise + T.hold, T.life, age);
}

/**
 * How often (Hz) an arm of the field sweeps a fixed pixel at the Loom's own speed: per layer, the bands
 * that pass in one loop (rotation span x speedMul / band period) over the loop length; the larger layer.
 */
export function bandHz(params, loopSeconds = loopS(params)) {
  const one = (l) => (loomSpanRad(l) * l.speedMul * l.arms / TAU) / loopSeconds;
  return Math.max(one(params.layer), params.layer2 && params.layer2.enabled ? one(params.layer2) : 0);
}

/** The gate screen's speed multiplier: the Loom's own pace, slowed until no arm sweeps a pixel faster than maxBandHz. */
export function screenRateMul(params, { gentle = false } = {}, L = SPIRAL_LOOK) {
  const hz = bandHz(params), S = L.screen;
  return Math.min(1, S.maxBandHz / Math.max(hz, 1e-6)) * (gentle ? S.gentleSpeedMul : 1);
}

/** The gate screen's CSS mask: a radial alpha ramp, light at the centre (over the road), strong at the corners. */
export function screenMaskCss(L = SPIRAL_LOOK) {
  const S = L.screen;
  const stops = S.mask.map(([at, a]) => `rgba(0,0,0,${a}) ${Math.round(at * 100)}%`).join(', ');
  return `radial-gradient(farthest-corner at 50% ${Math.round(S.centreY * 100)}%, ${stops})`;
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
