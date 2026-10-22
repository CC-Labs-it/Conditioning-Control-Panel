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

const HIT_KINDS = new Set(['freeze', 'golden', 'melt', 'glass', 'decoy', 'flash', 'spiral', 'brainDrain', 'blackout', 'pink', 'shatter', 'surge', 'echo']);
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
