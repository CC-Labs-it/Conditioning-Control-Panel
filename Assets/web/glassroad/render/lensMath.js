/* render/lensMath.js - the lens rule numbers and their JS mirror (no three.js; render/lens.js builds the nodes).
 *
 * Nothing additive near the camera may push a large area of the view to white. A sprite's size is capped
 * to a share of the view height at its own depth (nearFrac within nearM, easing to farFrac by farM, so a
 * rival's stars 10 to 40 m out stay big while the player's own burst stays small), and its alpha fades out
 * inside fadeM and dims to `dim` inside dimM, so sprites that reach the lens never flash it.
 */
export const LENS = Object.freeze({
  nearFrac: 0.055, farFrac: 0.2, nearM: 8, farM: 16,   // size cap: share of the view height, by depth
  fadeM: Object.freeze([0.8, 2.6]),                     // gone at the lens, whole by fadeM[1]
  dimM: Object.freeze([2.5, 6]), dim: 0.6,              // alpha kept at dimM[0], full by dimM[1]
  // the player's own bursts (bumps, hits, sprays right under the camera): this share of the count and stars
  playerBurstMul: 0.5,
  // shockwave rings (thin outlines, little area) may open this many times wider than the sprite cap
  ringCapMul: 3,
});

const sstep = (a, b, x) => { const t = Math.min(1, Math.max(0, (x - a) / (b - a))); return t * t * (3 - 2 * t); };

/** The capped size (world metres) at depth d for a view height viewH1 at 1 m (2 tan(vFov / 2)). */
export function lensCapSize(size, d, viewH1 = 1.4, L = LENS) {
  const frac = L.nearFrac + (L.farFrac - L.nearFrac) * sstep(L.nearM, L.farM, d);
  return Math.min(size, Math.max(0.01, d) * viewH1 * frac);
}

/** Alpha share at depth d: 0 at the lens, dim inside dimM, 1 beyond. */
export function lensAlphaAt(d, L = LENS) {
  return sstep(L.fadeM[0], L.fadeM[1], d) * (L.dim + (1 - L.dim) * sstep(L.dimM[0], L.dimM[1], d));
}
