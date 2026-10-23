/* render/stones.js - the stepping stones of a track field (BRIEF-STONES: the kart drives on 5 x 5 m stones over
 * the void where the bridge deck is gone). stoneLayout is pure: one row per stone, world position = the frame at
 * the stone's centre + lateral x along the binormal, lowered STONE_KIT.deckM along the normal so the GLB deck
 * (deckHeightM over platform_root, the base centre) sits ON the road surface; basis from the frame.
 * buildStones clones Astra's `stone-threshold` GLB per stone (shared geometry, the lip slot tinted with the court
 * brass so the kicker edge reads from the kart) or, without the model, one instanced slab 5 x 0.8 x 5 in the
 * court rim colours. One draw list, disposed with the scene. The void under a field needs nothing: the ground
 * deck below is the view.
 */
import { Group, InstancedMesh, BoxGeometry, MeshBasicNodeMaterial, Matrix4, Vector3, Quaternion, Color } from 'three/webgpu';
import { vec3, normalize, normalWorld, dot, clamp, mix } from 'three/tsl';
import { LOOK } from './look.js';

export const STONE_KIT = Object.freeze({ modelId: 'stone-threshold', deckM: 0.55, slab: Object.freeze([5, 0.8, 5]),
  rim: '#f3e2ce', brass: '#cda15f' });

/** One row per stone of every field: { id, field, s (centre), x, w, len, p (root), b, n, t }. Pure. */
export function stoneLayout(track, deckM = STONE_KIT.deckM) {
  const rows = [];
  // a stone longer than the model is covered by tiles spaced evenly along it, each squeezed along the road to fit
  const tileM = STONE_KIT.slab[2];
  for (const field of track?.fields || []) {
    for (const st of field.stones || []) {
      const tiles = Math.max(1, Math.round(st.len / tileM)), len = st.len / tiles;
      for (let k = 0; k < tiles; k++) {
        const s = st.s + len * (k + 0.5), f = track.frameAt(s);
        rows.push({ id: tiles > 1 ? st.id + ':' + k : st.id, stone: st.id, tile: k, tiles, field: field.id, s, x: st.x, w: st.w, len,
          scaleT: len / tileM,
          p: f.p.map((v, i) => v + f.b[i] * st.x - f.n[i] * deckM), b: [...f.b], n: [...f.n], t: [...f.t], w2: f.w });
      }
    }
  }
  return rows;
}

/** The GLB's materials by slot name (surface, lip, underside, detail), from the loaded scene. */
function slotMaterials(scene) {
  const out = new Map();
  scene.traverse((o) => { if (o.isMesh && o.material && o.material.name && !out.has(o.material.name)) out.set(o.material.name, o.material); });
  return out;
}

export function buildStones(track, art, pal) {
  const group = new Group(); group.name = 'stepping-stones';
  const rows = stoneLayout(track), owned = [], geometries = [];
  const basis = new Matrix4(), rotation = new Quaternion();
  const x = new Vector3(), y = new Vector3(), z = new Vector3();
  const model = rows.length && art && typeof art.model === 'function' ? art.model(STONE_KIT.modelId) : null;
  let mode = 'none';
  if (rows.length && model && model.scene) {
    mode = 'model';
    // the lip tint is one shared material: every clone swaps its lip meshes to it (shared geometry, one program)
    const slots = slotMaterials(model.scene), lipSrc = slots.get('lip');
    const lip = lipSrc ? lipSrc.clone() : null;
    if (lip) { lip.name = 'stone-lip'; if (lip.color) lip.color.set(STONE_KIT.brass); owned.push(lip); }
    for (const r of rows) {
      const holder = new Group(), body = model.scene.clone(true);
      holder.name = 'stone:' + r.id;
      basis.makeBasis(x.fromArray(r.b), y.fromArray(r.n), z.fromArray(r.t).negate());
      holder.quaternion.setFromRotationMatrix(basis);
      holder.position.fromArray(r.p);
      if (Math.abs(r.scaleT - 1) > 1e-6) holder.scale.z = r.scaleT;
      body.traverse((o) => {
        if (!o.isMesh) return;
        o.castShadow = true;
        if (lip && o.material && o.material.name === 'lip') o.material = lip;
      });
      holder.add(body); group.add(holder);
    }
  } else if (rows.length) {
    mode = 'slab';
    const geometry = new BoxGeometry(...STONE_KIT.slab); geometries.push(geometry);
    const c = new Color(STONE_KIT.rim), base = vec3(c.r, c.g, c.b), material = new MeshBasicNodeMaterial();
    const shade = clamp(dot(normalize(normalWorld), normalize(vec3(...LOOK.lightDir))).mul(0.38).add(0.62), 0.24, 1);
    material.name = 'stone-slab';
    material.colorNode = mix(base, pal?.col ? pal.col(1) : base, 0.015).mul(shade);
    owned.push(material);
    const mesh = new InstancedMesh(geometry, material, rows.length), matrix = new Matrix4();
    const position = new Vector3(), scale = new Vector3(1, 1, 1), half = STONE_KIT.slab[1] / 2;
    mesh.name = 'stone-slab';
    rows.forEach((r, i) => {
      basis.makeBasis(x.fromArray(r.b), y.fromArray(r.n), z.fromArray(r.t).negate());
      rotation.setFromRotationMatrix(basis);
      // the box is centred: lift it half its height so its base sits at the root
      position.set(r.p[0] + r.n[0] * half, r.p[1] + r.n[1] * half, r.p[2] + r.n[2] * half);
      scale.set(1, 1, r.scaleT || 1);
      matrix.compose(position, rotation, scale); mesh.setMatrixAt(i, matrix);
    });
    mesh.instanceMatrix.needsUpdate = true; mesh.computeBoundingSphere(); mesh.castShadow = true;
    group.add(mesh);
  }
  let disposed = false;
  return { group, rows, mode, count: rows.length,
    dispose() {
      if (disposed) return; disposed = true;
      group.removeFromParent(); group.traverse((o) => { if (o.isInstancedMesh) o.dispose(); });
      geometries.forEach((g) => g.dispose()); owned.forEach((m) => m.dispose());
    } };
}
