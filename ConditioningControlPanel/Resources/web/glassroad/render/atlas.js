/* render/atlas.js - pure helpers for Astra's wave 2 particle atlas (`particles-atlas`, ART.md).
 *
 * A 4 x 4 grid of 256 px cells, [col, row] with row 0 at the top of the image, straight alpha,
 * white RGB, coverage in .a. Sampling is clamped to mip 4 so the smallest mips never mix cells;
 * the UVs carry a half-texel inset at that mip. Which cell each particle family draws, and the
 * per-cell look (size share, upright, aligned to its velocity) live here as plain data.
 * No three.js, so tests/render-fxart.test.mjs runs it under bare node. render/particles.js,
 * motes.js and wakes.js turn these into TSL.
 */

import { SCREEN_LOOK, MOTE_TINT } from './items/look.js';
import { LOOK } from './look.js';

export const GRID = 4;
export const ATLAS_PX = 1024;
export const MAX_LOD = 4;

/** The manifest's cells map (art/manifest.json `particles-atlas`); a row's own map wins. */
export const CELLS = Object.freeze({
  disc: [0, 0], spark: [1, 0], glint: [2, 0], glitter: [3, 0],
  petal: [0, 1], heart: [1, 1], ring: [2, 1], smoke: [3, 1],
  drip: [0, 2], shard: [1, 2], frost: [2, 2], swirl: [3, 2],
  bubble: [0, 3], star: [1, 3],
});

/** Art numbers of this lane: they live in render/look.js `LOOK.fxArt` (plain data, no three). */
export const FXART_LOOK = LOOK.fxArt;

/** Cells drawn upright (no spin, no random angle) and cells turned along their screen velocity. */
export const UPRIGHT = Object.freeze(new Set(['drip']));
export const ALIGNED = Object.freeze(new Set(['spark']));

/** Legacy procedural shape when there is no atlas: 0 soft disc, 1 sparkle, 2 petal. */
const LEGACY = Object.freeze({ disc: 0, ring: 0, smoke: 0, bubble: 0, spark: 1, glint: 1, glitter: 1, shard: 1, frost: 1, star: 1, heart: 1, petal: 2, drip: 2, swirl: 2 });
export const legacyShape = (name) => LEGACY[name] ?? 0;

/** Linear cell index (row * 4 + col); an unknown name draws the disc. */
export function cellIndex(name, cells = CELLS) {
  const c = (cells && cells[name]) || (cells && cells.disc) || CELLS.disc;
  return c[1] * GRID + c[0];
}

/** Half a texel at the clamped mip, as a share of one cell. */
export function cellInset(sizePx = ATLAS_PX, maxLod = MAX_LOD) {
  return (0.5 * 2 ** maxLod) / (sizePx / GRID);
}

/**
 * Atlas UV rectangle of a cell with the inset applied. v0 is the cell's top edge: with flipY false
 * (KTX2, the default) image row 0 sits at v = 0, so the top of a cell has the smaller v.
 * @returns {{ u0: number, v0: number, u1: number, v1: number }}
 */
export function cellUV(name, { cells = CELLS, sizePx = ATLAS_PX, maxLod = MAX_LOD, flipY = false } = {}) {
  const i = cellIndex(name, cells), col = i % GRID, row = Math.floor(i / GRID), e = cellInset(sizePx, maxLod);
  const u0 = (col + e) / GRID, u1 = (col + 1 - e) / GRID;
  const top = (row + e) / GRID, bottom = (row + 1 - e) / GRID;
  return flipY ? { u0, v0: 1 - top, u1, v1: 1 - bottom } : { u0, v0: top, u1, v1: bottom };
}

/** Sprite uv (0..1, y up) to atlas uv inside a cell: the same mapping the TSL node uses. */
export function spriteToAtlas(name, x, y, opts = {}) {
  const r = cellUV(name, opts);
  return [r.u0 + (r.u1 - r.u0) * x, r.v0 + (r.v1 - r.v0) * (1 - y)];
}

/** Mip to sample: from the atlas-uv footprint of one pixel, clamped to 0..MAX_LOD. */
export function atlasLod(footprintUv, sizePx = ATLAS_PX, maxLod = MAX_LOD) {
  const t = footprintUv * sizePx;
  return t <= 1 ? 0 : Math.min(maxLod, Math.log2(t));
}

/** Which cells each particle family draws, as [cell, weight] lists (weights need not sum to 1). */
export const FAMILIES = Object.freeze({
  trail: [['disc', 3], ['glitter', 1]],              // boost exhaust
  wisp: [['smoke', 1]],                               // slipstream
  dust: [['ring', 1], ['disc', 2]],                   // landing
  bump: [['shard', 2], ['glint', 1]],
  pop: [['bubble', 2], ['glitter', 1]],               // pickup bubble pop
  box: [['glint', 1], ['glitter', 1], ['star', 1]],   // item box draw
  glass: [['bubble', 1], ['glitter', 1]],             // the Glass shield soaks a hit
  decoy: [['glint', 1], ['glitter', 1]],              // an Echo copy takes the throw
  freeze: [['frost', 3], ['glint', 1]],
  golden: [['star', 2], ['glitter', 1]],
  melt: [['drip', 3], ['disc', 1]],
  // polish 2: every effect that lands sprays its own shards or petals
  flash: [['glint', 2], ['glitter', 2], ['star', 1]],
  spiral: [['swirl', 2], ['petal', 1]],
  brainDrain: [['smoke', 2], ['swirl', 1]],
  blackout: [['smoke', 2], ['glint', 1]],
  pink: [['heart', 3], ['petal', 2]],
  shatter: [['shard', 3], ['glint', 1]],
  surge: [['spark', 2], ['glitter', 1]],
  echo: [['ring', 1], ['glint', 1]],
  flame: [['disc', 3], ['smoke', 1]],                 // the boost flame's body (glitter sparks ride on top)
  ember: [['glitter', 2], ['glint', 1]],              // drift spark embers that float
  streak: [['spark', 1]],                             // slipstream streaks, turned along their motion
  // toys kit (placeholder families until the toys art lands)
  pendulum: [['star', 2], ['glint', 1]],
  disc: [['shard', 3], ['spark', 1]],
  tether: [['heart', 2], ['glint', 1]],
  snap: [['ring', 1], ['spark', 2]],
  bigSpiral: [['swirl', 3], ['glitter', 1]],
});

/** Polish 2 particle numbers (move to LOOK.fx2 when the integrator wants them there; LOOK.fx2 wins). */
export const FX2_LOOK = Object.freeze({
  sparkWhite: 0.2, sparkAccent: 0.15, emberShare: 0.3, emberSizeMul: 0.45, emberLifeMul: 2.2, emberSpeedMul: 0.4,
  flameCore: '#fff6fb', flameGrow: 1.4, flameRateMul: 1.5, flameLifeMul: 1.2, flameSparkShare: 0.3,
  wispStreakShare: 0.5, wispStreakSizeMul: 1.8, phoneExtraMul: 0.5,
});

/** The colours an effect's hit sprays: its mote tint, white, and the tint halfway to white. */
export function hitColours(kind) {
  const t = MOTE_TINT[kind];
  if (!t) return null;
  const c = t.split(',').map(Number), hex = (v) => '#' + v.map((x) => Math.round(x).toString(16).padStart(2, '0')).join('');
  return [hex(c), '#ffffff', hex(c.map((x) => (x + 255) / 2))];
}

/** Drift sparks per tier: streaks, petals by the tier's petal share, glints join at tier 3. */
export function sparkFamily(tier, petalShare = 0) {
  const p = Math.max(0, Math.min(1, petalShare)), rest = 1 - p;
  const list = [['spark', rest * (tier >= 3 ? 0.65 : 1)]];
  if (tier >= 3) list.push(['glint', rest * 0.35]);
  if (p > 0) list.push(['petal', p]);
  return list;
}

/** Pick a cell from a [cell, weight] list with r in [0, 1). */
export function pickCell(list, r) {
  let sum = 0;
  for (const [, w] of list) sum += w;
  let x = Math.max(0, Math.min(0.999999, r)) * sum;
  for (const [name, w] of list) { if (x < w) return name; x -= w; }
  return list.length ? list[list.length - 1][0] : 'disc';
}

/** A burst's family: an explicit opts.family, else told apart by the LOOK colour list it carries. */
export function familyOf(opts, L) {
  if (opts && opts.family && FAMILIES[opts.family]) return opts.family;
  const c = opts && opts.colours;
  if (c && L) {
    if (c === L.bumpColours) return 'bump';
    if (c === L.popColours) return 'pop';
    if (c === L.boxColours) return 'box';
  }
  return 'box';
}

const HIT_KINDS = new Set(['freeze', 'golden', 'melt', 'glass', 'decoy', 'flash', 'spiral', 'brainDrain', 'blackout', 'pink', 'shatter', 'surge', 'echo',
  'pendulum', 'disc', 'tether', 'snap', 'bigSpiral']);
/** The item-hit family for an effect kind or verdict, or null when that hit sprays nothing (mercy, immune). */
export function hitFamily(kind) {
  return HIT_KINDS.has(kind) ? kind : null;
}

/**
 * Palette weights from what render/materials/palette.js `set(from, to, t)` receives:
 * from = id or [[id, w], ...], to = id, t = mix. Returns { id: weight } summing to 1.
 */
export function paletteWeights(from, to, t) {
  const k = Math.max(0, Math.min(1, Number.isFinite(t) ? t : 1)), out = {};
  const list = Array.isArray(from) ? from : [[from, 1]];
  for (const [id, w] of list) out[id] = (out[id] || 0) + w * (1 - k);
  out[to] = (out[to] || 0) + k;
  return out;
}

/** Mote petal and swirl shares from palette weights (petals in afterglow, swirls in deep). */
export function moteMix(weights, A = FXART_LOOK) {
  const ag = Math.max(0, Math.min(1, (weights && weights.afterglow) || 0));
  const dp = Math.max(0, Math.min(1, (weights && weights.deep) || 0));
  const petal = A.motePetal[0] + (A.motePetal[1] - A.motePetal[0]) * ag;
  const swirl = A.moteSwirl[0] + (A.moteSwirl[1] - A.moteSwirl[0]) * dp;
  return { petal, swirl: Math.min(swirl, 1 - petal) };
}

/** Wave 2 mask numbers, read from SCREEN_LOOK when the integrator adds them there. */
export function maskLook(S = SCREEN_LOOK) {
  return {
    frostMask: S.frostMask ?? [0.02, 0.4], frostVeinBand: S.frostVeinBand ?? [0.3, 0.7],
    meltFlowTile: S.meltFlowTile ?? [1, 0.6], meltFlowUvps: S.meltFlowUvps ?? 0.12,
    crackScale: S.crackScale ?? 0.56, crackDark: S.crackDark ?? 0.6, crackLight: S.crackLight ?? 0.1,
    crackTint: S.crackTint ?? '#fff0f8', crackRefractUv: S.crackRefractUv ?? 0.006,
    irisMaskR: S.irisMaskR ?? 0.34, ditherEdge: S.ditherEdge ?? 0.06, ditherOut: S.ditherOut ?? 1.5 / 255, noisePx: S.noisePx ?? 256,
  };
}

/** A fade edge x (0..1) jittered by blue noise j (-0.5..0.5): untouched at 0 and 1, so clear stays clear. */
export const ditherEdge = (x, j, amp) => Math.min(1, Math.max(0, x + j * amp * x * (1 - x) * 4));

// ---- polish 3 "spectacle" (JUICE lane): every feedback effect reads at 1440p at full speed ----

/**
 * The juice numbers of polish 3: multipliers over the LOOK / FXART numbers and the new bursts, rings and
 * marks. LOOK.juice (render/look.js, integrator) overrides any of them. Gentle keeps gentleMul of every
 * growth (a 2.6x spark is 1.8x under Gentle) and gentleCount of every burst count.
 */
export const JUICE_LOOK = Object.freeze({
  gentleMul: 0.5, gentleCount: 0.6, midMul: 0.75, phoneMul: 0.5,
  // drift sparks: bigger, faster, longer; a tier-up bursts in the new tier colour with a halo ring
  sparkSizeMul: 2.6, sparkRateMul: 1.3, sparkSpeedMul: 1.35, sparkLifeMul: 1.5,
  tierBurst: Object.freeze([16, 26, 40]), tierBurstSpeedMps: 7, tierRingM: Object.freeze([2.4, 3.2, 4.2]), tierRingS: 0.4,
  // boost: a big flame of soft tongues and glitter, and a burst with a ring when it starts
  flameSizeMul: 2.6, flameRateMul: 1.6, flameLifeMul: 1.3, flameSpeedMul: 1.3, flameSparkSizeMul: 2.2, flameInherit: 0.85,
  flameAccents: Object.freeze(['#ff8c1a', '#ffd23a', '#ff3355', '#ff2bd6', '#22e6ff']),
  boostBurst: 36, boostBurstSpeedMps: 9, boostBurstSizeM: 0.5, boostRingM: 4.6, boostRingS: 0.42,
  // slipstream: wisps you can see, tinted toward the cool end
  wispSizeMul: 2.6, wispRateMul: 1.6, wispLifeMul: 1.5, wispColours: Object.freeze(['#e8f6ff', '#9fe9ff', '#22e6ff', '#7f9cff']),
  // bursts at a world point, per family: count, size, speed and life multipliers; spectrum = add the spectrum hues
  burst: Object.freeze({
    bump: Object.freeze({ count: 1.3, size: 2, speed: 1.5, life: 1.3, spectrum: true }),
    pop: Object.freeze({ count: 1.6, size: 2.2, speed: 1.4, life: 1.3, spectrum: true }),
    box: Object.freeze({ count: 2, size: 2.4, speed: 1.5, life: 1.3, spectrum: true }),
  }),
  bumpRingM: 4.4, bumpRingS: 0.38, bumpCoreM: 0.7, bumpCoreS: 0.16, bumpStars: 6,
  hitCountMul: 1.8, hitSizeMul: 2.2, hitSpeedMul: 1.4, hitRingM: 5, hitRingS: 0.45,
  // landing: more, bigger dust and a coloured halo; ground rings bigger, brighter, a cooler echo ring
  dustCountMul: 1.5, dustSizeMul: 2.2, dustSpeedMul: 1.6, dustSpectrum: 0.35, landRingM: Object.freeze([2.6, 3.6, 5]),
  ringPool: 16, ringSizeMul: 2, ringGainMul: 2, ringGainMax: 1, ringEcho: 0.6, ringEchoLifeMul: 1.5,
  trailWidthMul: 2, trailAlphaMul: 1.3,
  // speed lines: more, longer, wider soft streaks, spectrum tinted, stronger with speed and boost
  lineCountMul: 1.4, lineLenMul: 1.7, lineWidthM: 0.1, lineAlpha: 0.6, lineRadiusM: Object.freeze([4.5, 12]), lineFromPct: 0.62,
  lineTint: 0.75, lineBoostGain: 0.5, lineBoostLen: 0.6, lineBoostWidth: 0.35, lineGentle: 0.5,
  // ambient motes: a share wears the spectrum (the hue drifts slowly), a little bigger and brighter
  moteSpectrum: 0.45, moteSizeMul: 1.5, moteAlphaMul: 1.35, moteHueHz: 0.02,
});

/** The juice numbers with LOOK.juice over them. */
export const juiceLook = (L = LOOK) => (L && L.juice ? { ...JUICE_LOOK, ...L.juice } : JUICE_LOOK);

/** Fallback spectrum (pink, magenta and violet weighted double: about 40 percent of picks). */
export const JUICE_HUES = Object.freeze(['#ff4fb0', '#ff2bd6', '#9b4dff', '#22e6ff', '#ff4fb0', '#a8ff2e', '#ff2bd6', '#ffd23a',
  '#3d7bff', '#9b4dff', '#ff8c1a', '#14f0c0', '#ff3355']);

const hex2 = (v) => Math.round(Math.max(0, Math.min(1, v)) * 255).toString(16).padStart(2, '0');
function hslHex(h, s, l) {
  const c = (1 - Math.abs(2 * l - 1)) * s, x = c * (1 - Math.abs(((h / 60) % 2) - 1)), m = l - c / 2;
  const [r, g, b] = h < 60 ? [c, x, 0] : h < 120 ? [x, c, 0] : h < 180 ? [0, c, x] : h < 240 ? [0, x, c] : h < 300 ? [x, 0, c] : [c, 0, x];
  return '#' + hex2(r + m) + hex2(g + m) + hex2(b + m);
}

/** One spectrum entry as '#rrggbb': a hex string, [r, g, b] (0..1 or 0..255), a hue (0..1 or degrees), or { hex | h }. */
export function toHex(v) {
  if (typeof v === 'string') return /^#[0-9a-f]{6}$/i.test(v) ? v.toLowerCase() : null;
  if (typeof v === 'number' && Number.isFinite(v)) return hslHex((((v <= 1 ? v * 360 : v) % 360) + 360) % 360, 0.92, 0.58);
  if (Array.isArray(v) && v.length >= 3 && v.every(Number.isFinite)) {
    const k = Math.max(v[0], v[1], v[2]) > 1 ? 1 / 255 : 1;
    return '#' + hex2(v[0] * k) + hex2(v[1] * k) + hex2(v[2] * k);
  }
  if (v && typeof v === 'object') return toHex(v.hex ?? v.h ?? null);
  return null;
}

/** Flatten a spectrum module's SPECTRUM (sections of hue sets, any nesting) into hex strings; [] when unreadable. */
export function spectrumHexes(spec) {
  const out = [], seen = new Set();
  const walk = (v, depth) => {
    if (depth > 4 || v == null) return;
    const h = toHex(v);
    if (h) { if (!seen.has(h)) { seen.add(h); out.push(h); } return; }
    if (Array.isArray(v)) for (const x of v) walk(x, depth + 1);
    else if (typeof v === 'object') for (const x of Object.values(v)) walk(x, depth + 1);
  };
  try { walk(spec, 0); } catch (e) { return []; }
  return out;
}

// render/spectrum.js is the one colour system (integrator, polish 3); JUICE_HUES stays the fallback for a short list
import * as SPEC from './spectrum.js';
const SPEC_HEXES = SPEC && SPEC.SPECTRUM ? spectrumHexes(SPEC.SPECTRUM) : [];
/** The juice spectrum: render/spectrum.js colours when readable (at least 6), else JUICE_HUES. */
export const juiceHues = () => (SPEC_HEXES.length >= 6 ? SPEC_HEXES : JUICE_HUES);

/** The hue angle (degrees) of a '#rrggbb'. */
export function hueOf(hex) {
  const n = parseInt(String(hex).slice(1), 16) || 0, r = ((n >> 16) & 255) / 255, g = ((n >> 8) & 255) / 255, b = (n & 255) / 255;
  const mx = Math.max(r, g, b), d = mx - Math.min(r, g, b);
  if (!d) return 0;
  const h = mx === r ? ((g - b) / d) % 6 : mx === g ? (b - r) / d + 2 : (r - g) / d + 4;
  return (h * 60 + 360) % 360;
}

/** k colours spread round the wheel: the list sorted by hue, then every (n / k)th. */
export function spreadHues(hues, k = 6) {
  const s = [...new Set(hues)].sort((a, b) => hueOf(a) - hueOf(b));
  return Array.from({ length: k }, (_, i) => s[Math.floor(i * s.length / k) % s.length]);
}

/** Count, size, speed and life multipliers (and the spectrum flag) of a burst's family; 1s when it has none. */
export function burstBoost(opts, L = LOOK, J = juiceLook(L)) {
  // an explicit family (item hits, head sprays) only grows when it has its own row: hits grow by hit*Mul
  const c = opts && opts.colours, named = opts && (opts.boost || opts.family);
  const fam = named ? named : c && L ? (c === L.bumpColours ? 'bump' : c === L.popColours ? 'pop' : c === L.boxColours ? 'box' : null) : null;
  const b = fam && J.burst ? J.burst[fam] : null;
  return { family: b ? fam : null, count: b ? b.count : 1, size: b ? b.size : 1, speed: b ? b.speed : 1, life: b ? b.life : 1, spectrum: !!(b && b.spectrum) };
}

/** A growth multiplier under Gentle: keeps J.gentleMul of the growth past 1 (never shrinks below 1). */
export const gentleGrow = (mul, gentle, J = JUICE_LOOK) => (gentle && mul > 1 ? 1 + (mul - 1) * J.gentleMul : mul);

/** The count share of a quality tier: high 1, mid midMul, phone phoneMul. */
export const tierShare = (name, J = JUICE_LOOK) => (name === 'phone' ? J.phoneMul : name === 'mid' ? J.midMul : 1);

// ---- polish 4 "control and payoff" (PARTICLES lane): depth speed field, skid marks, tier sparks, bursts ----

/**
 * Polish 4 particle numbers; LOOK.p4 (render/look.js, integrator) overrides any of them.
 * Drift tiers wear pink, white, gold everywhere (sparks, skid marks, ribbons, the mini-turbo burst), the
 * same fills as the HUD pips (hud/kit.js order: art/ui/drift-pip-pink|white|gold.svg, one to three
 * ticks), so the sparks at the rear wheels read as the tier the pips count
 * (playtest 2026-10-28: the pips alone did not read).
 * The speed field lives in camera space: each element is born fieldNearM..fieldFarM ahead at a screen
 * position whose |x| is never inside the centre third, then streams toward the camera, so it only ever
 * moves outward on screen and leaves past the edge long before it is near the lens.
 */
export const P4_LOOK = Object.freeze({
  tierColours: Object.freeze(['#ff5fa8', '#fff8ef', '#ffd36b']), slideColour: '#bfe4ff',
  // speed field (render/speedlines.js)
  // fixer: the owner still read polish 4's streaks as long straight white lines near the eye, so streaks are
  // short dashes (a fifth of the field, about 2 m at most), the field ends further out, and glitter and soft
  // orbs at depth carry the speed; colours lean on the spectrum tint, not white
  fieldNearM: 8, fieldFarM: 40, fieldEndM: 6, fieldNdcX: Object.freeze([0.38, 1]), fieldNdcY: Object.freeze([-0.75, 0.9]), fieldCentre: 1 / 3,
  fieldShare: Object.freeze({ streak: 0.22, glitter: 0.48, orb: 0.3 }), fieldCountMul: 1.6, fieldCruise: 0.55,
  fieldAlpha: 0.7, fieldFromPct: 0.45, fieldGain: 1.9, fieldBoostFloor: Object.freeze([0.7, 0.85, 1]), boostRiseS: 0.15, boostFallS: 0.45,
  streakLenM: Object.freeze([0.4, 1.1]), streakSpeedLen: 0.8, streakBoostLen: 0.7, streakWidthM: Object.freeze([0.035, 0.08]), streakTaper: 0.85,
  glitterSizeM: Object.freeze([0.09, 0.22]), glitterTwinkleHz: 1.5, orbSizeM: Object.freeze([0.5, 1.4]), orbAlpha: 0.3,
  curveGain: 0.6, curveMaxNdc: 0.22, curveTcS: 0.25, fieldGentle: 0.5, fieldTint: 0.9, fieldBoostTint: 0.6,
  // skid marks (render/skids.js): a pooled ring of ribbon segments, one draw call
  skidCap: Object.freeze({ high: 1200, mid: 720, phone: 360 }), skidStepM: 0.7, skidBreakM: 6, skidWidthM: 0.26, skidLiftM: 0.1,
  skidWheelX: 0.42, skidWheelZ: 0.38, skidRiseS: 0.05, skidHoldS: 1.6, skidLifeS: 3.8, skidAlpha: 0.9, skidHotS: 0.35, skidGentle: 0.5, skidSlideAlpha: 0.8,
  skidGlow: 1.7,
  // drift sparks per tier: size, rate and a share of stars grow with the tier
  // (playtest 2026-10-28: each tier a clear step, so the size and rate ladders climb further than polish 4's)
  sparkTierSize: Object.freeze([1, 1.5, 2.1]), sparkTierRate: Object.freeze([1, 1.4, 1.9]), sparkTierStars: Object.freeze([0, 0.08, 0.18]),
  // mini-turbo burst on drift-boost: a cone thrown back, stars round the saucer, a halo in the tier colour
  turboCount: Object.freeze([24, 40, 64]), turboSpeedMps: Object.freeze([7, 9, 12]), turboRingM: Object.freeze([2.6, 3.6, 5]), turboRingS: 0.42, turboSizeM: 0.42,
  // tricks: confetti thrown up at takeoff, a ring and confetti at the landing
  trickCount: Object.freeze({ good: 22, great: 40 }), trickLandCount: Object.freeze({ good: 30, great: 56 }),
  trickRingM: Object.freeze({ good: 3.4, great: 5.2 }), trickRingS: 0.5, trickSpeedMps: 7, confettiSizeM: 0.5,
  confettiLifeS: 1.3, confettiDrag: 0.25, confettiGrav: 1, turboWhite: 0.12, turboAccent: 0.18,
  confettiCells: Object.freeze(['petal', 'heart', 'glitter', 'star']),
});

/** The polish 4 numbers with LOOK.p4 over them. */
export const p4Look = (L = LOOK) => (L && L.p4 ? { ...P4_LOOK, ...L.p4 } : P4_LOOK);

/** The colour of a drift tier 1..3; tier 0 (sliding, no tier yet) is the pale slide colour. */
export function tierColour(tier, P = P4_LOOK) {
  const t = tier | 0;
  return t >= 1 ? P.tierColours[Math.min(t, 3) - 1] : P.slideColour;
}

const lerpR = (a, r) => a[0] + (a[1] - a[0]) * r;

/** A speed field element from three randoms (0..1) and a side (-1 left, else right): its birth screen spot and depth. */
export function fieldBirth(r1, r2, r3, side, P = P4_LOOK) {
  return { nx: (side < 0 ? -1 : 1) * lerpR(P.fieldNdcX, r1), ny: lerpR(P.fieldNdcY, r2), depth: P.fieldNearM + (P.fieldFarM - P.fieldNearM) * r3 };
}

/**
 * Camera-space point of an element at flow t (0..1) plus `back` metres further along its streak, with the
 * track curve (rad per m, + right) bending the far end. tanH, tanV = half-fov tangents. Mirrors the
 * speedlines.js shader: x never enters the centre third, whatever the curve.
 */
export function fieldPoint(e, t, back, curve, tanH, tanV, P = P4_LOOK) {
  const d = e.depth - t * (e.depth - P.fieldEndM) + back;
  const x0 = e.nx * e.depth * tanH, lim = P.curveMaxNdc * d * tanH, sg = x0 < 0 ? -1 : 1;
  const bend = Math.max(-lim, Math.min(lim, 0.5 * curve * d * d * P.curveGain));
  const x = sg * Math.max((x0 + bend) * sg, d * tanH * P.fieldCentre);
  return { x, y: e.ny * e.depth * tanV, z: -d };
}

/** Speed field density 0..1 from speed share and the eased boost (0..1): cruising tops out at fieldCruise, a boost fills it. */
export function fieldDensity(speedPct, boost, P = P4_LOOK) {
  return Math.min(1, Math.max(0, Math.max(boost, Math.min(P.fieldCruise, ((speedPct || 0) - P.fieldFromPct) * P.fieldGain))));
}

/** Streak length (m) for a length seed 0..1, speed share and eased boost 0..1. */
export function streakLen(r, speedPct, boost, P = P4_LOOK) {
  return lerpR(P.streakLenM, r) * (0.35 + Math.max(0, speedPct || 0) * P.streakSpeedLen) * (1 + boost * P.streakBoostLen);
}

/** The boost target 0..1 of a boost kind: a drift tier boost by its tier floor, any other boost full. */
export function boostTarget(kind, BOOST) {
  if (!kind) return 0;
  return kind >= BOOST.DRIFT1 && kind <= BOOST.DRIFT3 ? P4_LOOK.fieldBoostFloor[kind - BOOST.DRIFT1] : 1;
}

/** The per-tick skid flag: the sim's own field when it has one, else a grounded drift. */
export function skidOn(st, S) {
  if (!st || st[S.respawnPhase] > 0) return false;
  if (S.skid !== undefined) return st[S.skid] > 0.5;
  return st[S.driftDir] !== 0 && st[S.grounded] > 0.5 && !(st[S.airborne] > 0.5);
}

/** Skid mark alpha share at age (s): a quick rise, a hold, then an eased fade to zero at skidLifeS. */
export function skidFade(age, P = P4_LOOK) {
  if (!(age >= 0) || age >= P.skidLifeS) return 0;
  if (age < P.skidRiseS) return age / P.skidRiseS;
  if (age <= P.skidHoldS) return 1;
  const k = (age - P.skidHoldS) / (P.skidLifeS - P.skidHoldS);
  return (1 - k) ** 1.5;
}

/**
 * The skid ring buffer: cap quads of 4 vertices (from left, from right, to left, to right), plain arrays,
 * three-free. pos xyz, meta (birth s, side -1 or 1), col rgb. lay() writes one quad at the head and wraps.
 */
export function createSkidBuffer(cap) {
  const pos = new Float32Array(cap * 12), meta = new Float32Array(cap * 8), col = new Float32Array(cap * 12);
  for (let i = 0; i < cap * 4; i++) { meta[i * 2] = -1e6; meta[i * 2 + 1] = i & 1 ? 1 : -1; }
  const index = new Uint32Array(cap * 6);
  for (let i = 0; i < cap; i++) index.set([0, 1, 2, 2, 1, 3].map((v) => v + i * 4), i * 6);
  let head = 0, laid = 0;
  /** a, b = [x, y, z] ends; ra, rb = unit right vectors; ta, tb = birth times; w = half width; rgb = [r, g, b]. */
  function lay(a, b, ra, rb, ta, tb, w, rgb) {
    const i = head; head = (head + 1) % cap; laid++;
    for (let e = 0; e < 2; e++) {
      const p = e ? b : a, r = e ? rb : ra, t = e ? tb : ta;
      for (let s = 0; s < 2; s++) {
        const v = i * 4 + e * 2 + s, sg = s ? 1 : -1;
        pos[v * 3] = p[0] + r[0] * w * sg; pos[v * 3 + 1] = p[1] + r[1] * w * sg; pos[v * 3 + 2] = p[2] + r[2] * w * sg;
        meta[v * 2] = t; col[v * 3] = rgb[0]; col[v * 3 + 1] = rgb[1]; col[v * 3 + 2] = rgb[2];
      }
    }
    return i;
  }
  function clear() { for (let i = 0; i < cap * 4; i++) meta[i * 2] = -1e6; laid = 0; }
  return { cap, pos, meta, col, index, lay, clear, get head() { return head; }, get laid() { return laid; } };
}

/**
 * One rear wheel's mark: w = its memory ({} at first). Returns the segment start ({ a, ra, ta }) to lay up to
 * p once the wheel has moved skidStepM, else null; a break (off, or a jump over skidBreakM) starts fresh.
 */
export function stepWheel(w, p, right, on, t, P = P4_LOOK) {
  if (!on) { w.has = false; return null; }
  if (w.has) {
    const d = Math.hypot(p[0] - w.p[0], p[1] - w.p[1], p[2] - w.p[2]);
    if (d < P.skidStepM) return null;
    if (d <= P.skidBreakM) {
      const out = { a: w.p, ra: w.r, ta: w.t };
      w.p = p.slice(); w.r = right.slice(); w.t = t;
      return out;
    }
  }
  w.has = true; w.p = p.slice(); w.r = right.slice(); w.t = t;
  return null;
}

/** Trick grade key: 'great', anything else reads as 'good'. */
export const trickKey = (grade) => (grade === 'great' ? 'great' : 'good');

// ---- polish 5 wave 1 (FX lane, V4): a particle is born, it never pops in ----

/**
 * The birth of every particle: over birthS it grows from sizeFrom of its size and from no alpha, eased out,
 * so a spark reads as a glint that opens instead of a hard white tick. Drift spark cells are drawn a touch
 * fatter across their streak (sparkWidth, by drift tier 1..3; anything else 1). The ring buffer keeps a
 * high-water mark: only the slots below it are drawn and uploaded each frame.
 */
export const BIRTH_LOOK = Object.freeze({ birthS: 0.05, sizeFrom: 0.35, sparkWidth: Object.freeze([1.4, 1.15, 1]) });

/** Birth progress 0..1 at age ageS (eased out, so most of the opening is in the first half). */
export function birthK(ageS, B = BIRTH_LOOK) {
  if (!(B.birthS > 0)) return 1;
  const x = Math.min(1, Math.max(0, ageS / B.birthS));
  return 1 - (1 - x) * (1 - x);
}

/** The size share at birth progress k: sizeFrom at k = 0, whole at k = 1. */
export const birthSize = (k, B = BIRTH_LOOK) => B.sizeFrom + (1 - B.sizeFrom) * k;

/** Width across a drift spark streak by tier (1 for tier 0 and every other emitter). */
export function sparkWidth(tier, B = BIRTH_LOOK) {
  const t = tier | 0;
  return t >= 1 ? B.sparkWidth[Math.min(t, 3) - 1] : 1;
}

/**
 * The ring slot for the next particle: the ring runs over [0, hw); while the slot under the head is still alive
 * and there is room below cap, the ring grows into fresh space instead (hw is the high-water mark). Pure: takes
 * and returns { head, hw, i } (i is the slot to write). alive(i) says whether slot i still has life.
 */
export function ringSlot(head, hw, cap, alive) {
  if (hw > cap) hw = cap;
  if (head >= hw) head = 0;
  if (hw < cap && (hw === 0 || alive(head))) return { head, hw: hw + 1, i: hw };
  const i = head;
  return { head: (head + 1) % Math.max(1, hw), hw, i };
}

// ---- polish 5 wave 1 (FX lane, V3): spiral gate particles thin out at the lens ----

/**
 * A gate particle fades and shrinks over the last fromM..toM metres before the camera (distance to the camera),
 * full beyond toM: a landing through a gate keeps the road and the kart readable, the gate far off stays whole.
 */
export const GATE_LENS = Object.freeze({ fromM: 1.5, toM: 12, shrinkTo: 0.45 });

/** The gate lens share 0..1 at camera distance d (smoothstep fromM..toM). */
export function gateLensK(d, G = GATE_LENS) {
  const x = Math.min(1, Math.max(0, (d - G.fromM) / (G.toM - G.fromM)));
  return x * x * (3 - 2 * x);
}

/** The size share at lens share k: shrinkTo at the lens, whole at k = 1. */
export const gateLensSize = (k, G = GATE_LENS) => G.shrinkTo + (1 - G.shrinkTo) * k;
