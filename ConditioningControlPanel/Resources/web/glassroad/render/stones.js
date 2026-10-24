/* render/stones.js - stepping platforms over a field's missing deck. Legacy rectangles use tiled art; giant
 * hexagons use the same six-sided outline as track support. stoneLayout is pure: world position = the frame at
 * the stone's centre + lateral x along the binormal, lowered STONE_KIT.deckM along the normal so the GLB deck
 * (deckHeightM over platform_root, the base centre) sits ON the road surface; basis from the frame.
 * buildStones clones Astra's `stone-threshold` GLB per stone (shared geometry, the lip slot tinted with the court
 * brass so the kicker edge reads from the kart) or, without the model, one instanced slab 5 x 0.8 x 5 in the
 * court rim colours. Hex decks are at the frame surface, with their bevelled machinery below. Four instanced
 * layers share geometry and materials across the field. Every owned resource is disposed with the scene.
 */
import { Group, InstancedMesh, BoxGeometry, BufferGeometry, Float32BufferAttribute, MeshBasicNodeMaterial, Matrix4, Vector3, Quaternion, Color } from 'three/webgpu';
import { vec3, normalize, normalWorld, dot, clamp, mix } from 'three/tsl';
import { LOOK } from './look.js';
import { STONE_HEX } from '../track/courts.js';

export const STONE_KIT = Object.freeze({ modelId: 'stone-threshold', deckM: 0.55, slab: Object.freeze([5, 0.8, 5]),
  rim: '#f3e2ce', brass: '#cda15f' });

/** One row per legacy tile or whole hex: { id, field, s (centre), x, w, len, p (root), b, n, t }. Pure. */
export function stoneLayout(track, deckM = STONE_KIT.deckM) {
  const rows = [];
  // a stone longer than the model is covered by tiles spaced evenly along it, each squeezed along the road to fit
  const tileM = STONE_KIT.slab[2];
  for (const field of track?.fields || []) {
    for (const st of field.stones || []) {
      const hex = st.shape === 'hex', tiles = hex ? 1 : Math.max(1, Math.round(st.len / tileM)), len = st.len / tiles;
      for (let k = 0; k < tiles; k++) {
        const s = st.s + len * (k + 0.5), f = track.frameAt(s);
        rows.push({ id: tiles > 1 ? st.id + ':' + k : st.id, stone: st.id, tile: k, tiles, field: field.id, s, x: st.x, w: st.w, len,
          shape: st.shape, scaleT: len / tileM,
          p: f.p.map((v, i) => v + f.b[i] * st.x - f.n[i] * (hex ? 0 : deckM)), b: [...f.b], n: [...f.n], t: [...f.t], w2: f.w });
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

/** Six-sided machinery with a bevelled keel; every layer stays within the shared support outline. */
function hexGeometry(kind) {
  const points = [], tri = (a, b, c) => points.push(...a, ...b, ...c);
  const ring = (r, y) => STONE_HEX.map(([x, z]) => [x * r, y, z * r]);
  const face = (outer, inner = null) => {
    for (let i = 0; i < 6; i++) {
      const j = (i + 1) % 6;
      if (inner) { tri(outer[i], inner[j], outer[j]); tri(outer[i], inner[i], inner[j]); }
      else tri([0, outer[i][1], 0], outer[j], outer[i]);
    }
  };
  if (kind === 'hull') {
    const rings = [ring(1, 0), ring(1, -0.45), ring(0.94, -2.6), ring(0.82, -3.2)];
    face(rings[0]);
    for (let k = 1; k < rings.length; k++) for (let i = 0; i < 6; i++) {
      const j = (i + 1) % 6, a = rings[k - 1], b = rings[k];
      tri(a[i], a[j], b[j]); tri(a[i], b[j], b[i]);
    }
    const bottom = rings.at(-1);
    for (let i = 0; i < 6; i++) tri([0, -3.2, 0], bottom[i], bottom[(i + 1) % 6]);
  } else if (kind === 'rim') face(ring(0.985, 0.012), ring(0.955, 0.012));
  else if (kind === 'deck') face(ring(0.89, 0.006));
  else {
    face(ring(0.76, 0.014), ring(0.75, 0.014));
    // Radial seams and paired inset contact lights read as manufactured modules from the chase camera.
    for (const side of [-1, 1]) for (const z of [-0.28, -0.14, 0, 0.14, 0.28]) {
      const x = side * 0.29, a = [x - 0.025, 0.016, z - 0.012], b = [x + 0.025, 0.016, z - 0.012];
      const c = [x + 0.025, 0.016, z + 0.012], d = [x - 0.025, 0.016, z + 0.012];
      tri(a, c, b); tri(a, d, c);
    }
  }
  const g = new BufferGeometry(); g.setAttribute('position', new Float32BufferAttribute(points, 3));
  g.computeVertexNormals(); g.computeBoundingBox(); return g;
}

export function buildStones(track, art, pal) {
  const group = new Group(); group.name = 'stepping-stones';
  const rows = stoneLayout(track), hexes = rows.filter((r) => r.shape === 'hex'), tiled = rows.filter((r) => r.shape !== 'hex'), owned = [], geometries = [];
  const basis = new Matrix4(), rotation = new Quaternion();
  const x = new Vector3(), y = new Vector3(), z = new Vector3();
  const model = tiled.length && art && typeof art.model === 'function' ? art.model(track.id === 'mainframe' ? 'stone-mainframe' : STONE_KIT.modelId) : null;
  let mode = 'none';
  if (tiled.length && model && model.scene) {
    mode = 'model';
    // the lip tint is one shared material: every clone swaps its lip meshes to it (shared geometry, one program)
    const slots = slotMaterials(model.scene), lipSrc = slots.get('lip');
    const lip = lipSrc ? lipSrc.clone() : null;
    if (lip) { lip.name = 'stone-lip'; if (lip.color) lip.color.set(track.id === 'mainframe' ? '#76dfae' : STONE_KIT.brass); owned.push(lip); }
    for (const r of tiled) {
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
  } else if (tiled.length) {
    mode = 'slab';
    const geometry = new BoxGeometry(...STONE_KIT.slab); geometries.push(geometry);
    const c = new Color(STONE_KIT.rim), base = vec3(c.r, c.g, c.b), material = new MeshBasicNodeMaterial();
    const shade = clamp(dot(normalize(normalWorld), normalize(vec3(...LOOK.lightDir))).mul(0.38).add(0.62), 0.24, 1);
    material.name = 'stone-slab';
    material.colorNode = mix(base, pal?.col ? pal.col(1) : base, 0.015).mul(shade);
    owned.push(material);
    const mesh = new InstancedMesh(geometry, material, tiled.length), matrix = new Matrix4();
    const position = new Vector3(), scale = new Vector3(1, 1, 1), half = STONE_KIT.slab[1] / 2;
    mesh.name = 'stone-slab';
    tiled.forEach((r, i) => {
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
  if (hexes.length) {
    mode = tiled.length ? 'mixed' : 'hex';
    for (const [kind, tint] of [['hull', '#59716e'], ['rim', '#91f1c4'], ['deck', '#263c3c'], ['detail', '#d9bc72']]) {
      const geometry = hexGeometry(kind), material = new MeshBasicNodeMaterial(), c = new Color(tint);
      material.name = 'stone-hex-' + kind;
      const shade = clamp(dot(normalize(normalWorld), normalize(vec3(...LOOK.lightDir))).mul(0.4).add(0.6), 0.25, 1);
      material.colorNode = vec3(c.r, c.g, c.b).mul(shade);
      geometries.push(geometry); owned.push(material);
      const mesh = new InstancedMesh(geometry, material, hexes.length), matrix = new Matrix4(), position = new Vector3(), scale = new Vector3();
      mesh.name = material.name;
      hexes.forEach((r, i) => {
        basis.makeBasis(x.fromArray(r.b), y.fromArray(r.n), z.fromArray(r.t).negate()); rotation.setFromRotationMatrix(basis);
        matrix.compose(position.fromArray(r.p), rotation, scale.set(r.w, 1, r.len)); mesh.setMatrixAt(i, matrix);
      });
      mesh.instanceMatrix.needsUpdate = true; mesh.computeBoundingSphere(); mesh.castShadow = true; group.add(mesh);
    }
  }
  let disposed = false;
  return { group, rows, mode, count: rows.length,
    dispose() {
      if (disposed) return; disposed = true;
      group.removeFromParent(); group.traverse((o) => { if (o.isInstancedMesh) o.dispose(); });
      geometries.forEach((g) => g.dispose()); owned.forEach((m) => m.dispose());
    } };
}
