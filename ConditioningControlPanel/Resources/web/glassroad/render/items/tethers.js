/* Physical Tether props. Authored link meshes share one instance draw per live chain.
 * Host clocks own sag, recoil and fades; there are no baked clips or bubble shells. */
import { Box3, Group, InstancedMesh, Mesh, MeshBasicMaterial, RingGeometry, ConeGeometry, Vector3, Quaternion, Matrix4 } from 'three/webgpu';
import { S } from '../../sim/state.js';
import { TETHER_LOOK, tetherPoints, tetherSegment, tetherBreakPose, tetherScale } from './tetherMath.js';

const MAX = 8, UP = new Vector3(0, 1, 0), FORWARD = new Vector3(0, 0, -1);

/** Bake GLB root transforms once. Geometry stays local to the declared export origin. */
function source(art, id) {
  const scene = art?.model?.(id)?.scene, parts = [];
  if (!scene) return parts;
  scene.updateMatrixWorld(true);
  scene.traverse(o => {
    if (!o.isMesh) return;
    const geometry = o.geometry.clone().applyMatrix4(o.matrixWorld);
    const materials = (Array.isArray(o.material) ? o.material : [o.material]).map(m => {
      const c = m.clone(); c.transparent = true; c.depthWrite = false; return c;
    });
    parts.push({ geometry, materials, name: o.name });
  });
  return parts;
}

export function createTethers(root, art) {
  const group = new Group(); group.name = 'toy-tethers'; root.add(group);
  const link = source(art, 'toy-chain-link'), head = source(art, 'toy-tether-head'), cuff = source(art, 'toy-tether-cuff');
  const linkBounds = new Box3();
  for (const p of link) { p.geometry.computeBoundingBox(); linkBounds.union(p.geometry.boundingBox); }
  const linkLength = link.length ? linkBounds.max.y - linkBounds.min.y : 0.35;
  for (const p of link) p.geometry.translate(0, -linkBounds.min.y, 0);
  const pointCount = (TETHER_LOOK.links + 1) * 3, pts = new Float32Array(pointCount);
  const pos = new Vector3(), dir = new Vector3(), scale = new Vector3(), quat = new Quaternion(), roll = new Quaternion(), matrix = new Matrix4();
  const segment = {}, motion = {}, sizing = {}, liveRows = new Map();
  let time = 0;

  function copies(parts, holder, instanced = false) {
    return parts.map(p => {
      const materials = p.materials.map(m => m.clone()), mat = materials.length === 1 ? materials[0] : materials;
      const mesh = instanced ? new InstancedMesh(p.geometry, mat, TETHER_LOOK.links) : new Mesh(p.geometry, mat);
      mesh.name = p.name; mesh.frustumCulled = false; holder.add(mesh);
      return { mesh, materials };
    });
  }
  function poolRow() {
    const holder = new Group(); holder.visible = false; group.add(holder);
    const chain = copies(link, holder, true), hook = copies(head, holder), clasp = copies(cuff, holder);
    return { holder, chain, hook, clasp, active: false, t: 0, from: [0, 0, 0], to: [0, 0, 0], points: new Float32Array(pointCount) };
  }
  const live = Array.from({ length: MAX }, poolRow), broken = Array.from({ length: MAX }, poolRow);
  // Two open arcs and arrowheads are a directional cue around a passed kart, never a shell.
  const arcGeo = new RingGeometry(1.05, 1.17, 20, 1, 0, Math.PI * 0.7).rotateX(-Math.PI / 2);
  const tipGeo = new ConeGeometry(0.12, 0.3, 3).rotateZ(-Math.PI / 2);
  for (const row of broken) {
    row.turnCue = new Group(); row.holder.add(row.turnCue); row.turnMats = [];
    for (let i = 0; i < 2; i++) {
      const mat = new MeshBasicMaterial({ color: '#f6d39b', transparent: true, depthWrite: false }); row.turnMats.push(mat);
      const arc = new Mesh(arcGeo, mat), tip = new Mesh(tipGeo, mat);
      arc.rotation.y = i * Math.PI; tip.position.set(Math.cos(Math.PI * 0.7 + i * Math.PI) * 1.11, 0, -Math.sin(Math.PI * 0.7 + i * Math.PI) * 1.11);
      tip.rotation.y = Math.PI * 0.7 + i * Math.PI;
      row.turnCue.add(arc, tip);
    }
  }
  function alpha(parts, a, palette) {
    for (const p of parts) for (const m of p.materials) {
      m.opacity = a;
      if (palette && m.name === 'toy_enamel') m.color.set(palette);
    }
  }
  function endpoint(parts, p, other, karts, racer, isCuff) {
    pos.set(...p); pos.y += TETHER_LOOK.liftM;
    const st = karts[racer]?.st;
    if (isCuff && st) quat.set(st[S.quatX], st[S.quatY], st[S.quatZ], st[S.quatW]);
    else { dir.set(other[0] - p[0], other[1] - p[1], other[2] - p[2]).normalize(); quat.setFromUnitVectors(FORWARD, dir.lengthSq() ? dir : FORWARD); }
    for (const { mesh } of parts) { mesh.position.copy(pos); mesh.quaternion.copy(quat); mesh.scale.setScalar(isCuff ? 1.65 : 1.8); }
  }
  function render(row, points, age, soft, palette, karts, breaking = false) {
    tetherBreakPose(age, 0, row.reason, soft, motion);
    const opacity = breaking ? motion.alpha : tetherScale(1, 1, age, soft, sizing).alpha;
    row.holder.visible = opacity > 0.001;
    const ax = row.to[0] - row.from[0], az = row.to[2] - row.from[2], len = Math.hypot(ax, az) || 1;
    for (let i = 0; i < TETHER_LOOK.links; i++) {
      tetherSegment(points, i, segment); tetherBreakPose(age, i, row.reason, soft, motion);
      tetherScale(segment.length, linkLength, age, soft, sizing);
      pos.set(segment.x, segment.y, segment.z);
      if (breaking) { pos.x += ax / len * motion.recoil; pos.z += az / len * motion.recoil; pos.y += motion.lift; }
      dir.set(segment.dx, segment.dy, segment.dz); quat.setFromUnitVectors(UP, dir);
      quat.multiply(roll.setFromAxisAngle(UP, segment.roll + (breaking ? motion.turn : 0)));
      scale.set(sizing.width * 1.45, sizing.length, sizing.width * 1.45); matrix.compose(pos, quat, scale);
      for (const part of row.chain) part.mesh.setMatrixAt(i, matrix);
    }
    for (const part of row.chain) { part.mesh.count = TETHER_LOOK.links; part.mesh.instanceMatrix.needsUpdate = true; }
    alpha(row.chain, opacity, palette); alpha(row.hook, opacity, palette); alpha(row.clasp, opacity, palette);
    endpoint(row.hook, row.from, row.to, karts, row.by, false); endpoint(row.clasp, row.to, row.from, karts, row.target, true);
    if (breaking) {
      const target = karts[row.target]?.st;
      row.turnCue.visible = row.reason === 'pass' && (!target || target[S.stalled] > 0);
      row.turnCue.position.set(target?.[S.posX] ?? row.to[0], (target?.[S.posY] ?? row.to[1]) + 0.14, target?.[S.posZ] ?? row.to[2]);
      row.turnCue.rotation.y = motion.spin;
      for (const m of row.turnMats) { m.opacity = motion.cueAlpha * 0.8; if (palette) m.color.set(palette); }
    }
  }
  function clear() {
    liveRows.clear(); time = 0;
    for (const row of [...live, ...broken]) { row.active = false; row.holder.visible = false; }
  }
  return {
    update(view, dt, karts = [], soft = false, palette = null) {
      if (!view) { clear(); return; }
      const delta = Math.max(0, Math.min(0.1, dt || 0)); time += delta;
      liveRows.clear();
      const rows = view?.tethers || [];
      for (let i = 0; i < MAX; i++) {
        const item = rows[i], row = live[i];
        if (!item) { row.holder.visible = false; continue; }
        Object.assign(row, { by: item.by, target: item.target, k: item.k, t: item.t || 0 });
        row.from.splice(0, 3, ...item.from); row.to.splice(0, 3, ...item.to);
        tetherPoints(row.from, row.to, row.k, time, pts, TETHER_LOOK, soft); row.points.set(pts);
        liveRows.set(item.by + ':' + item.target, row);
        render(row, pts, row.t, soft, palette, karts);
      }
      for (const row of broken) {
        if (!row.active) continue;
        row.t += delta;
        if (row.t >= 0.8) { row.active = false; row.holder.visible = false; continue; }
        render(row, row.points, row.t, soft, palette, karts, true);
      }
    },
    event(name, payload) {
      if (name !== 'tether-break') return;
      const previous = liveRows.get(payload.by + ':' + payload.target);
      if (!previous) return;
      const row = broken.find(r => !r.active) || broken.reduce((a, b) => a.t > b.t ? a : b);
      Object.assign(row, { active: true, t: 0, by: payload.by, target: payload.target, reason: payload.reason });
      row.from.splice(0, 3, ...previous.from); row.to.splice(0, 3, ...previous.to); row.points.set(previous.points);
      // No luminance spike: pass arrows use ordinary diffuse colour. Payoff owns any flash.
    },
    clear,
    dispose() {
      root.remove(group);
      for (const row of [...live, ...broken]) { for (const p of [...row.chain, ...row.hook, ...row.clasp]) { p.mesh.dispose?.(); for (const m of p.materials) m.dispose(); } for (const m of row.turnMats || []) m.dispose(); }
      for (const p of [...link, ...head, ...cuff]) { p.geometry.dispose(); for (const m of p.materials) m.dispose(); }
      arcGeo.dispose(); tipGeo.dispose();
    },
  };
}
