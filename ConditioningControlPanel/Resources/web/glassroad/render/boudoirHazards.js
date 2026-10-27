/* Sculpted powder presses, sweeping mascara brushes and clocked satin conveyor lanes. */
import { Group, Mesh, InstancedMesh, BoxGeometry, SphereGeometry, CylinderGeometry, TorusGeometry, BufferGeometry, Float32BufferAttribute,
  PlaneGeometry, IcosahedronGeometry, MeshBasicNodeMaterial, Matrix4, Vector3, Quaternion, Color, DoubleSide } from 'three/webgpu';
import { vec3, uniform, normalize, normalWorld, dot, clamp } from 'three/tsl';
import { BOUDOIR_HAZARD as K, getBoudoirSetpieces, powderPressHeads, powderPressPhase as pressPose, mascaraHeads, mascaraPhase as brushPose, ribbonPhase as ribbonPose } from '../track/boudoir-setpieces.js';
import { machineryBeatAt } from '../track/machineryBeat.js';
import { chessTrap, chessWobble } from './chessTrap.js';
import { LOOK } from './look.js';
export function createBoudoirHazardView(scene, track, art = null) {
  const timed = pose => (row, timeS) => pose(row, timeS, machineryBeatAt(track,timeS));
  const powderPressPhase = timed(pressPose), mascaraPhase = timed(brushPose), ribbonPhase = timed(ribbonPose);
  const plan = getBoudoirSetpieces(track), group = new Group(); group.name = 'boudoir-interactions'; scene.add(group);
  const geometries = [], materials = [], entries = [], belts = [], burst = [], byId = new Map();
  const own = geometry => { geometries.push(geometry); return geometry; }, box = own(new BoxGeometry(1, 1, 1)), sphere = own(new SphereGeometry(1, 20, 12));
  const cylinder = own(new CylinderGeometry(1, 1, 1, 24)), ring = own(new TorusGeometry(2.16, .13, 5, 24)), plane = own(new PlaneGeometry(1, 1));
  const shade = clamp(dot(normalize(normalWorld), normalize(vec3(...LOOK.lightDir))).mul(.36).add(.64), .24, 1);
  function paint(hex) { const m = new MeshBasicNodeMaterial(), c = new Color(hex); m.colorNode = vec3(c.r, c.g, c.b).mul(shade); materials.push(m); return m; }
  const gold = paint('#dcb778'), ink = paint('#41263f'), pink = paint('#d994bc'), cushion = paint('#f4d0df'), satin = paint('#786096');
  const M = new Matrix4(), B = new Matrix4(), Q = new Quaternion(), P = new Vector3(), Scale = new Vector3(), X = new Vector3(), Y = new Vector3(), Z = new Vector3();
  function basis(f) { return B.makeBasis(X.fromArray(f.b), Y.fromArray(f.n), Z.fromArray(f.t).negate()); }
  function mesh(parent, geometry, material, position, scale = [1, 1, 1]) {
    const object = new Mesh(geometry, material); object.position.set(...position); object.scale.set(...scale); parent.add(object); return object;
  }
  function station(row) {
    const f = track.frameAt(row.s), holder = new Group(); holder.name = row.id; holder.position.fromArray(f.p); holder.quaternion.setFromRotationMatrix(basis(f)); group.add(holder);
    const frame = new InstancedMesh(box, gold, 3); frame.name = row.id + '-shared-support';
    const sweep = row.kind === 'sweeper', height = sweep ? 11.5 : 16, leg = sweep ? 1.1 : 2, depth = sweep ? 1.3 : 3;
    [[-f.w / 2 - 2, height / 2, 0, leg, height, depth], [f.w / 2 + 2, height / 2, 0, leg, height, depth], [0, height, 0, f.w + 6, sweep ? 1.3 : 2, depth]].forEach((a, i) => {
      M.compose(P.set(...a.slice(0, 3)), Q.identity(), Scale.set(...a.slice(3))); frame.setMatrixAt(i, M);
    }); frame.instanceMatrix.needsUpdate = true; frame.computeBoundingSphere(); holder.add(frame);
    const opacity = uniform(.08), material = new MeshBasicNodeMaterial({ color: '#ffd09c', transparent: true, depthWrite: false, side: DoubleSide });
    material.opacityNode = opacity; materials.push(material);
    const warning = mesh(holder, plane, material, [0, .08, 0], [row.w + 3, row.depthM + 4, 1]); warning.rotation.x = -Math.PI / 2;
    const entry = { row, holder, f, opacity, phase: null }; entries.push(entry); byId.set(row.id, entry); return entry;
  }
  for (const row of plan.presses) {
    const entry = station(row); entry.pressHeads = [];
    // The support spans the station once; each sculpture has its own shaft and floor warning.
    entry.holder.children.find(o => o.material?.opacityNode === entry.opacity).visible = false;
    for (const head of powderPressHeads(row)) {
      const press = new Group(); press.name = head.id; press.position.x = head.x; entry.holder.add(press);
      const sculpture = chessTrap(art, head.pressLane || 0, [head.w, head.heightM, head.depthM]);
      if (sculpture) press.add(sculpture);
      else mesh(press, box, pink, [0, head.heightM / 2, 0], [head.w, head.heightM, head.depthM]);
      const rod = mesh(entry.holder, cylinder, gold, [head.x, 12, 0], [.18, 6, .18]);
      const opacity = uniform(.04), material = new MeshBasicNodeMaterial({ color: '#ffd09c', transparent: true, depthWrite: false, side: DoubleSide });
      material.opacityNode = opacity; materials.push(material);
      const warning = mesh(entry.holder, plane, material, [head.x, .08, 0], [head.w, head.depthM + 2, 1]); warning.rotation.x = -Math.PI / 2;
      entry.pressHeads.push({ row: head, moving: press, sculpture, rod, opacity, phase: null });
      byId.set(head.id, entry);
    }
    entry.moving = entry.pressHeads[0].moving;
  }
  function brushHead(parent, head) {
    const brush = new Group(); brush.name = head.id + '-brush'; parent.add(brush);
    brush.position.y = -head.armM; brush.scale.set(head.radiusM / 2.3, head.radiusM / 2.3, head.depthM / 6);
    const body = mesh(brush, cylinder, ink, [0, 0, 0], [2.15, 5.8, 2.15]); body.rotation.x = Math.PI / 2;
    const bristles = new InstancedMesh(ring, satin, 9);
    for (let i = 0; i < 9; i++) bristles.setMatrixAt(i, M.compose(P.set(0, 0, -2.7 + i * .675), Q.identity(), Scale.setScalar(1)));
    bristles.instanceMatrix.needsUpdate = true; bristles.computeBoundingSphere(); brush.add(bristles);
    // Both approach faces have individual bristles, raised spiral ridges and a gilt ferrule.
    // The template scales to the compact collision radius and depth of each pendulum.
    const ends = new InstancedMesh(box, pink, 144); ends.name = 'mascara-radial-end-bristles'; let endIndex = 0;
    for (const sign of [-1, 1]) {
      mesh(brush, ring, gold, [0, 0, sign * 2.85]);
      const hub = mesh(brush, cylinder, gold, [0, 0, sign * 2.87], [.34, .23, .34]); hub.rotation.x = Math.PI / 2;
      for (let i = 0; i < 24; i++) {
        const a = i * Math.PI / 12, length = i % 2 ? .9 : 1.3, radius = 2.06 - length / 2;
        Q.setFromAxisAngle(Z.set(0, 0, 1), a);
        ends.setMatrixAt(endIndex++, M.compose(P.set(Math.cos(a) * radius, Math.sin(a) * radius, sign * 2.93), Q, Scale.set(length, .075, .12)));
      }
      for (let i = 0; i < 48; i++) {
        const a = i / 48 * Math.PI * 4, b = (i + 1) / 48 * Math.PI * 4, ra = .4 + i / 48 * 1.55, rb = .4 + (i + 1) / 48 * 1.55;
        const ax = Math.cos(a) * ra, ay = Math.sin(a) * ra, bx = Math.cos(b) * rb, by = Math.sin(b) * rb;
        Q.setFromAxisAngle(Z.set(0, 0, 1), Math.atan2(by - ay, bx - ax));
        ends.setMatrixAt(endIndex++, M.compose(P.set((ax + bx) / 2, (ay + by) / 2, sign * 2.93), Q, Scale.set(Math.hypot(bx - ax, by - ay), .055, .12)));
      }
    }
    ends.instanceMatrix.needsUpdate = true; ends.computeBoundingSphere(); brush.add(ends);
    return brush;
  }
  for (const row of plan.sweepers) {
    const entry = station(row); entry.heads = [];
    for (const head of mascaraHeads(row)) {
      const z = -(head.longitudinalM || 0), pendulum = new Group(); pendulum.name = head.id + '-pendulum';
      pendulum.position.set(head.x, head.pivotY, z); entry.holder.add(pendulum);
      mesh(entry.holder, box, gold, [head.x, 11.5, z / 2], [.7, .7, Math.abs(z) + 1.4]);
      mesh(entry.holder, box, satin, [head.x, (11.5 + head.pivotY) / 2, z], [.65, 11.5 - head.pivotY, .6]);
      const hinge = mesh(entry.holder, cylinder, gold, [head.x, head.pivotY, z], [.65, .85, .65]); hinge.rotation.x = Math.PI / 2;
      mesh(entry.holder, ring, pink, [head.x, head.pivotY, z + .44], [.29, .29, .4]);
      const arm = mesh(pendulum, cylinder, gold, [0, -head.armM / 2, 0], [.14, head.armM, .14]); arm.name = head.id + '-arm';
      for (const y of [-1.2, -head.armM + 1.4]) mesh(pendulum, cylinder, pink, [0, y, 0], [.24, .65, .24]);
      const brush = brushHead(pendulum, head); entry.heads.push({ row: head, pivot: pendulum, arm, brush }); byId.set(head.id, entry);
    }
    for (const side of [-1, 1]) {
      mesh(entry.holder, sphere, pink, [side * (entry.f.w / 2 + 2), 12.2, 0], [1.1, .7, .8]);
      mesh(entry.holder, box, ink, [side * (entry.f.w / 2 + 2), .6, 0], [2.6, 1.2, 2.6]);
    }
    const side = row.offsetS % 2 > 1 ? 1 : -1, x = side * (entry.f.w / 2 + 6);
    mesh(entry.holder, cylinder, satin, [x, 4, 0], [2.4, 8, 2.4]);
    mesh(entry.holder, cylinder, gold, [x, 8.8, 0], [2.2, 1.6, 2.2]);
  }
  const arrow = own(new BufferGeometry()); arrow.setAttribute('position', new Float32BufferAttribute([
    -1, 0, 2, 1, 0, 2, 1, 0, -.2, -1, 0, 2, 1, 0, -.2, -1, 0, -.2,
    -2.5, 0, -.1, 2.5, 0, -.1, 0, 0, -3,
  ], 3)); arrow.computeVertexNormals();
  for (const row of plan.conveyors) {
    const colour = uniform(new Color('#91dfc6')), mat = new MeshBasicNodeMaterial({ side: DoubleSide }); mat.colorNode = colour; materials.push(mat);
    const vertices = [], n = Math.ceil((row.s1 - row.s0) / 6);
    const corner = (s, x) => { const f = track.surfaceAt(s, x); return f.p.map((v, i) => v + f.n[i] * .065); };
    for (let i = 0; i < n; i++) {
      const s0 = row.s0 + (row.s1 - row.s0) * i / n, s1 = row.s0 + (row.s1 - row.s0) * (i + 1) / n;
      const a = corner(s0, -row.w / 2), b = corner(s1, -row.w / 2), c = corner(s1, row.w / 2), d = corner(s0, row.w / 2);
      vertices.push(...a, ...b, ...c, ...a, ...c, ...d);
    }
    const geo = own(new BufferGeometry()); geo.setAttribute('position', new Float32BufferAttribute(vertices, 3)); geo.computeVertexNormals();
    const bed = new Mesh(geo, satin); bed.material.side = DoubleSide; bed.name = row.id; group.add(bed);
    const count = Math.ceil((row.s1 - row.s0) / 9), arrows = new InstancedMesh(arrow, mat, count); arrows.frustumCulled = false; arrows.name = row.id + '-arrows'; group.add(arrows);
    belts.push({ row, arrows, count, colour });
  }
  const dustGeometry = own(new IcosahedronGeometry(.23, 0)), dustMaterial = paint('#f7cfdf');
  const dust = new InstancedMesh(dustGeometry, dustMaterial, 160); dust.name = 'boudoir-impact-powder'; dust.frustumCulled = false; group.add(dust); dust.count = 0;
  let disposed = false, clock = 0;
  function puff(entry, count = 18, at = {}) {
    const f = entry.f;
    for (let i = 0; i < count; i++) {
      const angle = i * 2.399, reach = 2 + i % 7;
      burst.push({ born: clock, p: f.p.map((v, j) => v + f.n[j] * .7 + f.b[j] * (at.x || 0) + f.t[j] * (at.z || 0)), v: f.b.map((v, j) => v * Math.cos(angle) * reach + f.t[j] * Math.sin(angle) * reach + f.n[j] * (2 + i % 4)) });
    }
    if (burst.length > 160) burst.splice(0, burst.length - 160);
  }
  const beltTarget = new Color(), reverse = new Quaternion().setFromAxisAngle(new Vector3(0, 1, 0), Math.PI);
  function update(timeS, { gentle = false, paused = false, active = true, dt = 1 / 60 } = {}) {
    if (disposed || paused || !Number.isFinite(timeS)) return;
    clock = Math.max(0, timeS);
    for (const entry of entries) {
      if (entry.row.kind === 'press') {
        for (const head of entry.pressHeads) {
          const p = powderPressPhase(head.row, clock);
          head.moving.position.y = p.liftM;
          if (head.sculpture) head.sculpture.rotation.z = chessWobble(p.cycleS, gentle);
          const top = p.liftM + head.row.heightM, ceiling = 15.5;
          head.rod.position.y = (top + ceiling) / 2; head.rod.scale.y = Math.max(.1, ceiling - top);
          head.opacity.value = (.04 + p.opacity * .55) * (gentle ? .65 : 1);
          if (active && p.phase === 'hold' && head.phase !== 'hold') puff(entry, gentle ? 8 : 22, { x: head.row.x });
          head.phase = p.phase;
        }
      } else {
        let motion = 0;
        for (const head of entry.heads) {
          const p = mascaraPhase(head.row, clock); head.pivot.rotation.z = p.angle; head.pivot.position.y = p.pivotY;
          motion = Math.max(motion, Math.abs(p.vx) / (K.brushAmplitudeM * Math.PI * 2 / head.row.cycleS));
        }
        entry.opacity.value = (.1 + .12 * Math.min(1, motion)) * (gentle ? .65 : 1);
      }
    }
    for (const belt of belts) {
      const p = ribbonPhase(belt.row, clock), length = belt.row.s1 - belt.row.s0;
      beltTarget.set(p.direction > 0 ? '#94e2c8' : p.direction < 0 ? '#f29ab4' : '#e8c675');
      belt.colour.value.lerp(beltTarget, 1 - Math.exp(-Math.max(0, dt) / .3));
      for (let i = 0; i < belt.count; i++) {
        const shift = p.travelM ?? p.direction * clock * 5, d = ((i * length / belt.count + shift) % length + length) % length;
        const f = track.surfaceAt(belt.row.s0 + d, 0); Q.setFromRotationMatrix(basis(f));
        if (p.direction < 0) Q.multiply(reverse);
        P.fromArray(f.p).addScaledVector(Y.fromArray(f.n), .09); belt.arrows.setMatrixAt(i, M.compose(P, Q, Scale.set(1, 1, 1)));
      }
      belt.arrows.instanceMatrix.needsUpdate = true;
    }
    for (let i = burst.length - 1; i >= 0; i--) if (clock < burst[i].born || clock - burst[i].born > 1.25) burst.splice(i, 1);
    dust.count = active ? burst.length : 0;
    burst.forEach((particle, i) => {
      const age = clock - particle.born, fade = 1 - age / 1.25;
      P.set(...particle.p.map((v, j) => v + particle.v[j] * age)); P.y -= age * age * 1.8;
      dust.setMatrixAt(i, M.compose(P, Q.identity(), Scale.setScalar(fade * (gentle ? .6 : 1))));
    }); dust.instanceMatrix.needsUpdate = true;
  }
  function reset() { burst.length = 0; entries.forEach(e => { e.phase = null; e.pressHeads?.forEach(h => { h.phase = null; }); });
    belts.forEach(b => b.colour.value.set('#94e2c8')); dust.count = 0; update(0, { active: false }); }
  reset();
  return { group, plan, entries, belts, update, reset,
    hit(event) { const e = byId.get(event?.id); if (e) puff(e, 18, event); },
    dispose() { if (disposed) return; disposed = true; group.removeFromParent(); group.traverse(o => { if (o.isInstancedMesh) o.dispose(); }); geometries.forEach(g => g.dispose()); materials.forEach(m => m.dispose()); },
  };
}
