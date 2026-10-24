/* Original Keep landscape. No textures, downloads or animated sky bands. */
import { Group, Mesh, InstancedMesh, BufferGeometry, Float32BufferAttribute, BoxGeometry, CylinderGeometry, SphereGeometry, MeshBasicNodeMaterial, Matrix4, Quaternion, Vector3, Color, BackSide, FogExp2 } from 'three/webgpu';
import { materialColor, normalWorld, positionGeometry, normalize, dot, clamp, vec3, color, mix, smoothstep, sin, float } from 'three/tsl';
import { getKeepLayout } from '../track/keep-layout.js';
import { LOOK } from './look.js';

const TAU = Math.PI * 2;
function random(seed) { return () => { seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0; return seed / 4294967296; }; }

/** Deterministic world placements are inspectable without a renderer or a media provider. */
export function keepBackdropPlan(track) {
  const layout = getKeepLayout(track); if (!layout) return null;
  const b = layout.bounds, center = [(b.minX + b.maxX) / 2, 0, (b.minZ + b.maxZ) / 2];
  const courseRadius = Math.hypot(b.maxX - b.minX, b.maxZ - b.minZ) / 2;
  const groundY = b.minY - 22, moatY = groundY - 16, nearRadius = courseRadius + 480;
  const rocks = [[], [], []], walls = [], towers = [], crowns = [], spires = [], r = random(240106);
  for (let ring = 0; ring < 3; ring++) {
    const count = [64, 72, 80][ring], radius = nearRadius + ring * 480;
    for (let i = 0; i < count; i++) {
      const angle = (i + (r() - .5) * .4) / count * TAU, distance = radius + r() * 150;
      const height = 180 + ring * 90 + r() * (220 + ring * 120);
      rocks[ring].push({ p: [center[0] + Math.cos(angle) * distance, moatY - 90 + height / 2, center[2] + Math.sin(angle) * distance],
        size: [190 + r() * 170, height, 130 + r() * 160], yaw: r() * TAU });
    }
  }
  // Ruined curtain walls stand on the first cliff ring, with gaps and uneven crowns.
  for (let i = 0; i < 32; i++) {
    const angle = i / 32 * TAU, radius = nearRadius + 75, x = center[0] + Math.cos(angle) * radius, z = center[2] + Math.sin(angle) * radius;
    const foot = moatY + 65 + r() * 45, height = 95 + r() * 110, width = 24 + r() * 16;
    towers.push({ p: [x, foot + height / 2, z], size: [width, height, width], yaw: angle });
    rocks[0].push({ p: [x, foot - 75, z], size: [190, 190, 190], yaw: angle });
    for (let c = 0; c < 5; c++) if (r() > .15) {
      const a = c / 5 * TAU;
      crowns.push({ p: [x + Math.cos(a) * width * .73, foot + height + 5, z + Math.sin(a) * width * .73], size: [9, 10 + r() * 10, 9], yaw: a });
    }
    if (i % 3 === 0) spires.push({ p: [x, foot + height + 28, z], size: [width * .85, 64 + r() * 32, width * .85], yaw: angle });
    for (let j = 1; j <= 3; j++) if (r() > .23) {
      const a = (i + j / 4) / 32 * TAU, h = 48 + r() * 44;
      walls.push({ p: [center[0] + Math.cos(a) * radius, foot + h / 2, center[2] + Math.sin(a) * radius], size: [radius * TAU / 32 / 3.5, h, 15], yaw: Math.PI / 2 - a });
    }
  }
  return { center, groundY, moatY, courseRadius, nearRadius, skyRadiusM: 4000, rocks, walls, towers, crowns, spires };
}

function stone(hex) {
  const material = new MeshBasicNodeMaterial({ color: new Color(hex) });
  const light = clamp(dot(normalize(normalWorld), normalize(vec3(...LOOK.lightDir))).mul(.28).add(.72), .4, 1);
  material.colorNode = materialColor.rgb.mul(light); return material;
}
function overcast() {
  const material = new MeshBasicNodeMaterial({ side: BackSide, depthWrite: false, fog: false });
  const d = normalize(positionGeometry), elevation = d.y;
  const cloud = sin(d.x.mul(13).add(d.z.mul(7))).mul(sin(d.z.mul(17).sub(d.x.mul(4)))).mul(.5).add(.5);
  const streak = sin(d.x.mul(9).add(d.z.mul(14)).add(elevation.mul(54))).mul(.5).add(.5);
  const high = smoothstep(-.06, .65, elevation);
  let sky = mix(color('#90617b'), color('#171d32'), high);
  const band = float(1).sub(smoothstep(.025, .19, elevation.abs()));
  sky = mix(sky, color('#c17594'), band.mul(.38));
  sky = mix(sky, color('#34323f'), cloud.mul(streak).mul(smoothstep(.03, .25, elevation)).mul(.48));
  material.colorNode = sky; return material;
}
function cragGeometry() {
  const out = [], n = 7, rings = [], r = random(442);
  for (const [y, radius, dx, dz] of [[-.5, .72, 0, 0], [.1, .58, .08, -.04], [.5, .15, -.13, .05]]) {
    const ring = []; for (let i = 0; i < n; i++) { const a = i / n * TAU, k = radius * (.8 + r() * .4); ring.push([Math.cos(a) * k + dx, y + (r() - .5) * .13, Math.sin(a) * k + dz]); } rings.push(ring);
  }
  const tri = (a, b, c) => out.push(...a, ...b, ...c);
  for (let ring = 0; ring < 2; ring++) for (let i = 0; i < n; i++) {
    const j = (i + 1) % n; tri(rings[ring][i], rings[ring + 1][i], rings[ring][j]); tri(rings[ring][j], rings[ring + 1][i], rings[ring + 1][j]);
  }
  for (let i = 1; i < n - 1; i++) tri(rings[2][0], rings[2][i + 1], rings[2][i]);
  const geo = new BufferGeometry(); geo.setAttribute('position', new Float32BufferAttribute(out, 3)); geo.computeVertexNormals(); return geo;
}
function islandGeometry(radius, topY, baseY) {
  const out = [], n = 80, r = random(991), top = [], bottom = [];
  for (let i = 0; i < n; i++) { const a = i / n * TAU, k = radius * (.91 + r() * .09) * (1 + Math.max(0, Math.cos(a)) * .18);
    top.push([Math.cos(a) * k, topY + r() * 3, Math.sin(a) * k * .68]);
    bottom.push([Math.cos(a) * (k + 50), baseY, Math.sin(a) * (k + 50) * .68]); }
  for (let i = 0; i < n; i++) { const j = (i + 1) % n;
    out.push(0, topY, 0, ...top[j], ...top[i], ...top[i], ...top[j], ...bottom[i], ...bottom[i], ...top[j], ...bottom[j]); }
  const geo = new BufferGeometry(); geo.setAttribute('position', new Float32BufferAttribute(out, 3)); geo.computeVertexNormals(); return geo;
}

export function createKeepBackdrop(scene, track) {
  const plan = keepBackdropPlan(track); if (!plan) return null;
  const group = new Group(); group.name = 'keep-cliffs-and-overcast'; const owned = [];
  const box = new BoxGeometry(1, 1, 1), crag = cragGeometry(), drum = new CylinderGeometry(.82, 1, 1, 7), spike = new CylinderGeometry(0, 1, 1, 5);
  owned.push(box, crag, drum, spike);
  const matrix = new Matrix4(), rotation = new Quaternion(), up = new Vector3(0, 1, 0), position = new Vector3(), scale = new Vector3();
  function batch(name, geometry, hex, entries) {
    const material = stone(hex); owned.push(material); const mesh = new InstancedMesh(geometry, material, entries.length); mesh.name = name;
    entries.forEach((v, i) => { rotation.setFromAxisAngle(up, v.yaw || 0); matrix.compose(position.fromArray(v.p), rotation, scale.fromArray(v.size)); mesh.setMatrixAt(i, matrix); });
    mesh.instanceMatrix.needsUpdate = true; mesh.computeBoundingSphere(); group.add(mesh); return mesh;
  }
  plan.rocks.forEach((entries, i) => batch('keep-cliff-layer-' + i, crag, ['#34313b', '#4c3f50', '#695368'][i], entries));
  batch('keep-distant-curtain-walls', box, '#302b35', plan.walls);
  batch('keep-distant-prison-towers', drum, '#2c2933', plan.towers);
  batch('keep-distant-broken-crowns', box, '#463944', plan.crowns);
  batch('keep-distant-spires', spike, '#302a36', plan.spires);
  const island = islandGeometry(plan.courseRadius * .85, plan.groundY, plan.moatY - 45), islandMaterial = stone('#3b3540'); owned.push(island, islandMaterial);
  const ground = new Mesh(island, islandMaterial); ground.name = 'keep-rocky-island'; ground.position.set(plan.center[0], 0, plan.center[2]); group.add(ground);
  const moatGeometry = new CylinderGeometry(3100, 3100, 2, 80), moatMaterial = stone('#25202d'); owned.push(moatGeometry, moatMaterial);
  const moat = new Mesh(moatGeometry, moatMaterial); moat.name = 'keep-still-black-moat'; moat.position.set(plan.center[0], plan.moatY - 1, plan.center[2]); group.add(moat);
  const skyGeometry = new SphereGeometry(plan.skyRadiusM, 48, 28), skyMaterial = overcast(); owned.push(skyGeometry, skyMaterial);
  const sky = new Mesh(skyGeometry, skyMaterial); sky.name = 'keep-overcast-sky'; sky.position.fromArray(plan.center); sky.renderOrder = -10; sky.frustumCulled = false; group.add(sky);
  const oldFog = scene.fog, fog = new FogExp2('#563450', .00034); scene.fog = fog; scene.add(group);
  const stats = { sky: 1, skyRadiusM: plan.skyRadiusM, starfield: false, rocks: plan.rocks.flat().length, cliffLayers: plan.rocks.length,
    towers: plan.towers.length, ruinedWalls: plan.walls.length, spires: plan.spires.length, island: 1, moat: 1, draws: group.children.length };
  return { group, plan, stats, dispose() { group.removeFromParent(); if (scene.fog === fog) scene.fog = oldFog;
    group.traverse(o => { if (o.isInstancedMesh) o.dispose(); }); for (const item of owned) item.dispose(); } };
}