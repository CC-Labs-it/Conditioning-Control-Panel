/* render/roadAaMath.js - the road's widen-and-melt anti-aliasing, as plain numbers (three-free, tested in node).
 *
 * The house rule (render/materials/rails.js, render/edges.js): a line narrower than a pixel is widened to a
 * pixel and its light scaled down to match, and once its cells get denser than the pixels it melts to its
 * average. render/materials/road.js builds the same three shapes as TSL nodes from these numbers; px is
 * always fwidth of the coordinate the shape is drawn in (metres or cells), so the result is per pixel.
 *
 *   aaLine(d, w, px, cell)  coverage of a line: d the distance from its centre, w its half width
 *   aaFloor(x, px)          floor(x) with each step blended over about a pixel, a ramp once the steps melt
 *   aaParity(x, px)         floor(x) mod 2 (0 or 1) blended the same way, 0.5 once melted
 */

export const ROAD_AA = Object.freeze({
  lineMul: 1,       // a line is at least this many fwidths wide (half width)
  stepMul: 0.75,    // a hard step blends over +- this many fwidths
  meltFrom: 0.3,    // cells per pixel where a pattern starts to melt to its average
  meltTo: 0.8,      // fully melted
});

const clamp01 = (v) => Math.max(0, Math.min(1, v));
export function smoothstep(a, b, x) { const t = clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }

/** Share melted for a pattern whose cell coordinate moves px per pixel (0 crisp, 1 flat average). */
export function melt(px, A = ROAD_AA) { return smoothstep(A.meltFrom, A.meltTo, Math.abs(px)); }

/**
 * Line coverage 0..1: d distance from the centre line, w the drawn half width, px fwidth of d's coordinate.
 * cell (optional) is the repeat period in the same units: past it the line melts to its average (w / cell:
 * the smoothstep profile's area each side is w / 2, held under 1). Without a cell the line only widens.
 */
export function aaLine(d, w, px, cell = 0, A = ROAD_AA) {
  const wE = Math.max(w, Math.abs(px) * A.lineMul, 1e-6);
  const cov = (1 - smoothstep(0, wE, Math.abs(d))) * (w / wE);
  if (!(cell > 0)) return cov;
  return cov + (Math.min(1, w / cell) - cov) * melt(Math.abs(px) / cell, A);
}

/** floor(x), each step blended over +- stepMul pixels; melts to the ramp x - 0.5 as the steps pass a pixel. */
export function aaFloor(x, px, A = ROAD_AA, m = melt(px, A)) {
  const n = Math.floor(x + 0.5), g = x - n;
  const h = Math.max(1e-4, Math.min(0.5, Math.abs(px) * A.stepMul));
  const stepped = n - 1 + smoothstep(-h, h, g);
  return stepped + (x - 0.5 - stepped) * m;
}

/** floor(x) mod 2, blended: 0 or 1 away from the steps, 0.5 once melted. */
export function aaParity(x, px, A = ROAD_AA) {
  const m = melt(px, A);
  return aaFloor(x, px, A, m) - 2 * aaFloor(x / 2, px / 2, A, m);
}

/** Smooth XOR of two parities 0..1 (the checker and the weave). */
export function xor01(p, q) { return p + q - 2 * p * q; }
