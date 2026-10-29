/* render/edgesMath.js - the edge language numbers and the pure part of render/edges.js (no three.js, node tests).
 *
 * EDGES lane (owner 2026-10-23: "it's hard to tell where the guardrails are and where they are not"). Shape and
 * pattern carry the difference, colour only helps:
 * - a RAILED edge is a solid 1.2 m barrier: a more opaque glass body, a bright top line and posts at a steady
 *   rhythm (render/materials/rails.js reads EDGE_LOOK.rail), and a tall end post wherever a rail run stops;
 * - an OPEN edge is a diagonal hazard strip painted along the road edge, a bright lip line on the very edge and a
 *   soft haze curtain falling away below the lip (track/mesh.js builds edgeStrip, edgeHaze and railCaps).
 * Photosafe: nothing here flashes or pulses on its own. Patterns are fixed in the world; where a pattern passes
 * the eye fast (near the camera) its contrast drops (nearM) and where it gets denser than a few pixels it melts
 * into its average (fwidth), so no point swaps light for dark faster than about 3 times a second over a large
 * area. The haze streaks drift down at hazeDriftMps (well under 1 Hz per point). Gentle keeps every part
 * visible, only calmer (gentleContrast, the drift halved). The music pulse (render/musicRoad.js rails pulse)
 * lifts the hot colour a little, like the rails.
 */

/** Look numbers; LOOK.edges (render/look.js) overrides any key. */
export const EDGE_LOOK = Object.freeze({
  // open edge strip (u = metres in from the edge)
  lipM: 0.2, stripePeriodM: 2.4, stripeSlant: 1.1, stripeDuty: 0.5, stripeAlpha: [0.82, 0.95], innerSoftM: 0.25,
  nearM: [3, 14], nearContrast: 0.35, denseFrom: [0.18, 0.45],
  hot: 0.55,                 // share of white mixed into the palette colour for the bright parts
  dark: '#160b24',           // the dark stripe: deep enough to read on the light dawn road, still a colour on the deep one
  // haze curtain below an open lip (u = metres down)
  hazeAlpha: 0.5, hazePow: 1.6, hazeTopM: 0.18, hazeStreakM: 1.7, hazeStreakGain: 0.35, hazeDriftMps: 0.5,
  // rail end posts (u = share of the post height)
  capTop: 0.84, capBandAt: 0.62, capBandW: 0.06,
  rampM: 60, pulseGain: 0.5,
  gentleContrast: 0.6, gentleDrift: 0.5,
  // the rail barrier (render/materials/rails.js): body, top line, posts
  rail: Object.freeze({ bodyAlpha: 0.3, topM: 0.14, topAlpha: 0.95, topWhite: 0.6, postM: 3, postW: 0.16, postAlpha: 0.8, postWhite: 0.4,
    postDense: [0.12, 0.35], baseM: 0.08, baseAlpha: 0.7 }),
});

/** Part kinds in the merged edge geometry (the `kind` vertex attribute). */
export const EDGE_KIND = Object.freeze({ strip: 0, haze: 1, cap: 2 });

/**
 * Merge the track's edge MeshData (track/mesh.js edgeStrip, edgeHaze, railCaps) into one set of arrays with a
 * per-vertex kind, so the whole edge language is one draw call. Missing or empty parts are skipped.
 * @returns {{positions, normals, uvs, kinds: Float32Array, indices: Uint32Array, counts: object}|null}
 */
export function mergeEdgeData(meshes) {
  const parts = [['strip', meshes && meshes.edgeStrip], ['haze', meshes && meshes.edgeHaze], ['cap', meshes && meshes.railCaps]]
    .filter(([, d]) => d && d.indices && d.indices.length);
  if (!parts.length) return null;
  let V = 0, I = 0;
  for (const [, d] of parts) { V += d.positions.length / 3; I += d.indices.length; }
  const positions = new Float32Array(V * 3), normals = new Float32Array(V * 3), uvs = new Float32Array(V * 2), kinds = new Float32Array(V);
  const indices = new Uint32Array(I), counts = {};
  let v0 = 0, i0 = 0;
  for (const [name, d] of parts) {
    const n = d.positions.length / 3;
    positions.set(d.positions, v0 * 3); normals.set(d.normals, v0 * 3); uvs.set(d.uvs, v0 * 2);
    kinds.fill(EDGE_KIND[name], v0, v0 + n);
    for (let k = 0; k < d.indices.length; k++) indices[i0 + k] = d.indices[k] + v0;
    counts[name] = d.indices.length / 3;
    v0 += n; i0 += d.indices.length;
  }
  return { positions, normals, uvs, kinds, indices, counts };
}

/** Hazard stripe value 0..1 at (u, v) metres, before anti-aliasing (the shader's pattern, for tests). */
export function stripeAt(u, v, L = EDGE_LOOK) {
  const x = (v + u * L.stripeSlant) / L.stripePeriodM;
  return (x - Math.floor(x)) < L.stripeDuty ? 1 : 0;
}

