/* Warmed, soft perfume volumes with a permanent outline and dry-lane arrows. */
import { Group, InstancedMesh, Mesh, MeshBasicNodeMaterial, SphereGeometry, BoxGeometry, BufferGeometry,
  Float32BufferAttribute, Matrix4, Vector3, Quaternion, DoubleSide } from 'three/webgpu';
import { uniform, normalView, positionViewDirection, abs, dot, pow } from 'three/tsl';
import { perfumeZones, perfumePhase, PERFUME } from '../sim/boudoirPerfume.js';

export function createBoudoirPerfumeView(scene, track, { zones } = {}) {
  const group = new Group(); group.name = 'boudoir-perfume'; scene.add(group);
  const rows = perfumeZones(track, zones), materials = [], geometries = [], entries = [];
  const sphere = new SphereGeometry(1, 14, 10), box = new BoxGeometry(1, 1, 1);
  geometries.push(sphere, box);
  const matrix = new Matrix4(), basis = new Matrix4(), p = new Vector3(), q = new Quaternion(), size = new Vector3();
  const axes = [new Vector3(), new Vector3(), new Vector3()], pose = {};
  function material(color, opacity = 1) {
    const m = new MeshBasicNodeMaterial({ color, transparent: true, opacity, depthWrite: false, side: DoubleSide });
    materials.push(m); return m;
  }
  function put(mesh, i, s, x, h, w, y, d, angle = 0) {
    const f = track.frameAt(s);
    p.fromArray(f.p).addScaledVector(axes[0].fromArray(f.b), x).addScaledVector(axes[1].fromArray(f.n), h);
    basis.makeBasis(axes[0], axes[1], axes[2].fromArray(f.t).negate()); q.setFromRotationMatrix(basis);
    if (angle) q.multiply(new Quaternion().setFromAxisAngle(new Vector3(0, 1, 0), angle));
    matrix.compose(p, q, size.set(w, y, d)); mesh.setMatrixAt(i, matrix);
  }
  for (const [index, zone] of rows.entries()) {
    const count = Math.ceil((zone.s1 - zone.s0) / 6), step = (zone.s1 - zone.s0) / count;
    const alpha = uniform(0), mist = material(index % 2 ? '#d6b7ed' : '#efbfd7', 0);
    mist.name = 'boudoir-perfume-mist:' + zone.id;
    // The silhouette fades to zero rather than showing opaque overlapping sphere edges.
    mist.opacityNode = pow(abs(dot(normalView, positionViewDirection)), 1.5).mul(alpha);
    const clouds = new InstancedMesh(sphere, mist, count * 3); clouds.name = 'perfume-cloud:' + zone.id;
    for (let i = 0; i < count; i++) for (let col = 0; col < 3; col++) {
      put(clouds, i * 3 + col, zone.s0 + (i + .5) * step, zone.x + (col - 1) * zone.w / 3,
        PERFUME.heightM / 2, zone.w / 5, PERFUME.heightM / 2, step * .72);
    }
    clouds.computeBoundingSphere(); clouds.renderOrder = 3; group.add(clouds);
    const edgeMat = material('#bb8c79', .32), edges = new InstancedMesh(box, edgeMat, count * 2 + 2);
    edges.name = 'perfume-boundary:' + zone.id;
    for (let i = 0; i < count; i++) for (let side = 0; side < 2; side++)
      put(edges, i * 2 + side, zone.s0 + (i + .5) * step, zone.x + (side ? 1 : -1) * zone.w / 2, .1, .18, .08, step * .85);
    for (let i = 0; i < 2; i++) put(edges, count * 2 + i, i ? zone.s1 : zone.s0, zone.x, .1, zone.w, .08, .22);
    edges.computeBoundingSphere(); edges.renderOrder = 4; group.add(edges);
    const fillMat = material('#e6bc7a', 0), positions = [], indices = [], fill = new BufferGeometry();
    for (let i = 0; i <= count; i++) {
      const f = track.frameAt(zone.s0 + i * step);
      for (const side of [-1, 1]) for (let j = 0; j < 3; j++) positions.push(f.p[j] + f.b[j] * (zone.x + side * zone.w / 2) + f.n[j] * .07);
      if (i < count) { const a = i * 2; indices.push(a, a + 1, a + 2, a + 1, a + 3, a + 2); }
    }
    fill.setAttribute('position', new Float32BufferAttribute(positions, 3)); fill.setIndex(indices); fill.computeVertexNormals(); geometries.push(fill);
    const footprint = new Mesh(fill, fillMat); footprint.name = 'perfume-footprint:' + zone.id; footprint.renderOrder = 2; group.add(footprint);
    const arrowMat = material('#f2e3be', .8), arrows = new InstancedMesh(box, arrowMat, Math.ceil(count / 2) * 2);
    arrows.name = 'perfume-dry-bypass:' + zone.id;
    for (let i = 0; i < Math.ceil(count / 2); i++) for (let side = 0; side < 2; side++)
      put(arrows, i * 2 + side, zone.s0 + (i * 2 + .5) * step, zone.dryX + (side ? .42 : -.42), .1, .18, .08, 1.25, side ? -.7 : .7);
    arrows.computeBoundingSphere(); arrows.renderOrder = 4; group.add(arrows);
    entries.push({ zone, alpha, edgeMat, fillMat });
  }
  function update(timeS, { ready = true, gentle = false, paused = false, active = true, dt = 1 / 60 } = {}) {
    if (paused) return;
    for (const e of entries) {
      perfumePhase(e.zone, timeS, pose);
      const warning = active ? ready ? pose.warning : 1 : 0, mist = active && ready ? pose.mist : 0;
      const share = gentle ? .78 : 1, rise = Math.min(1, Math.max(0, dt) / .3);
      // Clock discontinuities still fade in, including a seek directly into an active spray.
      const target = mist * .23 * share;
      e.alpha.value += Math.max(-.23 * rise, Math.min(.23 * rise, target - e.alpha.value));
      e.edgeMat.opacity += Math.max(-.6 * rise, Math.min(.6 * rise, (.32 + warning * .48) * share - e.edgeMat.opacity));
      e.fillMat.opacity += Math.max(-.18 * rise, Math.min(.18 * rise, warning * .13 * share - e.fillMat.opacity));
    }
  }
  let disposed = false;
  return { group, update, inspect: () => ({ zones: entries.length, cloudOpacity: entries.map(e => e.alpha.value), materials: materials.length }),
    dispose() { if (disposed) return; disposed = true; group.removeFromParent();
      group.traverse(o => { if (o.isInstancedMesh) o.dispose(); }); materials.forEach(m => m.dispose()); geometries.forEach(g => g.dispose()); } };
}
