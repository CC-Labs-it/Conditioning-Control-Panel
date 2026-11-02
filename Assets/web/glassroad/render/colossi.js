/* render/colossi.js - hero colossi: giant chess pieces, a pocket watch and a slot cabinet hung in the
 * sky over and beside the road (owner 2026-10-22: "imagine a big king or knight placed diagonally
 * like a slope over the track, projecting a shadow on the track").
 *
 * Layout per track from tracks/<id>.colossi.json, fitted by render/colossiMath.js: an arch leans
 * across the road with its body at least clearM (14 m) above the surface, a tower stands beside it,
 * and neither ever enters the road tube of any stretch. Models are the approved art at lod0, never
 * regenerated; their materials become the soft-shaded twins (materials/colossus.js). Each body turns
 * very slowly about its own axis (the clearance test is a capsule round that axis, so the turn can
 * never bring it into the road). Every mesh casts into the lamp's shadow map (render/shadows.js).
 */
import { Group, Matrix4, Vector3, Color } from 'three/webgpu';
import { uniform } from 'three/tsl';
import { placeColossi, sampleTrack, worldLook, propColour, chessColour, hexOf } from './colossiMath.js';
import { softenAll } from './materials/colossus.js';

const LAYOUTS = new Map();

/** Fetch tracks/<id>.colossi.json once; resolves the layout or null (never rejects). */
export async function loadColossi(trackId) {
  if (LAYOUTS.has(trackId)) return LAYOUTS.get(trackId);
  let data = null;
  try {
    const r = await fetch(new URL('../tracks/' + trackId + '.colossi.json', import.meta.url));
    if (r.ok) data = await r.json();
  } catch { /* no layout: no colossi */ }
  LAYOUTS.set(trackId, data);
  return data;
}
// Threshold is the only track: load it with the module so buildWorld stays synchronous
await loadColossi('threshold');

const M4 = new Matrix4(), VX = new Vector3(), VY = new Vector3(), VZ = new Vector3();

/** A colossus's body colour: the layout's own, else a spectrum pick per index (chess pieces never warm). */
export function colossusColours(spec, i) {
  const chess = String(spec.id || '').startsWith('chess-');
  return { tint: hexOf(spec.tint) || (chess ? chessColour(i * 5 + 2, 'deep') : propColour(i * 5 + 2, 'deep')) };
}

/**
 * art: render/art.js set; track: loaded track; pal: the section palette (materials/palette.js).
 * opts.layout overrides the fetched layout; opts.samples reuses road samples.
 * Returns { object, placed, casters, update(dt, spinMul), dispose } or null (no art, no layout).
 */
export function buildColossi(art, track, pal, { layout = LAYOUTS.get(track.id), samples = null } = {}) {
  if (!art || !layout || !Array.isArray(layout.colossi)) return null;
  const W = worldLook();
  const rowOf = (id) => (art.model(id) && art.row(id)) || null;
  const placed = placeColossi(layout.colossi, track, rowOf, W, samples || sampleTrack(track, W.stepM));
  if (!placed.length) return null;
  const group = new Group(), spinners = [], mats = [];
  group.name = 'colossi';
  placed.forEach((ax, i) => {
    const holder = new Group(), body = art.model(ax.id).scene.clone(true);
    M4.makeBasis(VX.set(...ax.X), VY.set(...ax.Y), VZ.set(...ax.Z));
    holder.quaternion.setFromRotationMatrix(M4);
    holder.position.set(...ax.B);
    body.scale.setScalar(ax.scale);
    // each body its own saturated colour and a bright rim, so it reads as what it is from the road (no
    // seams: flutes up an upright body read as ribbing, polish 3 review)
    const tint = new Color(colossusColours(ax.spec, i).tint);
    const cache = new Map();
    softenAll(body, pal, cache, { tint: uniform(tint), rimGain: W.colossusRimGain });
    for (const m of cache.values()) mats.push(m);
    body.traverse((o) => { if (o.isMesh) o.castShadow = true; });
    holder.add(body); group.add(holder);
    spinners.push({ body, rate: (ax.spec.spinRadps ?? W.colossusSpinRadps ?? 0.012) * (ax.spec.side < 0 ? -1 : 1) });
  });
  return {
    object: group,
    placed,
    casters: [group],
    /** spinMul: the Gentle scroll share (0 holds them still). */
    update(dt, spinMul = 1) {
      if (!spinMul) return;
      for (const s of spinners) s.body.rotation.y = (s.body.rotation.y + s.rate * dt * spinMul) % (Math.PI * 2);
    },
    dispose() { for (const m of mats) m.dispose(); },
  };
}
