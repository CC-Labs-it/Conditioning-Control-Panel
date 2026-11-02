// Machine departments: all moving bounds are fitted against the complete stacked course.
import { Group, Mesh, InstancedMesh, BoxGeometry, CylinderGeometry, TorusGeometry, SphereGeometry, Matrix4, Quaternion, Vector3, MeshBasicNodeMaterial, DoubleSide } from 'three/webgpu';
import { vec2, vec3, uv, texture, positionWorld, normalWorld, cameraPosition, normalize, max, dot, pow, sin, uniform } from 'three/tsl';
import { sampleTrack, bodyClear, segDist, WORLD_DEFAULTS } from './colossiMath.js';
import { mediaLook } from './mediaMath.js';
import { fitMainframeBillboard, motionEnvelope } from './mainframeMediaMotion.js';
import { createMachineMaterial } from './mainframeMaterials.js';
import { Q } from './quality.js';

export const ROOM_KINDS = Object.freeze({ 'boot-hall': 'control', 'core-approach': 'engine', 'upper-bus': 'containment', 'lower-bus': 'latex', 'return-weave': 'cables' });
export const ROOM_CLEARANCE = Object.freeze({ ...WORLD_DEFAULTS, sideM: 3, clearM: 14, underM: 3 });
export const mainframeRoomWindows = track => track.sections.filter(s => ROOM_KINDS[s.id]).map(s => ({ ...s, kind: ROOM_KINDS[s.id] }));
const local = (f, x, y, z = 0) => f.p.map((v, k) => v + f.b[k] * x + f.n[k] * y + f.t[k] * z);

/** Reject a whole room envelope when it obscures a fitted screen from the approaching road. */
export function roomScreenClear(capsule, faces, track) {
  const top = capsule.B.map((v, k) => v + capsule.D[k] * capsule.len);
  for (const face of faces) for (const lead of [30, 45, 60]) {
    const f = track.frameAt(face.s - lead), eye = local(f, 0, 3);
    for (const u of [-0.4, 0, 0.4]) for (const v of [-0.4, 0, 0.4]) {
      const target = face.C.map((x, k) => x + face.X[k] * face.w * u + face.Y[k] * face.h * v);
      if (segDist(capsule.B, top, eye, target) < capsule.r + 2) return false;
    }
  }
  return true;
}

export function mainframeRoomSites(track, samples, faces = [], tier = Q.name) {
  const sites = [];
  for (const section of mainframeRoomWindows(track)) {
    const count = tier === 'phone' ? 4 : section.kind === 'control' ? 6 : 10;
    for (let i = 0; i < count; i++) {
      const s = section.fromS + (section.toS - section.fromS) * (0.12 + 0.76 * Math.floor(i / 2) / Math.max(1, count / 2 - 1));
      const f = track.frameAt(s), side = i % 2 ? 1 : -1;
      for (let off = f.w / 2 + 18, attempt = 0; attempt < 20; attempt++, off += 8) {
        const p = local(f, side * off, 0), capsule = { B: p, D: [...f.n], len: 20, r: 14 };
        if (!bodyClear(capsule, samples, ROOM_CLEARANCE).ok || !roomScreenClear(capsule, faces, track)) continue;
        sites.push({ kind: section.kind, section: section.id, s, f, side, p, off, capsule, phase: sites.length * 1.73 }); break;
      }
    }
  }
  return sites;
}

/** Keep the accepted rider triangles and face pixels, removing only the vehicle triangles. */
export function riderVertex(mesh, vertex) {
  const skin = mesh.geometry.getAttribute('skinIndex'), weight = mesh.geometry.getAttribute('skinWeight');
  if (!skin || !weight || !mesh.skeleton) return false;
  let amount = 0;
  for (let j = 0; j < 4; j++) {
    let bone = mesh.skeleton.bones[skin.getComponent(vertex, j)];
    while (bone && bone.name !== 'rider_mount') bone = bone.parent;
    if (bone) amount += weight.getComponent(vertex, j);
  }
  return amount >= 0.5;
}

export function buildVatOccupant(art) {
  const source = art?.model?.('emi-teacup-kart')?.scene;
  if (!source) return null;
  const group = new Group(), owned = [];
  group.name = 'mainframe-accepted-emi'; source.updateMatrixWorld(true);
  const inverse = source.matrixWorld.clone().invert(), p = new Vector3();
  source.traverse(mesh => {
    if (!mesh.isSkinnedMesh || !mesh.geometry.index) return;
    mesh.skeleton.update();
    const ix = mesh.geometry.index, keep = [];
    for (let i = 0; i < ix.count; i += 3) {
      const a = ix.getX(i), b = ix.getX(i + 1), c = ix.getX(i + 2);
      if (riderVertex(mesh, a) && riderVertex(mesh, b) && riderVertex(mesh, c)) keep.push(a, b, c);
    }
    if (!keep.length) return;
    const geometry = mesh.geometry.clone(), position = geometry.getAttribute('position');
    for (let i = 0; i < position.count; i++) {
      p.fromBufferAttribute(position, i); mesh.applyBoneTransform(i, p); p.applyMatrix4(mesh.matrixWorld).applyMatrix4(inverse);
      position.setXYZ(i, p.x, p.y, p.z);
    }
    geometry.setIndex(keep); geometry.deleteAttribute('skinIndex'); geometry.deleteAttribute('skinWeight');
    geometry.computeVertexNormals(); geometry.computeBoundingSphere();
    const src = mesh.material, material = new MeshBasicNodeMaterial({ map: src.map || null, vertexColors: !!geometry.getAttribute('color'), color: src.color?.clone() || '#ffffff', side: src.side });
    // The model's source screen is intentionally blank. Reuse the accepted happy atlas cell.
    const atlas = src.name === 'emi_screen' && art?.tex?.('emi-faces');
    if (atlas) material.colorNode = texture(atlas, uv().mul(0.25).add(vec2(0.25, 0))).rgb.mul(1.7);
    material.name = src.name; owned.push(geometry, material); group.add(new Mesh(geometry, material));
  });
  if (!group.children.length) return null;
  return { group, dispose: () => owned.forEach(v => v.dispose()) };
}

export function buildMainframeRooms(track, art, { samples = sampleTrack(track, 4), mediaLayout = null } = {}) {
  const group = new Group(); group.name = 'mainframe-departments';
  const M = mediaLook();
  const faces = (mediaLayout?.billboards || []).map(b => fitMainframeBillboard(b, track, samples, M)).filter(Boolean).map(f => motionEnvelope(f, M));
  const sites = mainframeRoomSites(track, samples, faces), geometries = {
    box: new BoxGeometry(1, 1, 1), tube: new CylinderGeometry(1, 1, 1, 16), cone: new CylinderGeometry(0.72, 1, 1, 16),
    ring: new TorusGeometry(1, 0.08, 6, 24).rotateX(Math.PI / 2), sphere: new SphereGeometry(1, 12, 8),
    glass: new CylinderGeometry(1, 1, 1, 24, 1, true),
  };
  const mats = {
    steel: createMachineMaterial('#839896', art), dark: createMachineMaterial('#19282d', art), ivory: createMachineMaterial('#a0b6a9', art),
    copper: createMachineMaterial('#b16a36', art), blue: createMachineMaterial('#5ed7e3', art, { glow: true }),
    green: createMachineMaterial('#88d790', art, { glow: true }), amber: createMachineMaterial('#dca34c', art, { glow: true }),
    black: createMachineMaterial('#101214', art), glass: new MeshBasicNodeMaterial({ color: '#73e9bc', transparent: true, opacity: 0.10, side: DoubleSide, depthWrite: false }),
  };
  const clock = uniform(0), latex = new MeshBasicNodeMaterial();
  const normal = normalize(normalWorld), view = normalize(cameraPosition.sub(positionWorld));
  const shine = pow(max(dot(normal, normalize(view.add(vec3(0.4, 0.9, 0.2)))), 0), 60);
  const ripple = sin(positionWorld.x.mul(0.6).add(positionWorld.z.mul(0.43)).add(clock.mul(0.4))).mul(0.006);
  latex.colorNode = vec3(0.008, 0.012, 0.016).add(ripple).add(vec3(0.18, 0.25, 0.28).mul(shine));
  latex.name = 'opaque-black-latex'; mats.latex = latex;
  const batches = new Map(), dynamic = [], rotors = [], bubbles = [], occupants = [], occupant = buildVatOccupant(art);
  const basis = new Matrix4(), transform = new Matrix4(), matrix = new Matrix4(), q = new Quaternion(), p = new Vector3(), scale = new Vector3(), x = new Vector3(), y = new Vector3(), z = new Vector3();
  const localMatrix = new Matrix4(), up = new Vector3(0, 1, 0);
  let time = 0;
  const stat = { control: 0, engine: 0, containment: 0, latex: 0, cables: 0, occupants: 0, parts: 0, batches: 0 };
  function part(geo, mat, position, size, rotation = null, motion = null) {
    const key = geo + ':' + mat;
    if (!batches.has(key)) batches.set(key, { geo, mat, parts: [] });
    q.identity(); if (rotation) q.setFromAxisAngle(new Vector3(...rotation.axis), rotation.angle);
    localMatrix.compose(p.fromArray(position), q, scale.fromArray(size));
    const record = { local: localMatrix.clone(), parent: transform.clone(), motion };
    batches.get(key).parts.push(record); stat.parts++;
    if (motion) dynamic.push(record);
    return record;
  }
  const box = (m, p, s, r = null, motion = null) => part('box', m, p, s, r, motion);
  const ring = (m, p, r, rotation = null) => part('ring', m, p, [r, r, r], rotation);
  const pipe = (m, a, b, radius = 0.5) => {
    const A = new Vector3(...a), B = new Vector3(...b), delta = B.clone().sub(A), length = delta.length();
    const center = A.add(B).multiplyScalar(0.5), turn = new Quaternion().setFromUnitVectors(up, delta.normalize());
    const result = part('tube', m, center.toArray(), [radius, length, radius]);
    result.local.compose(center, turn, scale.set(radius, length, radius)); return result;
  };
  function cable(a, b, sag, material = 'black', radius = 0.3) {
    const points = Array.from({ length: 13 }, (_, i) => a.map((v, k) => v + (b[k] - v) * i / 12 - (k === 1 ? Math.sin(i / 12 * Math.PI) * sag : 0)));
    for (let j = 1; j < points.length; j++) pipe(material, points[j - 1], points[j], radius);
  }
  for (const site of sites) {
    stat[site.kind]++;
    basis.makeBasis(x.fromArray(site.f.b), y.fromArray(site.f.n), z.fromArray(site.f.t).negate());
    q.setFromRotationMatrix(basis); transform.compose(p.fromArray(site.p), q, scale.set(1, 1, 1));
    const front = -site.side;
    box('dark', [0, -0.6, 0], [19, 1.2, 19]);
    for (const z of [-8.5, 8.5]) box('steel', [0, 0.15, z], [19, 0.3, 0.45]);
    if (site.kind === 'control') {
      // A horseshoe of angled desks, instrument racks, stools and a wall of scopes.
      for (const dz of [-5, 0, 5]) {
        box('ivory', [front * 2, 2.2, dz], [6, 4.4, 4.4]);
        box('dark', [front * 2, 4.5, dz], [6.4, 0.7, 4.8], { axis: [0, 0, 1], angle: front * -0.18 });
        box('blue', [front * 1.9, 4.91, dz], [3.6, 0.06, 2.9]);
        for (let k = 0; k < 5; k++) box(k % 2 ? 'amber' : 'green', [front * 4.4, 4.9, dz - 1.6 + k * 0.8], [0.25, 0.2, 0.3]);
        part('tube', 'dark', [front * 7, 1.8, dz], [1.25, 0.55, 1.25]);
        part('tube', 'steel', [front * 7, 0.8, dz], [0.18, 1.5, 0.18]);
      }
      box('steel', [-front * 5, 8.6, 0], [2.2, 17, 18]);
      for (let row = 0; row < 4; row++) for (let col = 0; col < 4; col++) {
        box('dark', [-front * 3.8, 3.1 + row * 3.4, -6.6 + col * 4.4], [0.35, 2.85, 3.7]);
        box(row % 2 ? 'green' : 'blue', [-front * 3.58, 3.1 + row * 3.4, -6.6 + col * 4.4], [0.08, 2.35, 3.2]);
        for (let line = 0; line < 4; line++) box('dark', [-front * 3.50, 2.35 + row * 3.4 + line * 0.44, -6.6 + col * 4.4], [0.08, 0.16, 2.7 - line * 0.35]);
      }
    } else if (site.kind === 'engine') {
      // Turbine barrels, copper winding, actual rotating fan blades and distribution pipes.
      for (const dz of [-5.2, 5.2]) {
        part('tube', 'steel', [0, 6, dz], [5.1, 11, 5.1], { axis: [0, 0, 1], angle: Math.PI / 2 });
        for (const dx of [-4.9, -3.7, 0, 3.7, 4.9]) ring('copper', [dx, 6, dz], 5.2, { axis: [0, 0, 1], angle: Math.PI / 2 });
        part('tube', 'dark', [front * 5.6, 6, dz], [4.5, 0.5, 4.5], { axis: [0, 0, 1], angle: Math.PI / 2 });
        for (let j = 0; j < 8; j++) {
          const angle = j * Math.PI / 4;
          const record = box('ivory', [front * 5.95, 6 + Math.cos(angle) * 2.6, dz + Math.sin(angle) * 2.6], [0.25, 3.2, 1.25], { axis: [1, 0, 0], angle }, { kind: 'rotor', center: [front * 5.95, 6, dz], angle, phase: site.phase });
          rotors.push(record);
        }
        ring('amber', [front * 6.1, 6, dz], 4.6, { axis: [0, 0, 1], angle: Math.PI / 2 });
      }
      for (const dz of [-6, 6]) { pipe('copper', [-5, 17, dz], [5, 17, dz], 0.85); pipe('steel', [-5, 8, dz], [-5, 17, dz], 0.85); }
      cable([-7, 19, -8], [7, 19, 8], 3.5, 'black', 0.65);
    } else if (site.kind === 'containment') {
      part('cone', 'steel', [0, 1, 0], [6.3, 2, 6.3]); part('glass', 'glass', [0, 9.4, 0], [5.5, 15, 5.5]);
      for (const h of [2.2, 16.8]) { part('tube', 'steel', [0, h, 0], [6.2, 1, 6.2]); ring('green', [0, h + 0.6, 0], 5.7); }
      for (const dz of [-4.9, 4.9]) pipe('steel', [-front * 2.5, 2, dz], [-front * 2.5, 17, dz], 0.3);
      box('dark', [front * 6, 4, 0], [1.2, 4.6, 4]); box('green', [front * 6.7, 4.5, 0], [0.1, 2.7, 3.2]);
      pipe('copper', [-front * 6, 1, -5], [-front * 6, 19, -5], 0.65);
      pipe('copper', [-front * 6, 19, -5], [0, 19, 0], 0.65);
      if (occupant) {
        const holder = new Group(), model = occupant.group.clone(true); holder.name = 'mainframe-vat-emi';
        model.scale.setScalar(8); model.position.y = 3; model.rotation.y = -front * Math.PI / 2;
        holder.add(model); holder.applyMatrix4(transform); group.add(holder); occupants.push({ model, phase: site.phase }); stat.occupants++;
      }
      for (let i = 0; i < 9; i++) bubbles.push(part('sphere', 'green', [Math.cos(i * 2.4) * 3.9, 3 + i * 1.4, Math.sin(i * 2.4) * 3.9], [0.13, 0.13, 0.13], null, { kind: 'bubble', phase: site.phase + i * 0.7, tar: false }));
    } else if (site.kind === 'latex') {
      // Opaque black liquid has a solid volume, raised meniscus and lazy surface bubbles.
      part('glass', 'steel', [0, 3.5, 0], [6.3, 7, 6.3]);
      part('tube', 'latex', [0, 6, 0], [5.9, 1.2, 5.9]); ring('steel', [0, 6.7, 0], 6.1); ring('latex', [0, 6.65, 0], 5.7);
      for (let i = 0; i < 7; i++) bubbles.push(part('sphere', 'latex', [Math.cos(i * 2.4) * 4.3, 6.6, Math.sin(i * 2.4) * 4.3], [0.6, 0.2, 0.6], null, { kind: 'bubble', phase: site.phase + i, tar: true }));
      pipe('copper', [-6, 3, -6], [-6, 17, -6], 0.8); pipe('copper', [-6, 17, -6], [0, 17, 0], 0.8);
      part('tube', 'latex', [0, 12, 0], [0.68, 9, 0.68]); ring('amber', [-6, 12, -6], 1.2);
      part('tube', 'dark', [6, 3, 5], [2.1, 5, 2.1], { axis: [1, 0, 0], angle: Math.PI / 2 });
      for (const dz of [-7, 7]) box('amber', [front * 3, 0.1, dz], [8, 0.16, 0.45]);
      for (let k = 0; k < 4; k++) box('black', [front * 3 - 3 + k * 2, 0.2, 7], [0.65, 0.1, 0.6]);
    } else {
      for (const dz of [-8, 8]) {
        box('steel', [0, 10, dz], [2, 20, 2]); box('steel', [0, 19, dz], [17, 1.4, 2.4]);
        for (let i = -3; i <= 3; i++) ring('copper', [i * 2.1, 18.2, dz], 0.65, { axis: [1, 0, 0], angle: Math.PI / 2 });
      }
      for (let i = -3; i <= 3; i++) cable([i * 2.1, 18.2, -8], [i * 2.1, 18.2, 8], 3.3 + (i + 3) * 0.6, i % 2 ? 'black' : 'copper', 0.36);
      part('tube', 'dark', [0, 3, 0], [4, 8, 4], { axis: [0, 0, 1], angle: Math.PI / 2 });
      for (const dx of [-4.3, 4.3]) part('tube', 'steel', [dx, 3, 0], [4.5, 0.45, 4.5], { axis: [0, 0, 1], angle: Math.PI / 2 });
      ring('blue', [front * 4.7, 3, 0], 2.8, { axis: [0, 0, 1], angle: Math.PI / 2 });
    }
  }
  const meshes = [];
  for (const [key, batch] of batches) {
    const mesh = new InstancedMesh(geometries[batch.geo], mats[batch.mat], batch.parts.length); mesh.name = 'department-' + key;
    batch.parts.forEach((part, i) => { part.mesh = mesh; part.index = i; matrix.multiplyMatrices(part.parent, part.local); mesh.setMatrixAt(i, matrix); });
    mesh.instanceMatrix.needsUpdate = true; mesh.computeBoundingSphere(); if (mesh.boundingSphere) mesh.boundingSphere.radius += 18;
    meshes.push(mesh); group.add(mesh);
  }
  stat.batches = meshes.length;
  function update(dt, { paused = false, gentle = false } = {}) {
    if (paused || !(dt > 0)) return;
    time += Math.min(dt, 0.1) * (gentle ? 0.5 : 1); clock.value = time;
    for (const part of dynamic) {
      const motion = part.motion; localMatrix.copy(part.local);
      if (motion.kind === 'rotor') {
        const a = motion.angle + time * 0.45 + motion.phase;
        p.set(motion.center[0], motion.center[1] + Math.cos(a) * 2.6, motion.center[2] + Math.sin(a) * 2.6);
        q.setFromAxisAngle(x.set(1, 0, 0), a); localMatrix.compose(p, q, scale.set(0.25, 3.2, 1.25));
      } else if (motion.tar) {
        const bulge = 0.15 + (Math.sin(time * 0.55 + motion.phase) * 0.5 + 0.5) * 0.65;
        localMatrix.elements[5] = bulge;
      } else localMatrix.elements[13] = 3 + ((time * 0.65 + motion.phase) % 12);
      matrix.multiplyMatrices(part.parent, localMatrix); part.mesh.setMatrixAt(part.index, matrix); part.mesh.instanceMatrix.needsUpdate = true;
    }
    for (const o of occupants) { o.model.position.y = 3 + Math.sin(time * 0.45 + o.phase) * 0.45; o.model.rotation.z = Math.sin(time * 0.2 + o.phase) * 0.04; }
  }
  return { group, sites, stats: stat, occupants, rotors, bubbles, clock, update,
    dispose() { group.removeFromParent(); meshes.forEach(m => m.dispose()); Object.values(geometries).forEach(g => g.dispose()); Object.values(mats).forEach(m => m.dispose()); occupant?.dispose(); } };
}
