/* render/spectrum.js - the colour system of polish 3 (SPECTRUM lane). Three-free data and helpers.
 *
 * Every section shows the full spectrum at high saturation; pink, magenta and violet stay dominant
 * (about 40 percent by weight). SPECTRUM holds only hex strings (other lanes flatten it), the weights
 * sit beside it in SPECTRUM_WEIGHTS. A ramp is the section's hues in wheel order, each stop as long as
 * its weight, with soft joins; its luminance is partly levelled so a hue change never reads as a
 * brightness change (trippy never means flicker): bright hues are scaled down, never dark ones up.
 * The ROAD streams its pattern past the eye at kart speed, so its ramp (roadRampAt, a second texture)
 * is levelled both ways into a narrow band (roadLevel: bright hues scaled down, dark ones lifted with a
 * little white), so a block of one hue next to a block of another is never a brightness step: the
 * worst neighbouring pair stays under roadFlashDelta in relative luminance at any streaming rate.
 *
 * Road patterns (render/materials/road.js): each section runs a list of patterns along the lap, zone by
 * zone, and one zone morphs into the next with a V shaped sweep. The zone maths lives here so it is
 * tested in node.
 */

/** Hue sets per section, wheel order (pink, magenta, violet, blue, cyan, teal, lime, gold, orange, red). */
export const SPECTRUM = Object.freeze({
  dawn: Object.freeze(['#ff1a8c', '#ff1ff0', '#8f1fff', '#2a5cff', '#00dcff', '#00e0a8', '#8cff1a', '#ffcc0a', '#ff6a0a', '#ff1a33']),
  deep: Object.freeze(['#ff1a80', '#ec14ff', '#6e14ff', '#1a40ff', '#00ccff', '#00d49c', '#7cff24', '#ffc00a', '#ff5c0a', '#ff1438']),
  afterglow: Object.freeze(['#ff2a84', '#ff2ad8', '#9a2aff', '#2a60ff', '#14e0ff', '#00e4ac', '#a8ff1f', '#ffd21f', '#ff7a14', '#ff2626']),
});

/** Weight of each hue above (same order); the first three are the dominant pinks. Each row sums to 1. */
export const SPECTRUM_WEIGHTS = Object.freeze({
  dawn: Object.freeze([0.16, 0.12, 0.12, 0.08, 0.10, 0.07, 0.08, 0.10, 0.10, 0.07]),
  deep: Object.freeze([0.12, 0.14, 0.16, 0.14, 0.12, 0.10, 0.06, 0.06, 0.05, 0.05]),
  afterglow: Object.freeze([0.16, 0.10, 0.12, 0.04, 0.08, 0.04, 0.08, 0.14, 0.14, 0.10]),
});

/** Three saturated base colours per section (pal.col(0..2): band A, band B, ground). */
export const SPECTRUM_BASE = Object.freeze({
  dawn: Object.freeze(['#ff3d9e', '#ff7ae6', '#ffb02e']),
  deep: Object.freeze(['#f0287a', '#4a2cff', '#170e46']),
  afterglow: Object.freeze(['#ff8a2a', '#ff3d8e', '#ffe6b8']),
});

/** Road patterns, in shader index order. */
export const PATTERNS = Object.freeze(['lanes', 'chevrons', 'checker', 'rings', 'weave']);

/** Pattern list per section, zones of equal length in order. Lanes carry about half the lap. */
export const SECTION_PATTERNS = Object.freeze({
  dawn: Object.freeze(['lanes', 'chevrons', 'lanes']),
  deep: Object.freeze(['rings', 'lanes', 'checker', 'weave']),
  afterglow: Object.freeze(['weave', 'lanes', 'chevrons']),
});

/** Numbers of the colour system and the road patterns. */
export const SPECTRUM_LOOK = Object.freeze({
  rampN: 256, rampBlend: 0.4,            // ramp texels; share of each stop spent blending into the next
  levelTarget: 0.24, levelK: 0.75,       // brighter hues are pulled down toward this linear luminance, this far
  roadLevelTarget: 0.27, roadLevelBand: 0.1, // the road ramp: every colour inside target +- band (a share)
  roadFlashDelta: 0.1, flickerHz: 3,     // a brightness step under this (relative luminance) is no flash; above it, at most flickerHz
  hueCycleS: 90, rampM: 96,              // a full hue turn in time; metres for a full turn along the road
  flowBaseMps: 5, flowSpeedMul: 0.6,     // pattern flow toward the player: base plus a share of kart speed
  flowMaxMps: 140, speedTcS: 0.25,       // camera speed estimate: clamp and smoothing
  morphM: 34, sweepSoftM: 9, sweepSlope: 1.4,   // zone morph window, sweep softness, V slope (m of s per m lateral)
  laneW: 1.6, laneHueStep: 0.29, laneLineW: 0.1, packetM: 18, packetLen: 0.32, packetShare: 0.55,
  chevronM: 8, chevronSlope: 1.6, chevronHueSteps: 5, chevronEdgeM: 0.6, checkerM: 3.2, ringCellM: 32, ringW: 2.2, ringArms: 2, ringHueM: 9, ringSwing: 0.3, ringLineR: [11, 7], weaveM: 5,
  centreLineW: 0.12, centreGain: 0.35, edgeLaneM: 1.3, edgeGain: 0.55,
  nearCalmM: [3, 12],                    // across-the-road lines fade in over this distance from the eye
  lineGain: 0.6, packetGain: 1, baseGain: 1,
});

const SECTION_KEYS = Object.keys(SPECTRUM);
const ALIAS = { induction: 'dawn', deepening: 'deep', release: 'afterglow' };

/** A section key for a palette id, section id or act, or an index (wraps); 'dawn' when unknown. */
export function sectionKey(id) {
  if (typeof id === 'number' && Number.isFinite(id)) return SECTION_KEYS[((Math.floor(id) % 3) + 3) % 3];
  if (typeof id === 'string') { if (SPECTRUM[id]) return id; if (ALIAS[id]) return ALIAS[id]; }
  if (id && typeof id === 'object') return sectionKey(id.palette ?? id.act ?? id.id);
  return 'dawn';
}

/** '#rrggbb' to [r, g, b] in 0..1 (sRGB). */
export function hexRgb(hex) {
  const n = parseInt(String(hex).slice(1, 7), 16) || 0;
  return [((n >> 16) & 255) / 255, ((n >> 8) & 255) / 255, (n & 255) / 255];
}
/** [r, g, b] 0..1 to '#rrggbb'. */
export function rgbHex(c) {
  return '#' + c.map((v) => Math.round(Math.max(0, Math.min(1, v)) * 255).toString(16).padStart(2, '0')).join('');
}
const toLin = (v) => (v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4);
const toSrgb = (v) => (v <= 0.0031308 ? v * 12.92 : 1.055 * v ** (1 / 2.4) - 0.055);
/** Relative luminance of an sRGB [r, g, b]. */
export function luma(c) { return 0.2126 * toLin(c[0]) + 0.7152 * toLin(c[1]) + 0.0722 * toLin(c[2]); }

/**
 * Pull an sRGB colour's luminance a share k down toward target (a linear scale, so the hue and the
 * saturation hold). Darker colours stay as they are: lifting them would wash them toward white.
 */
export function levelLuma(c, target = SPECTRUM_LOOK.levelTarget, k = SPECTRUM_LOOK.levelK) {
  const L = luma(c);
  if (L <= target) return c.slice();
  const want = L + (target - L) * k, g = want / L;
  return c.map((v) => Math.max(0, Math.min(1, toSrgb(Math.min(1, toLin(v) * g)))));
}

/**
 * Level an sRGB colour into the road band: luminance above target * (1 + band) is scaled down (hue and
 * saturation hold), below target * (1 - band) it is lifted by mixing a little white in linear light.
 */
export function roadLevel(c, target = SPECTRUM_LOOK.roadLevelTarget, band = SPECTRUM_LOOK.roadLevelBand) {
  const L = luma(c), hi = target * (1 + band), lo = target * (1 - band);
  if (L > hi) { const g = hi / L; return c.map((v) => Math.max(0, Math.min(1, toSrgb(Math.min(1, toLin(v) * g))))); }
  if (L < lo) { const w = (lo - L) / Math.max(1e-6, 1 - L); return c.map((v) => Math.max(0, Math.min(1, toSrgb(toLin(v) + (1 - toLin(v)) * w)))); }
  return c.slice();
}

/** Saturation (HSV) of an sRGB colour, 0..1. */
export function saturation(c) { const M = Math.max(...c), m = Math.min(...c); return M <= 0 ? 0 : (M - m) / M; }

/** Cumulative stop edges of a section: [[from, to, hex], ...] over 0..1. */
export function stops(key) {
  const k = sectionKey(key), hex = SPECTRUM[k], w = SPECTRUM_WEIGHTS[k];
  const sum = w.reduce((a, b) => a + b, 0);
  let at = 0;
  return hex.map((h, i) => { const from = at; at += w[i] / sum; return [from, i === hex.length - 1 ? 1 : at, h]; });
}

const smooth = (t) => t * t * (3 - 2 * t);
const fract = (x) => x - Math.floor(x);
const mixRgb = (a, b, t) => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];

/** The ramp colour at x (wraps), sRGB 0..1, before levelling: a plateau per stop, a soft join to the next. */
export function rampRaw(key, x, blend = SPECTRUM_LOOK.rampBlend) {
  const st = stops(key), f = fract(x);
  const i = st.findIndex(([a, b]) => f >= a && f < b), j = i < 0 ? st.length - 1 : i;
  const [a, b, h] = st[j], n = st[(j + 1) % st.length];
  const len = b - a, t = len > 0 ? (f - a) / len : 0, edge = 1 - blend;
  const c = hexRgb(h);
  return t <= edge ? c : mixRgb(c, hexRgb(n[2]), smooth((t - edge) / Math.max(1e-6, blend)));
}

/** The levelled ramp colour at x for a section, sRGB 0..1. */
export const rampAt = (key, x) => levelLuma(rampRaw(key, x));
/** The road's ramp colour at x: levelled both ways into the road band (roadLevel), sRGB 0..1. */
export const roadRampAt = (key, x) => roadLevel(rampRaw(key, x));

/** The ramp of every section as RGBA bytes, rows dawn, deep, afterglow (SECTION_KEYS order), n wide. at: rampAt or roadRampAt. */
export function rampBytes(n = SPECTRUM_LOOK.rampN, at = rampAt) {
  const out = new Uint8Array(n * SECTION_KEYS.length * 4);
  SECTION_KEYS.forEach((k, row) => {
    for (let i = 0; i < n; i++) {
      const c = at(k, (i + 0.5) / n), o = (row * n + i) * 4;
      out[o] = Math.round(c[0] * 255); out[o + 1] = Math.round(c[1] * 255); out[o + 2] = Math.round(c[2] * 255); out[o + 3] = 255;
    }
  });
  return out;
}
export const RAMP_ROWS = SECTION_KEYS;

/** The track colour at lap metres s and time t seconds for a section: [r, g, b] sRGB 0..1. */
export function hueAt(s, t = 0, section = 'dawn') {
  const L = SPECTRUM_LOOK;
  return rampAt(sectionKey(section), (Number(s) || 0) / L.rampM + (Number(t) || 0) / L.hueCycleS);
}

/** A prop colour: the index walks the section's stops by the golden ratio, so picks follow the weights. */
export function propColour(index, section = 'dawn') {
  const x = fract(0.5 + (Number(index) || 0) * 0.6180339887498949), st = stops(section);
  const hit = st.find(([a, b]) => x >= a && x < b) || st[st.length - 1];
  return hit[2];
}

/** A period close to `base` that divides the lap exactly (so a pattern never jumps at the line). */
export function fitPeriod(lengthM, base) {
  if (!(lengthM > 0) || !(base > 0)) return base;
  return lengthM / Math.max(1, Math.round(lengthM / base));
}

/** Pattern zones over the lap: [{ fromS, toS, pattern (index into PATTERNS), section }], in s order. */
export function patternZones(sections) {
  const zones = [];
  for (const sec of sections || []) {
    const key = sectionKey(sec), list = SECTION_PATTERNS[key];
    const a = sec.fromS, len = (sec.toS - sec.fromS) / list.length;
    list.forEach((p, i) => zones.push({ fromS: a + i * len, toS: a + (i + 1) * len, pattern: PATTERNS.indexOf(p), section: key }));
  }
  return zones;
}

/**
 * What the road shows at s: { a, b, edgeS }: pattern a before the boundary edgeS, b after it. Away from
 * any boundary (further than morphM) a === b. The lap wraps: the boundary at lengthM is also 0.
 */
export function patternAt(s, zones, lengthM, morphM = SPECTRUM_LOOK.morphM) {
  if (!zones.length) return { a: 0, b: 0, edgeS: s };
  const L = lengthM, n = zones.length;
  const x = ((s % L) + L) % L;
  let i = zones.findIndex((z) => x >= z.fromS && x < z.toS);
  if (i < 0) i = x < zones[0].fromS ? 0 : n - 1;
  const z = zones[i];
  if (z.toS - x < morphM) return { a: z.pattern, b: zones[(i + 1) % n].pattern, edgeS: s + (z.toS - x) };
  if (x - z.fromS < morphM) return { a: zones[(i - 1 + n) % n].pattern, b: z.pattern, edgeS: s - (x - z.fromS) };
  return { a: z.pattern, b: z.pattern, edgeS: s };
}

/** The sweep share of pattern b at lap metres s and lateral u, given patternAt's edgeS (0 = a, 1 = b). */
export function sweepMix(s, u, edgeS, L = SPECTRUM_LOOK) {
  const d = (s - edgeS + Math.abs(u) * L.sweepSlope) / L.sweepSoftM;
  const t = Math.max(0, Math.min(1, d * 0.5 + 0.5));
  return smooth(t);
}

/**
 * The per-metre pattern table as RGBA bytes (one texel per metre of s, n = ceil(lengthM)):
 * R = a * 40, G = b * 40, B = 128 + (edgeS - texel centre) * 2 (0.5 m steps, +-63 m), A = 255.
 */
// Stay within the default WebGPU texture limit, including long courses.
// A GPU under that limit (WebGL2 at 4096) cannot hold a long course's table: the texture fails, its reads come back 0, and a
// road width of 0 paints the whole road as white edge line (emulator, 2026-10-31). The renderer names the real limit before any
// course builds. Paint only: the course hash never reads this.
let trackTableLimit = 8192;
export const setTrackTableLimit = (max) => { trackTableLimit = Number.isFinite(max) && max >= 2 ? Math.min(8192, Math.floor(max)) : 8192; };
export const trackTableWidth = lengthM => Math.max(2, Math.min(trackTableLimit, Math.ceil(lengthM)));

export function patternBytes(zones, lengthM, morphM = SPECTRUM_LOOK.morphM) {
  const n = trackTableWidth(lengthM), out = new Uint8Array(n * 4), step = lengthM / n;
  for (let i = 0; i < n; i++) {
    const c = (i + 0.5) * step, p = patternAt(c, zones, lengthM, morphM);
    out[i * 4] = p.a * 40; out[i * 4 + 1] = p.b * 40;
    out[i * 4 + 2] = Math.max(0, Math.min(255, Math.round(128 + (p.edgeS - c) * 2)));
    out[i * 4 + 3] = 255;
  }
  return out;
}

/** Flow speed of the road pattern toward the player for a kart speed (m/s); Gentle halves it via motion. */
export function flowMps(speedMps, motion = 1, L = SPECTRUM_LOOK) {
  const v = Math.max(0, Math.min(L.flowMaxMps, Number(speedMps) || 0));
  return (L.flowBaseMps + L.flowSpeedMul * v) * Math.max(0, motion);
}

/** Share of weight on the dominant hues (pink, magenta, violet: the first three) of a section. */
export function dominantShare(key) {
  const w = SPECTRUM_WEIGHTS[sectionKey(key)], sum = w.reduce((a, b) => a + b, 0);
  return (w[0] + w[1] + w[2]) / sum;
}
