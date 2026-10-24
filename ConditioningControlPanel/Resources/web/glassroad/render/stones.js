/* Field platforms share the authored stone model. Threshold keeps its 5m tiles; Keep scales one
 * coherent model to each broad collision deck, including its outer lip. Positions remain tied to
 * the static physics surface. Landing motion only changes render transforms, respects pause and
 * Gentle, and returns every platform to its starting pose on restart.
 */
import { Group, InstancedMesh, BoxGeometry, BufferGeometry, Float32BufferAttribute, LineSegments, LineBasicMaterial, MeshBasicNodeMaterial, Matrix4, Vector3, Quaternion, Color } from 'three/webgpu';
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
      const broad = track.id === 'keep' && st.w > 8;
      const tiles = broad ? 1 : Math.max(1, Math.round(st.len / tileM)), len = st.len / tiles;
      for (let k = 0; k < tiles; k++) {
        const s = st.s + len * (k + 0.5), f = track.frameAt(s);
        rows.push({ id: tiles > 1 ? st.id + ':' + k : st.id, stone: st.id, tile: k, tiles, field: field.id, s, x: st.x, w: st.w, len,
          scaleT: len / tileM, scaleX: st.w / STONE_KIT.slab[0], broad,
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

// The authored Keep profile is an eight-sided plate with chamfered corners. These one-pixel
// strokes trace its existing bevels and seams, sharing a single geometry across all platforms.
function keepEdgeGeometry() {
  const corners = [[-2.25, -2.5], [2.25, -2.5], [2.5, -2.25], [2.5, 2.25], [2.25, 2.5], [-2.25, 2.5], [-2.5, 2.25], [-2.5, -2.25]];
  const points = [];
  for (const [scale, height] of [[1.001, .45], [.968, .805], [.932, .805], [.90, .557]]) {
    for (let i = 0; i < corners.length; i++) for (const p of [corners[i], corners[(i + 1) % corners.length]]) points.push(p[0] * scale, height, p[1] * scale);
  }
  for (const z of [-.9, .9]) points.push(-1.8, .559, z, 1.8, .559, z);
  const geometry = new BufferGeometry(); geometry.setAttribute('position', new Float32BufferAttribute(points, 3));
  geometry.computeBoundingSphere(); return geometry;
}

export function buildStones(track, art, pal) {
  const group = new Group(); group.name = 'stepping-stones';
  const rows = stoneLayout(track), owned = [], geometries = [], moving = [];
  const basis = new Matrix4(), rotation = new Quaternion();
  const x = new Vector3(), y = new Vector3(), z = new Vector3();
  const model = rows.length && art && typeof art.model === 'function' ? art.model(track.id === 'keep' ? 'stone-keep' : STONE_KIT.modelId) : null;
  let mode = 'none';
  if (rows.length && model && model.scene) {
    mode = 'model';
    // the lip tint is one shared material: every clone swaps its lip meshes to it (shared geometry, one program)
    const slots = slotMaterials(model.scene), lipSrc = slots.get('lip'), keepPaint = new Map();
    if (track.id === 'keep') for (const [name, source] of slots) {
      const tint = source.clone();
      tint.color?.set(({ surface: '#617faf', lip: '#ff2a9d', underside: '#11121b', detail: '#d4bdd8' })[name] || '#617faf');
      if ('metalness' in tint) tint.metalness = name === 'lip' ? .08 : .18;
      if ('roughness' in tint) tint.roughness = name === 'lip' ? .42 : .6;
      keepPaint.set(name, tint); owned.push(tint);
    }
    const faceAlt = track.id === 'keep' ? keepPaint.get('surface')?.clone() : null;
    if (faceAlt) { faceAlt.color?.set('#8e557d'); owned.push(faceAlt); }
    let outlineGeometry = null, outlineMaterial = null;
    if (track.id === 'keep') {
      outlineGeometry = keepEdgeGeometry(); geometries.push(outlineGeometry);
      outlineMaterial = new LineBasicMaterial({ color: '#0d0a14', linewidth: 1 });
      outlineMaterial.name = 'keep-platform-ink'; owned.push(outlineMaterial);
    }
    const lip = lipSrc ? lipSrc.clone() : null;
    if (lip) { lip.name = 'stone-lip'; if (lip.color) lip.color.set(STONE_KIT.brass); owned.push(lip); }
    for (const [index, r] of rows.entries()) {
      const holder = new Group(), body = model.scene.clone(true);
      holder.name = 'stone:' + r.id;
      basis.makeBasis(x.fromArray(r.b), y.fromArray(r.n), z.fromArray(r.t).negate());
      holder.quaternion.setFromRotationMatrix(basis);
      holder.position.fromArray(r.p);
      holder.scale.set(r.scaleX, 1, r.scaleT);
      if (r.broad) moving.push({ row: r, object: holder, p: holder.position.clone(), q: holder.quaternion.clone(), age: Infinity });
      body.traverse((o) => {
        if (!o.isMesh) return;
        o.castShadow = true;
        if (faceAlt && index % 2 && o.material?.name === 'surface') o.material = faceAlt;
        else if (keepPaint.has(o.material?.name)) o.material = keepPaint.get(o.material.name);
        else if (lip && o.material && o.material.name === 'lip') o.material = lip;
      });
      holder.add(body);
      if (outlineGeometry) {
        const outline = new LineSegments(outlineGeometry, outlineMaterial); outline.name = 'keep-platform-outline';
        holder.add(outline);
      }
      group.add(holder);
    }
  } else if (rows.length) {
    mode = 'slab';
    const geometry = new BoxGeometry(...STONE_KIT.slab); geometries.push(geometry);
    const c = new Color(track.id === 'keep' ? '#617faf' : STONE_KIT.rim), base = vec3(c.r, c.g, c.b), material = new MeshBasicNodeMaterial();
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
      scale.set(r.scaleX || 1, 1, r.scaleT || 1);
      matrix.compose(position, rotation, scale); mesh.setMatrixAt(i, matrix);
      if (r.broad) moving.push({ row: r, mesh, index: i, p: position.clone(), q: rotation.clone(), scale: scale.clone(), age: Infinity });
    });
    mesh.instanceMatrix.needsUpdate = true; mesh.computeBoundingSphere(); mesh.castShadow = true;
    group.add(mesh);
  }
  const motionQ = new Quaternion(), rockQ = new Quaternion(), motionN = new Vector3(), motionAxis = new Vector3(1, 0, 0), motionP = new Vector3(), motionM = new Matrix4();
  function applyMotion(m, depth = 0, rock = 0) {
    motionP.copy(m.p).addScaledVector(motionN.fromArray(m.row.n), -depth);
    motionQ.copy(m.q).multiply(rockQ.setFromAxisAngle(motionAxis, rock));
    if (m.object) { m.object.position.copy(motionP); m.object.quaternion.copy(motionQ); }
    else { motionM.compose(motionP, motionQ, m.scale); m.mesh.setMatrixAt(m.index, motionM); m.mesh.instanceMatrix.needsUpdate = true; }
  }
  function update(dt, view = {}) {
    if (view.paused) return;
    const quiet = !!(view.gentle || view.reducedMotion);
    for (const m of moving) {
      if (!Number.isFinite(m.age)) continue;
      m.age += Math.max(0, dt);
      if (m.age >= 1.5) { m.age = Infinity; applyMotion(m); continue; }
      const u = m.age < .22 ? m.age / .22 : 1 - (m.age - .22) / 1.28;
      const shape = u * u * (3 - 2 * u), depth = (quiet ? .04 : .28) * shape;
      const safeRock = Math.max(0, Math.min(.012, depth / (m.row.len / 2 + 1), (.3 - depth) / (m.row.len / 2 + 1)));
      applyMotion(m, depth, quiet ? 0 : safeRock * Math.sin(m.age * 17));
    }
  }
  function hit(event) {
    const id = typeof event === 'string' ? event : event?.id;
    let row = null;
    for (const m of moving) if (m.row.stone === id) { if (!Number.isFinite(m.age)) m.age = 0; row = m.row; }
    return row;
  }
  function reset() { for (const m of moving) { m.age = Infinity; applyMotion(m); } }
  let disposed = false;
  return { group, rows, mode, count: rows.length, update, hit, reset,
    dispose() {
      if (disposed) return; disposed = true;
      group.removeFromParent(); group.traverse((o) => { if (o.isInstancedMesh) o.dispose(); });
      geometries.forEach((g) => g.dispose()); owned.forEach((m) => m.dispose());
    } };
}
