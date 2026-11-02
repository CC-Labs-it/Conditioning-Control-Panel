// Suspended processor boards. Whole motion bounds clear every road level and media face.
import { Group, InstancedMesh, BoxGeometry, TorusGeometry, Matrix4, Vector3, Quaternion, Euler } from 'three/webgpu';
import { bodyClear } from './colossiMath.js';
import { createMachineMaterial } from './mainframeMaterials.js';
import { Q } from './quality.js';

export function buildFloatingProcessors(track, art, samples, billboards = []) {
  const group = new Group(); group.name = 'mainframe-floating-processors';
  const sites = [], box = new BoxGeometry(1, 1, 1), ring = new TorusGeometry(10, 0.3, 5, 36);
  const materials = [createMachineMaterial('#1c3934', art), createMachineMaterial('#a3b9ac', art), createMachineMaterial('#60f1be', art, { glow: true })];
  const count = Q.name === 'phone' ? 10 : 22;
  for (let i = 0; i < count; i++) {
    const s = (i + 0.4) * track.lengthM / count, f = track.frameAt(s), side = i % 2 ? 1 : -1;
    const radius = 18, up = 15 + i % 3 * 8;
    for (let off = f.w / 2 + 32, tries = 0; tries < 10; tries++, off += 12) {
      const p = f.p.map((v, k) => v + f.b[k] * side * off + (k === 1 ? up : 0));
      const capsule = { B: p, D: [0, 1, 0], len: 0, r: radius };
      if (!bodyClear(capsule, samples).ok) continue;
      if (billboards.some(b => Math.hypot(...b.C.map((v, k) => v - p[k])) < radius + Math.hypot(b.w, b.h) / 2 + 20)) continue;
      sites.push({ p, capsule, yaw: Math.atan2(-f.t[0], -f.t[2]), side, phase: i * 2.3 }); break;
    }
  }
  const parts = [[], [], []];
  const add = (kind, p, scale) => parts[kind].push({ p, scale });
  add(0, [0, 0, 0], [16, 12, 1.8]); add(1, [0, 0, 1.1], [7, 7, 0.7]); add(0, [0, 0, 1.55], [5.8, 5.8, 0.3]);
  for (let i = -3; i <= 3; i++) for (const side of [-1, 1]) {
    add(1, [side * 9, i * 1.5, 0], [2.5, 0.5, 0.65]);
    add(2, [side * 5.5, i * 1.5, 1], [3.5, 0.12, 0.16]);
  }
  for (let i = -2; i <= 2; i++) add(2, [i * 1.1, 0, 1.8], [0.2, 4.5, 0.1]);
  const meshes = parts.map((rows, i) => new InstancedMesh(box, materials[i], sites.length * rows.length));
  meshes.forEach((m, i) => { m.name = 'floating-processor-' + ['body', 'pins', 'traces'][i]; group.add(m); });
  const rings = new InstancedMesh(ring, materials[2], sites.length); rings.name = 'floating-processor-induction-rings'; group.add(rings);
  const matrix = new Matrix4(), transform = new Matrix4(), local = new Matrix4(), pos = new Vector3(), scale = new Vector3();
  const q = new Quaternion(), euler = new Euler(); let time = 0, age = 1;
  function update(dt, gentle = false) {
    time += Math.max(0, dt) * (gentle ? 0.5 : 1); age += dt;
    if (age < 1 / 30) return; age = 0;
    sites.forEach((site, i) => {
      pos.fromArray(site.p); pos.y += Math.sin(time * 0.55 + site.phase) * 1.4;
      q.setFromEuler(euler.set(0.18 + Math.sin(time * 0.18 + site.phase) * 0.12, site.yaw + Math.sin(time * 0.14 + site.phase) * 0.3, site.side * 0.22));
      transform.compose(pos, q, scale.set(1, 1, 1));
      parts.forEach((rows, kind) => rows.forEach((part, j) => {
        local.makeScale(...part.scale).setPosition(...part.p); matrix.multiplyMatrices(transform, local);
        meshes[kind].setMatrixAt(i * rows.length + j, matrix);
      }));
      local.makeRotationX(Math.PI / 2); local.setPosition(0, -9, 0); matrix.multiplyMatrices(transform, local); rings.setMatrixAt(i, matrix);
    });
    for (const mesh of meshes) mesh.instanceMatrix.needsUpdate = true; rings.instanceMatrix.needsUpdate = true;
  }
  update(0);
  for (const mesh of [...meshes, rings]) { mesh.computeBoundingSphere(); if (mesh.boundingSphere) mesh.boundingSphere.radius += 20; }
  return { group, sites, update, dispose() { group.removeFromParent(); [...meshes, rings].forEach(m => m.dispose()); box.dispose(); ring.dispose(); materials.forEach(m => m.dispose()); } };
}
