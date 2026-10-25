/* Field platforms share the authored stone model. Challenge fields use coherent islands; other fields retain tiles. Scale one
 * coherent model to each broad collision deck, including its outer lip. Positions remain tied to
 * the static physics surface. Landing motion only changes render transforms, respects pause and
 * Gentle, and returns every platform to its starting pose on restart.
 */
import { Group, Mesh, InstancedMesh, BoxGeometry, BufferGeometry, Float32BufferAttribute, LineSegments, LineBasicMaterial, MeshBasicNodeMaterial, Matrix4, Vector3, Quaternion, Color } from 'three/webgpu';
import { vec2, vec3, attribute, positionGeometry, normalize, normalWorld, dot, clamp, mix } from 'three/tsl';
import { LOOK } from './look.js';
import { STONE_HEX } from '../track/courts.js';
import { createStoneHover } from './stoneHover.js';
import { machineSurface } from './mainframeMaterials.js';
import { desiredPlatformDepth, platformBodyGeometry } from './platformDepth.js';
import { visualClearanceSamples, capVisualDepth } from '../track/visualDepth.js';

export const STONE_KIT = Object.freeze({ modelId: 'stone-threshold', deckM: 0.55, slab: Object.freeze([5, 0.8, 5]),
  rim: '#f3e2ce', brass: '#cda15f' });

/** One row per legacy tile or whole hex: { id, field, s (centre), x, w, len, p (root), b, n, t }. Pure. */
export function stoneLayout(track, deckM = STONE_KIT.deckM) {
  const rows = [];
  // a stone longer than the model is covered by tiles spaced evenly along it, each squeezed along the road to fit
  const tileM = STONE_KIT.slab[2];
  for (const field of track?.fields || []) {
    for (const st of field.stones || []) {
      const broad = !!field.platforming || (track.id === 'keep' || track.id === 'boudoir') && st.w > 8;
      const hex = st.shape === 'hex', tiles = (hex || broad) ? 1 : Math.max(1, Math.round(st.len / tileM)), len = st.len / tiles;
      for (let k = 0; k < tiles; k++) {
        const s = st.s + len * (k + 0.5), f = track.frameAt(s);
        rows.push({ id: tiles > 1 ? st.id + ':' + k : st.id, stone: st.id, tile: k, tiles, field: field.id, s, x: st.x, w: st.w, len,
          shape: st.shape, source: st, scaleT: len / tileM, scaleX: st.w / STONE_KIT.slab[0], broad,
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
    // Opaque deck and rim occupy their own footprints, avoiding distant depth ties.
    face(rings[0], ring(.985, 0));
    face(ring(.955, 0), ring(.89, 0));
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

// The compact is round, while the authored landing surface is rectangular. Its gilded tray
// covers every supported corner; only the underside bevel narrows. The inset sits 4cm below
// the mirror, avoiding coplanar faces while keeping the driving surface visually continuous.
function compactTrayGeometry() {
  const points = [], colours = [], gold = new Color('#cda15f'), blush = new Color('#b87796'), shadow = new Color('#765044');
  const ring = (half, height) => [[-half, height, -half], [-half, height, half], [half, height, half], [half, height, -half]];
  const quad = (a, b, c, d, colour) => {
    for (const p of [a, b, c, a, c, d]) { points.push(...p); colours.push(colour.r, colour.g, colour.b); }
  };
  const top = ring(2.5, STONE_KIT.deckM), inset = ring(2.36, STONE_KIT.deckM - .04), shoulder = ring(2.5, .34), base = ring(2.42, .04);
  quad(...inset, blush);
  for (let i = 0; i < 4; i++) {
    const j = (i + 1) % 4;
    quad(top[i], top[j], inset[j], inset[i], gold);
    quad(top[i], shoulder[i], shoulder[j], top[j], gold);
    quad(shoulder[i], base[i], base[j], shoulder[j], shadow);
  }
  quad(base[3], base[2], base[1], base[0], shadow);
  const geometry = new BufferGeometry();
  geometry.setAttribute('position', new Float32BufferAttribute(points, 3));
  geometry.setAttribute('color', new Float32BufferAttribute(colours, 3));
  geometry.computeVertexNormals(); geometry.computeBoundingBox(); geometry.computeBoundingSphere(); return geometry;
}

export function buildStones(track, art, pal) {
  const group = new Group(); group.name = 'stepping-stones';
  const rows = stoneLayout(track), hexes = rows.filter((r) => r.shape === 'hex'), tiled = rows.filter((r) => r.shape !== 'hex'), owned = [], geometries = [], moving = [];
  const clearance = rows.length ? visualClearanceSamples(track) : [];
  for (const r of rows) {
    const radius = r.source.spin ? Math.hypot(r.w,r.len)/2 : null;
    r.depthM = capVisualDepth(r,desiredPlatformDepth(r,track.id),clearance,
      {halfWidth:radius||r.w/2,halfLength:radius||r.len/2,motionM:.45,ignoreId:r.stone});
  }
  const basis = new Matrix4(), rotation = new Quaternion();
  const hexMeshes = [], spin = new Quaternion(), up = new Vector3(0,1,0), hexAges = hexes.map(()=>Infinity), lightColor = new Color();
  const x = new Vector3(), y = new Vector3(), z = new Vector3();
  const model = tiled.length && art && typeof art.model === 'function' ? art.model(track.id === 'mainframe' ? 'stone-mainframe' : track.id === 'keep' ? 'stone-keep' : track.id === 'boudoir' ? 'stone-boudoir' : STONE_KIT.modelId) : null;
  let mode = 'none';
  if (tiled.length && model && model.scene) {
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
    if (lip) { lip.name = 'stone-lip'; if (lip.color) lip.color.set(track.id === 'mainframe' ? '#76dfae' : STONE_KIT.brass); owned.push(lip); }
    let compactTray = null, compactPaint = null;
    const bodyGeometry = platformBodyGeometry(track.id), bodyPaint = new MeshBasicNodeMaterial();
    geometries.push(bodyGeometry); owned.push(bodyPaint); bodyPaint.name = 'platform-structural-body';
    const bodyShade = clamp(dot(normalize(normalWorld),normalize(vec3(...LOOK.lightDir))).mul(.42).add(.58),.22,1);
    bodyPaint.colorNode = attribute('color','vec3').mul(bodyShade);
    if (track.id === 'boudoir') {
      compactTray = compactTrayGeometry(); geometries.push(compactTray);
      compactPaint = new MeshBasicNodeMaterial(); compactPaint.name = 'boudoir-compact-tray';
      const shade = clamp(dot(normalize(normalWorld), normalize(vec3(...LOOK.lightDir))).mul(.38).add(.62), .24, 1);
      compactPaint.colorNode = attribute('color', 'vec3').mul(shade); owned.push(compactPaint);
    }
    for (const [index, r] of tiled.entries()) {
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
      if (r.depthM > .05) {
        const hull = new Mesh(bodyGeometry,bodyPaint); hull.name = 'platform-depth:' + r.id;
        hull.scale.y = r.depthM; hull.castShadow = true; holder.add(hull);
      }
      if (compactTray && r.broad) {
        const tray = new Mesh(compactTray, compactPaint); tray.name = 'boudoir-compact-tray'; tray.castShadow = true;
        holder.add(tray);
      }
      if (outlineGeometry) {
        const outline = new LineSegments(outlineGeometry, outlineMaterial); outline.name = 'keep-platform-outline';
        holder.add(outline);
      }
      group.add(holder);
    }
  } else if (tiled.length) {
    mode = 'slab';
    const geometry = new BoxGeometry(...STONE_KIT.slab); geometries.push(geometry);
    const c = new Color(track.id === 'keep' ? '#617faf' : STONE_KIT.rim), base = vec3(c.r, c.g, c.b), material = new MeshBasicNodeMaterial();
    const shade = clamp(dot(normalize(normalWorld), normalize(vec3(...LOOK.lightDir))).mul(0.38).add(0.62), 0.24, 1);
    material.name = 'stone-slab';
    material.colorNode = mix(base, pal?.col ? pal.col(1) : base, 0.015).mul(shade);
    owned.push(material);
    const mesh = new InstancedMesh(geometry, material, tiled.length), matrix = new Matrix4();
    const position = new Vector3(), scale = new Vector3(1, 1, 1);
    mesh.name = 'stone-slab';
    tiled.forEach((r, i) => {
      basis.makeBasis(x.fromArray(r.b), y.fromArray(r.n), z.fromArray(r.t).negate());
      rotation.setFromRotationMatrix(basis);
      // The fallback deepens only downward; its existing top remains exactly where it was.
      const centre = (STONE_KIT.slab[1] - r.depthM) / 2;
      position.set(r.p[0] + r.n[0] * centre, r.p[1] + r.n[1] * centre, r.p[2] + r.n[2] * centre);
      scale.set(r.scaleX || 1, (STONE_KIT.slab[1] + r.depthM) / STONE_KIT.slab[1], r.scaleT || 1);
      matrix.compose(position, rotation, scale); mesh.setMatrixAt(i, matrix);
      if (r.broad) moving.push({ row: r, mesh, index: i, p: position.clone(), q: rotation.clone(), scale: scale.clone(), age: Infinity });
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
      const base = vec3(c.r, c.g, c.b);
      // The normalized source plate follows the hex through rotation; no additional mesh or texture.
      material.colorNode = (track.id === 'mainframe' && kind === 'deck'
        ? machineSurface(positionGeometry.xz.mul(vec2(18,25)), base, art?.detail?.('detail-gold-wear')) : base).mul(shade);
      geometries.push(geometry); owned.push(material);
      const mesh = new InstancedMesh(geometry, material, hexes.length), matrix = new Matrix4(), position = new Vector3(), scale = new Vector3();
      mesh.name = material.name;
      hexes.forEach((r, i) => {
        basis.makeBasis(x.fromArray(r.b), y.fromArray(r.n), z.fromArray(r.t).negate()); rotation.setFromRotationMatrix(basis);
        const hidden = kind === 'hull' && r.depthM <= .05;
        matrix.compose(position.fromArray(r.p), rotation, scale.set(hidden ? 0 : r.w, kind === 'hull' ? r.depthM / 3.2 : 1, hidden ? 0 : r.len)); mesh.setMatrixAt(i, matrix);
        mesh.setColorAt(i, lightColor.setRGB(1,1,1));
      });
      mesh.instanceMatrix.needsUpdate = true; mesh.computeBoundingSphere(); mesh.castShadow = true; group.add(mesh);
      hexMeshes.push(mesh); mesh.boundingSphere.radius += 12;
    }
  }
  const hover = track.id === 'mainframe' && hexes.length ? createStoneHover(hexes) : null;
  if (hover) group.add(hover.object);
  const matrix = new Matrix4(), position = new Vector3(), scale = new Vector3();
  const motionQ = new Quaternion(), rockQ = new Quaternion(), motionN = new Vector3(), motionAxis = new Vector3(1, 0, 0), motionP = new Vector3(), motionM = new Matrix4();
  function applyMotion(m, depth = 0, rock = 0) {
    motionP.copy(m.p).addScaledVector(motionN.fromArray(m.row.n), -depth);
    motionQ.copy(m.q).multiply(rockQ.setFromAxisAngle(motionAxis, rock));
    if (m.object) { m.object.position.copy(motionP); m.object.quaternion.copy(motionQ); }
    else { motionM.compose(motionP, motionQ, m.scale); m.mesh.setMatrixAt(m.index, motionM); m.mesh.instanceMatrix.needsUpdate = true; }
  }
  let hoverTime = 0;
  function update(dt, view = {}) {
    if (view.paused || disposed) return;
    const quiet = !!(view.gentle || view.reducedMotion);
    hoverTime += Math.max(0, dt) * (quiet ? .5 : 1);
    hexes.forEach((r, i) => {
      let glow = 0;
      if (Number.isFinite(hexAges[i])) {
        const age = hexAges[i] += Math.max(0, dt), rise = quiet ? .45 : .3;
        const u = age < rise ? age / rise : Math.max(0, 1 - (age - rise) / 1.5);
        glow = u * u * (3 - 2 * u);
        if (age >= rise + 1.5) hexAges[i] = Infinity;
      }
      const depth = (quiet ? .03 : .22) * glow;
      const rock = quiet ? 0 : (.0015 * Math.sin(hoverTime * .6 + i) + .001 * glow * Math.sin(hoverTime * 7));
      basis.makeBasis(x.fromArray(r.b), y.fromArray(r.n), z.fromArray(r.t).negate());
      rotation.setFromRotationMatrix(basis).multiply(spin.setFromAxisAngle(up, r.source.angle || 0))
        .multiply(rockQ.setFromAxisAngle(motionAxis, rock));
      position.fromArray(r.p).addScaledVector(motionN.fromArray(r.n), -depth);
      for (const mesh of hexMeshes) {
        const hull = mesh.name === 'stone-hex-hull', hidden = hull && r.depthM <= .05;
        matrix.compose(position, rotation, scale.set(hidden ? 0 : r.w,hull ? r.depthM / 3.2 : 1,hidden ? 0 : r.len));
        mesh.setMatrixAt(i, matrix);
        const gain = mesh.name === 'stone-hex-rim' ? 1.8 : mesh.name === 'stone-hex-deck' ? 1.5 : .45;
        lightColor.setRGB(1 + glow * gain * .55, 1 + glow * gain, 1 + glow * gain * .9);
        mesh.setColorAt(i, lightColor);
      }
    });
    for (const mesh of hexMeshes) { mesh.instanceMatrix.needsUpdate = true; mesh.instanceColor.needsUpdate = true; }
    hover?.update(dt, quiet);
    for (const m of moving) {
      if (!Number.isFinite(m.age)) continue;
      m.age += Math.max(0, dt);
      if (m.age >= 1.5) { m.age = Infinity; applyMotion(m); continue; }
      const rise = quiet ? .45 : .3;
      const u = m.age < rise ? m.age / rise : 1 - (m.age - rise) / (1.5 - rise);
      const shape = u * u * (3 - 2 * u), depth = (quiet ? .04 : .28) * shape;
      const safeRock = Math.max(0, Math.min(.012, depth / (m.row.len / 2 + 1), (.3 - depth) / (m.row.len / 2 + 1)));
      applyMotion(m, depth, quiet ? 0 : safeRock * Math.sin(m.age * 9));
    }
  }
  function hit(event) {
    const id = typeof event === 'string' ? event : event?.id;
    let row = null;
    for (const m of moving) if (m.row.stone === id) { if (!Number.isFinite(m.age)) m.age = 0; row = m.row; }
    const index=hexes.findIndex(r=>r.stone===id);
    if(index>=0) { if(!Number.isFinite(hexAges[index])) hexAges[index]=0; row=hexes[index]; }
    return row;
  }
  function reset() {
    for (const m of moving) { m.age = Infinity; applyMotion(m); }
    hoverTime = 0; hexAges.fill(Infinity);
    for(const mesh of hexMeshes) { hexes.forEach((_,i)=>mesh.setColorAt(i,lightColor.setRGB(1,1,1))); mesh.instanceColor.needsUpdate=true; }
  }
  let disposed = false;
  return { group, rows, mode, count: rows.length, update, hit, reset,
    dispose() {
      if (disposed) return; disposed = true;
      group.removeFromParent(); group.traverse((o) => { if (o.isInstancedMesh) o.dispose(); });
      geometries.forEach((g) => g.dispose()); owned.forEach((m) => m.dispose());
      hover?.dispose();
    } };
}
