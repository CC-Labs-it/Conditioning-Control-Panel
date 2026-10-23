/* Mainframe: original server-rack colossi and fixed cable conduits, built before shader warmup.
 * Four static instanced batches, no texture allocation. Scan timing comes from the shared sim law. */
import { Group, Mesh, InstancedMesh, BoxGeometry, MeshBasicNodeMaterial, Matrix4, Quaternion, Vector3, Color, DoubleSide } from 'three/webgpu';
import { vec3, normalWorld, normalize, dot, clamp } from 'three/tsl';
import { scanZones, scanPose } from '../sim/mainframeScan.js';
import { sampleTrack, bodyClear } from './colossiMath.js';
import { Q } from './quality.js';

const point = (f, x, y, z = 0) => f.p.map((v, i) => v + f.b[i] * x + f.n[i] * y + f.t[i] * z);
function material(hex, lit = true) {
  const c = new Color(hex), m = new MeshBasicNodeMaterial();
  const light = clamp(dot(normalize(normalWorld), normalize(vec3(0.35, 0.85, 0.4))).mul(0.4).add(0.6), 0.3, 1);
  m.colorNode = lit ? vec3(c.r, c.g, c.b).mul(light) : vec3(c.r, c.g, c.b);
  return m;
}

export function buildMainframe(track, art, { samples = sampleTrack(track, 4) } = {}) {
  const group = new Group(); group.name = 'mainframe';
  const geometry = new BoxGeometry(1, 1, 1), materials = [], rows = { shell: [], slot: [], green: [], cyan: [] };
  const add = (kind, f, x, y, z, size) => rows[kind].push({ p: point(f, x, y, z), b: [...f.b], n: [...f.n], t: [...f.t], size });
  const length = track.lengthM, count = Q.name === 'phone' ? 34 : 60;
  // A capsule check against the WHOLE road keeps every rack out of nearby switchbacks too.
  for (let i = 0; i < count; i++) {
    const f = track.frameAt((i + 0.5) * length / count), side = i % 2 ? 1 : -1;
    const height = 27 + (i * 17 % 5) * 5, width = 6 + (i % 3), depth = 5;
    let off = f.w / 2 + 13 + (i % 3) * 8, fit = false;
    for (let n = 0; n < 10; n++, off += 8) {
      if (bodyClear({ B: point(f, side * off, -7), D: f.n, len: height, r: Math.hypot(width, depth) / 2 }, samples).ok) { fit = true; break; }
    }
    if (!fit) continue;
    add('shell', f, side * off, height / 2 - 7, 0, [width, height, depth]);
    // Rack faces wrap all four sides, including the inner face seen along the road.
    for (let h = 1; h < height - 1; h += 2.4) {
      for (const face of [-1, 1]) {
        add('slot', f, side * off, h - 7, face * (depth / 2 + 0.06), [width - 0.6, 1.45, 0.18]);
        add(i % 3 ? 'green' : 'cyan', f, side * off - width / 2 + 1.1, h - 6.7, face * (depth / 2 + 0.18), [0.85, 0.32, 0.14]);
        add('green', f, side * off + width / 2 - 0.65, h - 7, face * (depth / 2 + 0.18), [0.2, 1.2, 0.14]);
        add('slot', f, side * off + face * (width / 2 + 0.06), h - 7, 0, [0.18, 1.45, depth - 0.5]);
        add('green', f, side * off + face * (width / 2 + 0.18), h - 6.7, -1.15, [0.14, 0.32, 1.15]);
        add('cyan', f, side * off + face * (width / 2 + 0.18), h - 7.05, 1.45, [0.14, 1.1, 0.2]);
      }
    }
    for (const edge of [-1, 1]) for (const face of [-1, 1]) {
      add('cyan', f, side * off + edge * (width / 2 - 0.15), height / 2 - 7, face * (depth / 2 + 0.15), [0.1, height - 0.4, 0.1]);
    }
  }
  // Bundles follow the transported road frame. Never bridge a jump or a stone-field opening.
  for (let s = 0; s < length; s += 5) {
    const f = track.frameAt(s + 2.5);
    if (track.nodeFlags(f.seg).includes('jump') || (track.fields || []).some((v) => s < v.toS && s + 5 > v.fromS)) continue;
    for (const side of [-1, 1]) {
      for (let wire = 0; wire < 3; wire++) add('slot', f, side * (f.w / 2 + 0.85 + wire * 0.26), 0.45, 0, [0.2, 0.2, 5.15]);
      if (Math.round(s / 5) % 3 === 0) add('cyan', f, side * (f.w / 2 + 1.1), 0.46, 0, [0.85, 0.29, 0.16]);
    }
  }
  const basis = new Matrix4(), matrix = new Matrix4(), q = new Quaternion(), p = new Vector3(), size = new Vector3();
  const x = new Vector3(), y = new Vector3(), z = new Vector3();
  const orient = (object, f, position) => {
    basis.makeBasis(x.fromArray(f.b), y.fromArray(f.n), z.fromArray(f.t).negate());
    object.quaternion.setFromRotationMatrix(basis); object.position.fromArray(position);
  };
  for (const [kind, hex] of Object.entries({ shell: '#203b3d', slot: '#091918', green: '#5de58d', cyan: '#39b4b5' })) {
    const mat = material(hex, kind === 'shell' || kind === 'slot'); materials.push(mat);
    const batch = rows[kind], mesh = new InstancedMesh(geometry, mat, batch.length); mesh.name = 'mainframe-' + kind;
    batch.forEach((r, i) => {
      basis.makeBasis(x.fromArray(r.b), y.fromArray(r.n), z.fromArray(r.t).negate()); q.setFromRotationMatrix(basis);
      matrix.compose(p.fromArray(r.p), q, size.fromArray(r.size)); mesh.setMatrixAt(i, matrix);
    });
    mesh.instanceMatrix.needsUpdate = true; mesh.computeBoundingSphere(); group.add(mesh);
  }
  // Delivered Mainframe cable spouts sit beside the stream, not over the driving corridor.
  const source = art?.model?.('spout-mainframe');
  if (source?.scene) {
    const water = samples.filter((f) => ['stream', 'pool'].includes(track.frameAt(f.s).surface));
    for (const f of water.filter((_, i) => i % 16 === 0).slice(0, 3)) {
      const body = source.scene.clone(true); orient(body, f, point(f, f.w / 2 + 9, -2)); group.add(body);
    }
  }
  const scans = scanZones(track).map((zone) => {
    const f = track.frameAt(zone.s + zone.len / 2), holder = new Group();
    orient(holder, f, point(f, 0, 0.06)); group.add(holder); holder.name = 'scan:' + zone.id;
    const panelMat = new MeshBasicNodeMaterial({ color: '#77ffad', transparent: true, opacity: 0.02, depthWrite: false, side: DoubleSide });
    const floorMat = new MeshBasicNodeMaterial({ color: '#b2f9bd', transparent: true, opacity: 0.02, depthWrite: false });
    materials.push(panelMat, floorMat);
    const panel = new Mesh(geometry, panelMat), floor = new Mesh(geometry, floorMat);
    panel.scale.set(zone.width, 5.5, zone.len); panel.position.y = 2.75;
    floor.scale.set(zone.width, 0.035, zone.len); holder.add(panel, floor);
    // Permanent dark gantry and two steady green edge marks show the trap before it activates.
    const frameMat = material('#295354'); materials.push(frameMat);
    for (const side of [-1, 1]) {
      const post = new Mesh(geometry, frameMat); post.scale.set(0.45, 6.5, 0.7);
      post.position.set(side * (f.w / 2 + 0.5), 3.25, 0); holder.add(post);
    }
    const beam = new Mesh(geometry, frameMat); beam.scale.set(f.w + 1.5, 0.45, 0.7); beam.position.y = 6.4; holder.add(beam);
    return { zone, panel, floor, panelMat, floorMat };
  });
  return { group, scans, counts: Object.fromEntries(Object.entries(rows).map(([k, r]) => [k, r.length])),
    update(dt, view, gentle = false) {
      if (view.paused) return;
      for (const scan of scans) {
        const pose = scanPose(scan.zone, view.scanTimeS ?? view.timeS ?? 0, view.scanBpm || 120);
        if (view.scanReady === false) { pose.phase = 'warn'; pose.opacity = 0.18; }
        scan.panel.position.x = scan.floor.position.x = pose.x;
        scan.panelMat.color.set(pose.phase === 'warn' ? '#53c5d4' : '#77ffad');
        const intensity = gentle ? 0.75 : 1, target = Math.max(0.02, pose.opacity * intensity);
        // 300 ms minimum full rise, with the same collision timing and bypass on every setting.
        const step = Math.min(1, dt / (gentle ? 0.3 : 0.18));
        scan.panelMat.opacity += (target - scan.panelMat.opacity) * step;
        scan.floorMat.opacity += (Math.max(0.025, pose.phase === 'warn' ? 0.23 : target * 0.5) - scan.floorMat.opacity) * step;
      }
    },
    dispose() {
      group.removeFromParent(); group.traverse((o) => { if (o.isInstancedMesh) o.dispose(); });
      geometry.dispose(); materials.forEach((m) => m.dispose());
    } };
}
