/* Mainframe: original server-rack colossi and fixed cable conduits, built before shader warmup.
 * Static instanced architecture and three animated Loom displays. Scan timing comes from the shared sim law. */
import { Group, Mesh, InstancedMesh, BufferGeometry, Float32BufferAttribute, BoxGeometry, CylinderGeometry, TorusGeometry, SphereGeometry, CircleGeometry, Shape, Path, ShapeGeometry, CanvasTexture, SRGBColorSpace, MeshBasicNodeMaterial, Matrix4, Quaternion, Vector3, DoubleSide } from 'three/webgpu';
import { vec3, uniform, mix, texture, positionWorld, float, uv, smoothstep } from 'three/tsl';
import { scanZones, scanPose } from '../sim/mainframeScan.js';
import { sampleTrack, bodyClear, WORLD_DEFAULTS } from './colossiMath.js';
import { Q } from './quality.js';
import { createMachineMaterial, coolantCaustics } from './mainframeMaterials.js';
import { createSubmergedBubbleMaterial } from './underwaterFx.js';
import { createLoomDisplayFrames } from './loomVariety.js';
import { buildFloatingProcessors } from './mainframeFloat.js';
import { mediaLook, placeBillboard, rectPoints } from './mediaMath.js';
import { fitMainframeBillboard, motionEnvelope } from './mainframeMediaMotion.js';
import { buildMainframeRooms, mainframeRoomWindows } from './mainframeRooms.js';
import { buildMainframeHazards } from './mainframeHazards.js';
import { createAudioReactiveUniforms, reactiveWaterNodes, waterHelixLight } from './audioReactive.js';

const point = (f, x, y, z = 0) => f.p.map((v, i) => v + f.b[i] * x + f.n[i] * y + f.t[i] * z);

/** Fit the circular reactor room from the route itself, including translated or rotated courses. */
export function reactorRoom(track) {
  const section = track.sections.find(s => s.id === 'spiral-core');
  if (!section) return null;
  const frames = [1 / 6, 1 / 2, 5 / 6].map(u => track.frameAt(section.fromS + (section.toS - section.fromS) * u));
  const [[ax, , az], [bx, , bz], [cx, , cz]] = frames.map(f => f.p);
  const d = 2 * (ax * (bz - cz) + bx * (cz - az) + cx * (az - bz));
  if (Math.abs(d) < 1e-6) return null;
  const a = ax * ax + az * az, b = bx * bx + bz * bz, c = cx * cx + cz * cz;
  const x = (a * (bz - cz) + b * (cz - az) + c * (az - bz)) / d;
  const z = (a * (cx - bx) + b * (ax - cx) + c * (bx - ax)) / d;
  const all = Array.from({ length: 25 }, (_, i) => track.frameAt(section.fromS + (section.toS - section.fromS) * i / 24));
  const low = Math.min(...all.map(f => f.p[1])), high = Math.max(...all.map(f => f.p[1]));
  const radius = Math.min(...all.map(f => Math.hypot(f.p[0] - x, f.p[2] - z) - f.w / 2 - 11));
  return { center: [x, (low + high) / 2, z], radius: Math.max(1, radius), height: high - low + 60 };
}

export function buildMainframe(track, art, { samples = sampleTrack(track, 4), makeCanvas = null, mediaLayout = null, reactive = createAudioReactiveUniforms() } = {}) {
  const group = new Group(); group.name = 'mainframe';
  const geometry = new BoxGeometry(1, 1, 1), geometries = [geometry], materials = [], textures = [], rows = { shell: [], slot: [], green: [], cyan: [], floor: [], wall: [], tank: [], rib: [], amber: [] };
  let loomFrames = null;
  const coolantTime = uniform(0), loomContrast = uniform(1), displays = [], landmarks = [];
  const room = reactorRoom(track);
  const reservoir = { waterY: track.reservoir?.waterY ?? 4,
    submerged: track.sections.find(s => s.id === 'submerged'), emerge: track.sections.find(s => s.id === 'emerge') };
  const displaySpecs = ['boot-hall', 'core-approach', 'spiral-core'].map(id => {
    const section = track.sections.find(s => s.id === id);
    return section ? id === 'boot-hall' ? section.fromS + (section.toS - section.fromS) * 0.7 : section.toS - 25 : 0;
  }).filter(s => s > 0);
  const add = (kind, f, x, y, z, size) => rows[kind].push({ p: point(f, x, y, z), b: [...f.b], n: [...f.n], t: [...f.t], size });
  const length = track.lengthM, count = Q.name === 'phone' ? 34 : 60;
  const roomWindows = mainframeRoomWindows(track);
  const roomBay = s => roomWindows.some(room => s >= room.fromS && s <= room.toS);
  // Use the media renderer's fitted faces, including any clearance-driven outward adjustment.
  // Long display bays expose the whole picture while it is still 30-60 m ahead of the driver.
  const mediaLookNow = mediaLook();
  const billboards = (mediaLayout?.billboards || []).map(b => fitMainframeBillboard(b, track, samples, mediaLookNow)).filter(Boolean)
    .map(b => motionEnvelope(b, mediaLookNow));
  const displayBay = (s, side) => billboards.find(b => {
    const d = ((s - b.s + length * 1.5) % length) - length / 2;
    return b.side === side && d >= -80 - b.w / 2 && d <= b.w / 2 + 16;
  });
  const sectionAt = (s) => track.sections[track.sectionAt(s)]?.id || '';
  const isCanal = (s) => sectionAt(s).includes('canal');
  const isCore = (s) => /spiral|core|bus-descent/.test(sectionAt(s));
  const hasSpills = (s) => /spillway|coolant-chute|bus-descent/.test(sectionAt(s));
  const hasGap = (s) => track.nodeFlags(track.frameAt(s).seg).includes('jump') || (track.fields || []).some((v) => s < v.toS && s + 8 > v.fromS);
  // A capsule check against the WHOLE road keeps every rack out of nearby switchbacks too.
  for (let i = 0; i < count; i++) {
    const s = (i + 0.5) * length / count, f = track.frameAt(s), side = i % 2 ? 1 : -1;
    if (displayBay(s, side) || roomBay(s)) continue;
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
  // The course is inside a machine: continuous metal aisles, real overhead ribs and maintenance decks.
  const clearance = { ...WORLD_DEFAULTS, sideM: 0.3, clearM: 6, underM: 1 };
  for (let s = 0; s < length; s += 8) {
    const f = track.frameAt(s + 4), id = sectionAt(s), core = isCore(s), canal = isCanal(s), under = /submerged|emerge/.test(id);
    const half = f.w / 2, gap = hasGap(s), spills = hasSpills(s);
    // Leave room for bank changes between each slab's midpoint and the real road at its ends.
    if (!gap && !canal) add('floor', f, 0, -1.9, 0, [spills ? f.w - 1.2 : f.w + 13, 1.4, 8.5]);
    for (const side of [-1, 1]) {
      if (!gap && !spills && !displayBay(s + 4, side)) add('rib', f, side * (half + 3), -0.2, 0, [3.8, 0.6, 8.5]);
      if (canal) continue;
      if (spills) continue; // Rails belong to the road; the open sides expose the falling coolant.
      if (core && room && (f.p[0] - room.center[0]) * f.b[0] * side + (f.p[2] - room.center[2]) * f.b[2] * side < 0) continue;
      const off = side * (half + 8), baseHeight = under ? Math.max(19, reservoir.waterY - f.p[1] + 8) : 28;
      const exit = id === 'emerge' ? Math.max(0, Math.min(1, (s - reservoir.emerge.fromS) / (reservoir.emerge.toS - reservoir.emerge.fromS))) : 0;
      const blend = exit * exit * (3 - 2 * exit), height = baseHeight + (28 - baseHeight) * blend;
      if (!bodyClear({ B: point(f, off, -2), D: f.n, len: height, r: 4.3 }, samples, clearance).ok) continue;
      if (displayBay(s + 4, side) || roomBay(s + 4)) {
        // Continuous lintel and sill frame the opening; solid adjacent wall segments form the jambs.
        add('rib', f, off, height - 3, 0, [1.8, 2, 8.5]);
        add('rib', f, off, -1, 0, [1.8, 1, 8.5]);
        add('cyan', f, off - side, height - 4.1, 0, [0.12, 0.16, 8.5]);
        continue;
      }
      add(under ? 'tank' : 'wall', f, off, height / 2 - 2, 0, [1.5, height, 8.5]);
      add('slot', f, off - side * 0.95, 5, 0, [0.5, 9, 6.2]);
      add(under ? 'cyan' : id.includes('boot') ? 'green' : 'amber', f, off - side * 1.25, 9.6, 0, [0.22, 0.22, 6.7]);
      if (Math.round(s / 8) % 3 === 0) {
        add('rib', f, off - side * 0.7, 11, 0, [2.2, 26, 1.2]);
        add('cyan', f, off - side * 1.9, 3.5, 0, [0.15, 5.5, 0.7]);
      }
    }
    if (!core && !canal && !under && !displaySpecs.some(d => s > d - 100 && s < d + 20) && Math.round(s / 8) % 3 === 0
      && bodyClear({ B: point(f, 0, 27), D: f.n, len: 0, r: half + 8 }, samples, clearance).ok) {
      add('rib', f, 0, 27, 0, [f.w + 17, 1.6, 2]);
      add('floor', f, 0, 28, 0, [f.w + 17, 0.7, 24.5]);
      add('cyan', f, 0, 26.1, 0, [Math.min(12, f.w / 2), 0.12, 0.8]);
    }
  }
  // Bundles follow the transported road frame. Never bridge a jump or a stone-field opening.
  for (let s = 0; s < length; s += 5) {
    const f = track.frameAt(s + 2.5);
    if (hasSpills(s) || track.nodeFlags(f.seg).includes('jump') || (track.fields || []).some((v) => s < v.toS && s + 5 > v.fromS)) continue;
    for (const side of [-1, 1]) {
      if (displayBay(s + 2.5, side)) continue;
      for (let wire = 0; wire < 3; wire++) add('slot', f, side * (f.w / 2 + 0.85 + wire * 0.26), 0.45, 0, [0.2, 0.2, 5.15]);
      if (Math.round(s / 5) % 3 === 0) add('cyan', f, side * (f.w / 2 + 1.1), 0.46, 0, [0.85, 0.29, 0.16]);
    }
  }
  // Cooling towers mark the bends; a taller pair brackets each platform gallery outside its void.
  const towerSites = track.sections.filter(s => /canal|chicane|switchback|gantry-turn|spillway-turn/.test(s.id))
    .map(s => ({ s: (s.fromS + s.toS) / 2, gallery: false }));
  for (const field of track.fields || []) for (const s of [field.fromS - 16, field.toS + 16]) towerSites.push({ s, gallery: true });
  for (const site of towerSites.slice(0, 8)) for (const side of [-1, 1]) {
    if (displayBay(site.s, side)) continue;
    const f = track.frameAt(site.s), height = site.gallery ? 58 : 46, radius = site.gallery ? 11 : 9;
    for (let off = f.w / 2 + radius + 16, tries = 0; tries < 10; tries++, off += 8) {
      const capsule = { B: point(f, side * off, -8), D: f.n, len: height, r: radius };
      if (!bodyClear(capsule, samples).ok) continue;
      landmarks.push({ f, off: side * off, height, radius, capsule, gallery: site.gallery });
      add('rib', f, side * off, -7, 0, [radius * 2.1, 2, radius * 2.1]);
      break;
    }
  }
  const basis = new Matrix4(), matrix = new Matrix4(), q = new Quaternion(), p = new Vector3(), size = new Vector3();
  const x = new Vector3(), y = new Vector3(), z = new Vector3();
  const orient = (object, f, position) => {
    basis.makeBasis(x.fromArray(f.b), y.fromArray(f.n), z.fromArray(f.t).negate());
    object.quaternion.setFromRotationMatrix(basis); object.position.fromArray(position);
  };
  for (const [kind, hex] of Object.entries({ shell: '#405151', slot: '#152021', green: '#5de58d', cyan: '#39b4b5', floor: '#35484b', wall: '#50635d', tank: '#237389', rib: '#70807b', amber: '#efb751' })) {
    const mat = createMachineMaterial(hex, art, { glow: ['green', 'cyan', 'amber'].includes(kind), vent: kind === 'slot', coolant: kind === 'tank' ? coolantTime : null }); materials.push(mat);
    const batch = rows[kind], mesh = new InstancedMesh(geometry, mat, batch.length); mesh.name = 'mainframe-' + kind;
    batch.forEach((r, i) => {
      basis.makeBasis(x.fromArray(r.b), y.fromArray(r.n), z.fromArray(r.t).negate()); q.setFromRotationMatrix(basis);
      matrix.compose(p.fromArray(r.p), q, size.fromArray(r.size)); mesh.setMatrixAt(i, matrix);
    });
    mesh.instanceMatrix.needsUpdate = true; mesh.computeBoundingSphere(); group.add(mesh);
  }
  const keepGeo = (g) => { geometries.push(g); return g; };
  const keepMat = (m) => { materials.push(m); return m; };
  const metal = keepMat(createMachineMaterial('#50696c', art)), dark = keepMat(createMachineMaterial('#172c30', art));
  const light = keepMat(createMachineMaterial('#62e0bb', art, { glow: true }));
  const towerBody = new InstancedMesh(keepGeo(new CylinderGeometry(0.72, 1, 1, 6)), metal, Math.max(1, landmarks.length));
  const towerBands = new InstancedMesh(keepGeo(new TorusGeometry(1, 0.055, 4, 12).rotateX(Math.PI / 2)), light, Math.max(1, landmarks.length * 3));
  towerBody.name = 'mainframe-cooling-towers'; towerBands.name = 'mainframe-cooling-bands';
  towerBody.count = landmarks.length; towerBands.count = landmarks.length * 3;
  landmarks.forEach((r, i) => {
    basis.makeBasis(x.fromArray(r.f.b), y.fromArray(r.f.n), z.fromArray(r.f.t).negate()); q.setFromRotationMatrix(basis);
    matrix.compose(p.fromArray(point(r.f, r.off, r.height / 2 - 8)), q, size.set(r.radius, r.height, r.radius)); towerBody.setMatrixAt(i, matrix);
    for (let j = 0; j < 3; j++) {
      const h = r.height * (0.16 + j * 0.35), radius = r.radius * (1 - 0.28 * h / r.height) + 0.15;
      matrix.compose(p.fromArray(point(r.f, r.off, h - 8)), q, size.setScalar(radius)); towerBands.setMatrixAt(i * 3 + j, matrix);
    }
  });
  for (const mesh of [towerBody, towerBands]) { mesh.instanceMatrix.needsUpdate = true; mesh.computeBoundingSphere(); group.add(mesh); }
  // Each landmark wears a distinct evolving Loom field, warmed with the rest of the world.
  if (makeCanvas || typeof document !== 'undefined' || typeof OffscreenCanvas !== 'undefined') {
    const make = makeCanvas || ((w, h) => { const c = typeof document !== 'undefined' ? document.createElement('canvas') : new OffscreenCanvas(w,h); c.width = w; c.height = h; return c; });
    loomFrames = createLoomDisplayFrames(make, Q.name);
    for (const s of displaySpecs) {
      const f = track.frameAt(s), radius = s === displaySpecs[0] ? 21 : 29;
      let center = point(f, 0, radius + 16), fit = false;
      for (let i = 0; i < 20; i++) {
        if (bodyClear({ B: center, D: f.n, len: 0, r: radius + 1.5 }, samples, { ...WORLD_DEFAULTS, sideM: 1, underM: 1 }).ok) { fit = true; break; }
        center = center.map((v, k) => v + f.n[k] * 3);
      }
      if (!fit) continue;
      const holder = new Group(); orient(holder, f, center); group.add(holder);
      const row = loomFrames.rows[displays.length], tex = new CanvasTexture(row.canvas);
      tex.colorSpace = SRGBColorSpace; tex.anisotropy = 4; textures.push(tex);
      const mat = keepMat(new MeshBasicNodeMaterial({ side: DoubleSide }));
      mat.colorNode = mix(vec3(0.06, 0.24, 0.14), texture(tex).rgb, loomContrast);
      const disc = new Mesh(keepGeo(new CircleGeometry(radius, 64)), mat); disc.name = 'mainframe-loom-display'; holder.add(disc);
      const rim = new Mesh(keepGeo(new TorusGeometry(radius + 0.6, 1, 8, 64)), metal); holder.add(rim);
      displays.push({ disc, center, radius, frame: f, tex });
    }
  }
  // The raised road winds around a reactor sized to the current spiral's inner clearance.
  const core = new Group(); core.name = 'mainframe-reactor';
  if (room) { core.position.fromArray(room.center); core.scale.set(room.radius / 44.1, room.height / 108, room.radius / 44.1); }
  else core.visible = false;
  group.add(core);
  const drum = new Mesh(keepGeo(new CylinderGeometry(35, 39, 108, Q.name === 'phone' ? 32 : 64)), dark); core.add(drum);
  const coreRing = keepGeo(new TorusGeometry(43, 1.1, 8, 64).rotateX(Math.PI / 2));
  for (let h = -45; h <= 50; h += 19) { const ring = new Mesh(coreRing, metal); ring.position.y = h; core.add(ring); }
  const luminousRing = keepGeo(new TorusGeometry(39.4, 0.5, 6, 64).rotateX(Math.PI / 2));
  for (let h = -41; h <= 49; h += 6) { const ring = new Mesh(luminousRing, light); ring.position.y = h; core.add(ring); }
  for (let i = 0; i < 12; i++) {
    const a = i * Math.PI / 6, pillar = new Mesh(geometry, metal);
    pillar.position.set(Math.cos(a) * 39, 0, Math.sin(a) * 39); pillar.scale.set(2, 111, 2); core.add(pillar);
  }
  // The foundation opens below the islands: a textured floor, even far below, flattens their silhouette.
  const low = Math.min(...samples.map((f) => f.p[1])) - 45, high = Math.max(...samples.map((f) => f.p[1])) + 65;
  const boundaryPoints = [...samples.map(f => f.p), ...billboards.flatMap(b => rectPoints(b, 1)), ...landmarks.map(r => r.capsule.B)];
  const minX = Math.min(...boundaryPoints.map(p => p[0])) - 70, maxX = Math.max(...boundaryPoints.map(p => p[0])) + 70;
  const minZ = Math.min(...boundaryPoints.map(p => p[2])) - 70, maxZ = Math.max(...boundaryPoints.map(p => p[2])) + 70;
  const foundation = new Shape();
  foundation.moveTo(minX, -minZ); foundation.lineTo(maxX, -minZ);
  foundation.lineTo(maxX, -maxZ); foundation.lineTo(minX, -maxZ); foundation.closePath();
  for (const field of track.fields || []) {
    const ends = [track.frameAt(field.fromS), track.frameAt(field.toS)], hole = new Path();
    const corners = [point(ends[0], -ends[0].w / 2 - 40, 0, -45), point(ends[1], -ends[1].w / 2 - 40, 0, 45),
      point(ends[1], ends[1].w / 2 + 40, 0, 45), point(ends[0], ends[0].w / 2 + 40, 0, -45)];
    corners.forEach((p, i) => i ? hole.lineTo(p[0], -p[2]) : hole.moveTo(p[0], -p[2]));
    hole.closePath(); foundation.holes.push(hole);
  }
  const base = new Mesh(keepGeo(new ShapeGeometry(foundation).rotateX(-Math.PI / 2)), dark);
  base.name = 'mainframe-foundation'; base.position.y = low; group.add(base);
  for (const [px, pz, sx, sz] of [[minX, (minZ + maxZ) / 2, 3, maxZ - minZ], [maxX, (minZ + maxZ) / 2, 3, maxZ - minZ], [(minX + maxX) / 2, minZ, maxX - minX, 3], [(minX + maxX) / 2, maxZ, maxX - minX, 3]]) {
    const wall = new Mesh(geometry, dark); wall.position.set(px, (low + high) / 2, pz); wall.scale.set(sx, high - low, sz); group.add(wall);
  }
  const tubes = track.sections.filter((s) => /canal|submerged|emerge/.test(s.id));
  const bubbleRows = [], bubbleGeo = keepGeo(new SphereGeometry(0.10, 10, 7));
  for (const section of tubes) {
    for (let s = section.fromS + 5; s < section.toS; s += 18) {
      const f = track.frameAt(s), canal = section.id.includes('canal'), radius = canal ? 20.8 : f.w / 2 + 5, holder = new Group(); orient(holder, f, point(f, 0, canal ? 20 : 0)); holder.userData.trackS = s; group.add(holder);
      const exitT = section.id === 'emerge' ? Math.max(0, Math.min(1, (s - section.fromS) / (section.toS - section.fromS))) : 0;
      const open = exitT * exitT * (3 - 2 * exitT);
      const openRight = !!displayBay(s, 1), openLeft = !!displayBay(s, -1);
      const arc = openRight || openLeft ? Math.PI / 2 : Math.PI;
      const ribGeo = keepGeo(new TorusGeometry(radius, 0.8, 6, Q.name === 'phone' ? 16 : 32, arc).rotateZ(openRight ? Math.PI / 2 : 0));
      const rib = new Mesh(ribGeo, metal); rib.name = 'mainframe-tube-arch'; if (!(openRight && openLeft)) holder.add(rib);
      rib.scale.y = 1 + open * (27 / radius - 1);
      const glassMat = keepMat(new MeshBasicNodeMaterial({ color: canal ? '#79b7b1' : '#4198bc', transparent: true, opacity: 0.09 * (1 - open), depthWrite: false, side: DoubleSide }));
      const glass = new Mesh(keepGeo(new CylinderGeometry(radius, radius, 18, 32, 1, true, Math.PI / 2, Math.PI).rotateX(Math.PI / 2)), glassMat); holder.add(glass);
      glass.scale.y = rib.scale.y;
      if (!canal && reservoir.waterY - f.p[1] > 1) for (let i = 0; i < 18; i++) bubbleRows.push({ f, x: ((i * 7) % 23 - 11) * 1.15, phase: i / 18, s });
    }
  }
  if (reservoir.submerged) {
    const from = reservoir.submerged.fromS, to = reservoir.emerge?.toS ?? reservoir.submerged.toS;
    const n = Math.ceil((to - from) / 6), positions = [], uvs = [], indices = [];
    for (let i = 0; i <= n; i++) {
      const f = track.frameAt(from + (to - from) * i / n);
      for (const side of [-1, 1]) {
        const v = point(f, side * (f.w / 2 + 7), 0); positions.push(v[0], reservoir.waterY, v[2]); uvs.push((side + 1) / 2, i / n);
      }
      if (i < n) { const a = i * 2; indices.push(a, a + 2, a + 1, a + 1, a + 2, a + 3); }
    }
    const g = keepGeo(new BufferGeometry()); g.setAttribute('position', new Float32BufferAttribute(positions, 3));
    g.setAttribute('uv', new Float32BufferAttribute(uvs, 2)); g.setIndex(indices); g.computeVertexNormals();
    const mat = keepMat(new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, side: DoubleSide }));
    const response = reactiveWaterNodes(positionWorld.xz, reactive);
    mat.colorNode = vec3(0.025, 0.25, 0.39).add(vec3(0.07, 0.26, 0.34).mul(coolantCaustics(positionWorld.xz, coolantTime)))
      .add(vec3(0.14, 0.66, 0.64).mul(response.light)).add(vec3(0.7, 0.9, 1).mul(response.crest))
      .add(waterHelixLight(positionWorld.xz, reactive, vec3(.18,.85,.65)));
    mat.opacityNode = smoothstep(0, 0.035, uv().y).mul(float(1).sub(smoothstep(0.965, 1, uv().y))).mul(0.38);
    const surface = new Mesh(g, mat); surface.name = 'mainframe-reservoir-surface'; group.add(surface);
  }
  const bubbleMat = keepMat(createSubmergedBubbleMaterial());
  const bubbles = new InstancedMesh(bubbleGeo, bubbleMat, Math.max(1, bubbleRows.length)); bubbles.count = bubbleRows.length; bubbles.name = 'mainframe-coolant-bubbles'; group.add(bubbles);
  let bubbleTime = 0;
  function updateBubbles(dt) {
    bubbleTime += dt;
    bubbleRows.forEach((r, i) => {
      const depth = Math.max(0.5, reservoir.waterY - r.f.p[1] - 0.7);
      const height = ((bubbleTime * 0.65 + r.phase * depth) % depth) + 0.35;
      p.fromArray(point(r.f, r.x + Math.sin(r.phase * 20 + bubbleTime * 0.4) * 0.3, height));
      p.y = Math.min(p.y, reservoir.waterY - 0.2);
      matrix.compose(p, q.identity(), size.setScalar(0.5 + r.phase)); bubbles.setMatrixAt(i, matrix);
    });
    bubbles.instanceMatrix.needsUpdate = true; bubbles.computeBoundingSphere();
  }
  updateBubbles(0);
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
    const height = zone.height ?? 5.5;
    panel.scale.set(zone.width, height, zone.len); panel.position.y = height / 2;
    floor.scale.set(zone.width, 0.035, zone.len); holder.add(panel, floor);
    const floorTiles = sectionAt(zone.s).includes('canal') ? new InstancedMesh(geometry, floorMat, Math.ceil(zone.width / 0.8)) : null;
    if (floorTiles) { floor.visible = false; holder.add(floorTiles); }
    // Permanent dark gantry and two steady green edge marks show the trap before it activates.
    const frameMat = createMachineMaterial('#687d74', art); materials.push(frameMat);
    for (const side of [-1, 1]) {
      const post = new Mesh(geometry, frameMat); post.scale.set(1.3, height + 2.5, 1.8);
      post.position.set(side * (f.w / 2 + 1), height / 2 + 1, 0); holder.add(post);
    }
    const beam = new Mesh(geometry, frameMat); beam.scale.set(f.w + 3.5, 1.5, 2); beam.position.y = height + 1.6; holder.add(beam);
    const stripeMat = keepMat(new MeshBasicNodeMaterial({ color: '#8bffbd', transparent: true, opacity: 0.04, depthWrite: false }));
    const barRows = [];
    for (const x of [-zone.width / 2, zone.width / 2]) for (const z of [-zone.len / 2, zone.len / 2]) {
      barRows.push({ x, y: height / 2, z, w: 0.2, h: height, d: 0.2 });
    }
    for (let x = -zone.width / 2; x <= zone.width / 2; x += 1.2) {
      barRows.push({ x, y: height / 2, z: zone.len / 2, w: 0.12, h: height, d: 0.1 });
    }
    const bars = new InstancedMesh(geometry, stripeMat, barRows.length); holder.add(bars);
    barRows.forEach((r, i) => { matrix.makeScale(r.w, r.h, r.d).setPosition(r.x, r.y, r.z); bars.setMatrixAt(i, matrix); });
    bars.instanceMatrix.needsUpdate = true; bars.computeBoundingSphere();
    const lamps = new Mesh(geometry, stripeMat); lamps.scale.set(zone.width, 0.45, 0.35); lamps.position.set(0, height + 0.6, 0); holder.add(lamps);
    const sparks = new InstancedMesh(keepGeo(new SphereGeometry(.15, 5, 4)), stripeMat, Q.name === 'phone' ? 24 : 80);
    sparks.frustumCulled = false; sparks.name = 'laser-ion-sparks'; holder.add(sparks);
    return { zone, frame: f, panel, floor, floorTiles, panelMat, floorMat, bars, stripeMat, sparks };
  });
  const floating = buildFloatingProcessors(track, art, samples, billboards); group.add(floating.group);
  const rooms = buildMainframeRooms(track, art, { samples, mediaLayout }); group.add(rooms.group);
  const industry = buildMainframeHazards(track, art); group.add(industry.group);
  return { group, scans, core, tubes, displays, landmarks, reservoir, floating, rooms, industry, counts: Object.fromEntries(Object.entries(rows).map(([k, r]) => [k, r.length])),
    update(dt, view, gentle = false) {
      if (view.paused) return;
      floating.update(dt, gentle);
      rooms.update(dt, { gentle, paused: view.paused });
      industry.update(dt, { timeS: view.raceTimeS ?? 0, gentle, paused: view.paused });
      updateBubbles(dt * (gentle ? 0.5 : 1));
      coolantTime.value += dt * (gentle ? 0.5 : 1);
      loomContrast.value += ((gentle ? 0.5 : 1) - loomContrast.value) * Math.min(1, dt / 0.3);
      if (loomFrames?.update(dt, gentle, view)) for (const display of displays) display.tex.needsUpdate = true;
      for (const display of displays) display.disc.rotation.z += dt * (gentle ? 0.18 : 0.45);
      for (const scan of scans) {
        const pose = scanPose(scan.zone, view.scanTimeS ?? view.timeS ?? 0, view.scanBpm || 120);
        if (view.scanReady === false) { pose.phase = 'warn'; pose.opacity = 0.18; }
        scan.panel.position.x = scan.floor.position.x = scan.bars.position.x = pose.x;
        if (scan.floorTiles && track.surfaceAt) {
          const f = scan.frame, n = scan.floorTiles.count, width = scan.zone.width / n;
          for (let i = 0; i < n; i++) {
            const lateral = pose.x - scan.zone.width / 2 + (i + 0.5) * width;
            const sf = track.surfaceAt(scan.zone.s + scan.zone.len / 2, lateral);
            const height = sf.p.reduce((h, v, k) => h + (v - f.p[k]) * f.n[k], 0);
            const sideN = sf.n.reduce((v, k, j) => v + k * f.b[j], 0), upN = sf.n.reduce((v, k, j) => v + k * f.n[j], 0);
            p.set(lateral, height + 0.06, 0); q.setFromAxisAngle(z.set(0, 0, 1), Math.atan2(-sideN, upN));
            matrix.compose(p, q, size.set(width + 0.03, 0.04, scan.zone.len)); scan.floorTiles.setMatrixAt(i, matrix);
          }
          scan.floorTiles.instanceMatrix.needsUpdate = true; scan.floorTiles.computeBoundingSphere();
        }
        const warning = pose.phase === 'warn', active = pose.phase === 'sweep' || pose.phase === 'hold';
        scan.panelMat.color.set(pose.effect === 'boost' ? '#57ff9d' : '#ffd04e'); scan.stripeMat.color.copy(scan.panelMat.color);
        const scanTime = view.scanTimeS ?? view.timeS ?? 0;
        const flutter = pose.changingSoon && !gentle ? .72 + .28 * Math.sin(scanTime * Math.PI * 2) ** 2 : 1;
        for (let j = 0; j < scan.sparks.count; j++) {
          const phase = ((scanTime * (gentle ? .2 : .45) + j * .618) % 1 + 1) % 1;
          const x = pose.x + (j / scan.sparks.count - .5) * scan.zone.width;
          matrix.makeScale(.6 + phase, .6 + phase, .6 + phase).setPosition(x,
            .25 + phase * (scan.zone.height ?? 5.5), Math.sin(j * 2.4 + scanTime * .4) * scan.zone.len / 2);
          scan.sparks.setMatrixAt(j, matrix);
        }
        scan.sparks.instanceMatrix.needsUpdate = true;
        const intensity = gentle ? 0.75 : 1, target = active ? 0.52 * intensity : warning ? 0.12 * intensity : 0.015;
        // 300 ms minimum full rise, with the same collision timing and bypass on every setting.
        const step = Math.min(1, dt / (gentle ? 0.3 : 0.18));
        scan.panelMat.opacity += (target - scan.panelMat.opacity) * step;
        scan.stripeMat.opacity += ((active ? 0.9 : warning || pose.changingSoon ? 0.5 : 0.04) * intensity * flutter - scan.stripeMat.opacity) * step;
        scan.floorMat.color.copy(scan.panelMat.color);
        scan.floorMat.opacity += (Math.max(0.025, warning ? 0.4 : target * 0.65) - scan.floorMat.opacity) * step;
      }
    },
    dispose() {
      rooms.dispose(); industry.dispose();
      floating.dispose();
      group.removeFromParent(); group.traverse((o) => { if (o.isInstancedMesh) o.dispose(); });
      geometries.forEach((g) => g.dispose()); materials.forEach((m) => m.dispose());
      textures.forEach((t) => t.dispose()); loomFrames?.dispose();
    } };
}
