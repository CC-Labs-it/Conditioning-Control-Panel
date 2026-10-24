/* Solid fortress construction around the authored course. One instanced draw per finish. */
import { Group, Mesh, InstancedMesh, BoxGeometry, CylinderGeometry, MeshBasicNodeMaterial, Matrix4, Quaternion, Vector3, Color } from 'three/webgpu';
import { materialColor, positionWorld, normalWorld, normalize, dot, clamp, vec3, texture, mix, float, fract, smoothstep, fwidth } from 'three/tsl';
import { LOOK } from './look.js';

export function keepStoneMaterial(hex, art, mortar = true) {
  const material = new MeshBasicNodeMaterial({ color: new Color(hex) });
  const N = normalize(normalWorld), light = clamp(dot(N, normalize(vec3(...LOOK.lightDir))).mul(0.4).add(0.6), 0.28, 1);
  const map = art?.tex('road-glass-roughness');
  const grain = map ? texture(map, positionWorld.xz.div(7)).r.add(texture(map, positionWorld.xy.div(7)).r).mul(0.5) : float(0.5);
  let colour = materialColor.rgb.mul(light).mul(grain.mul(0.28).add(0.84));
  if (mortar) {
    const y = positionWorld.y.div(2.4), edge = fract(y), aa = fwidth(y).max(0.008);
    const joint = smoothstep(0.015, aa.add(0.035), edge).mul(smoothstep(0.015, aa.add(0.035), float(1).sub(edge)));
    colour = colour.mul(mix(float(0.64), float(1), joint));
  }
  material.colorNode = colour;
  return material;
}

/** Physical construction is separate from accepted prop meshes; all driving clearances stay empty. */
export function buildKeepFortress(track, layout, art) {
  const group = new Group(); group.name = 'keep-solid-fortress';
  const owned = [], batches = { deck: [], wall: [], cap: [], iron: [] }, supports = [];
  const box = new BoxGeometry(1, 1, 1), cylinder = new CylinderGeometry(1, 1, 1, art?.tier === 'phone' ? 32 : 64);
  owned.push(box, cylinder);
  const M = new Matrix4(), B = new Matrix4(), Q = new Quaternion(), P = new Vector3(), S = new Vector3();
  const X = new Vector3(), Y = new Vector3(), Z = new Vector3();
  const groundY = Math.min(-20, ...(layout.towers || []).map((t) => t.baseY));
  const landmarkAt = (s) => layout.landmarks.find((r) => s >= r.s0 && s <= r.s1);
  function block(batch, f, x, y, size) {
    B.makeBasis(X.fromArray(f.b), Y.fromArray(f.n), Z.fromArray(f.t).negate());
    Q.setFromRotationMatrix(B); P.fromArray(f.p).addScaledVector(X, x).addScaledVector(Y, y);
    batches[batch].push(M.compose(P, Q, S.fromArray(size)).clone());
  }
  function worldBlock(batch, p, size, angle = 0) {
    Q.setFromAxisAngle(Y.set(0, 1, 0), angle);
    batches[batch].push(M.compose(P.fromArray(p), Q, S.fromArray(size)).clone());
  }
  const count = Math.ceil(track.lengthM / 6), step = track.lengthM / count, frames = [];
  for (let i = 0; i < count; i++) {
    const f = track.frameAt((i + 0.5) * step);
    frames.push({ s: (i + 0.5) * step, p: [...f.p], b: [...f.b], n: [...f.n], t: [...f.t], w: f.w });
  }
  for (let i = 0; i < frames.length; i++) {
    const f = frames[i], name = landmarkAt(f.s)?.id || '';
    const spiral = /spiral/.test(name), hall = /gate|hall/.test(name), h = hall ? 22 : spiral ? 3.5 : 7;
    block('deck', f, 0, -2.05, [f.w + 3.4, 4, step + 1.8]);
    for (const side of [-1, 1]) {
      const x = side * (f.w / 2 + 1.35);
      block('wall', f, x, h / 2 - 0.2, [2.1, h, step + 0.35]);
      block('cap', f, x, h + 0.15, [2.7, 0.65, step + 0.35]);
      if (i % 2 === 0) block('cap', f, x, h + 1.25, [2.5, 1.7, 2.8]);
      if (i % 4 === 0) block('wall', f, side * (f.w / 2 + 2.8), h / 2, [2.5, h + 2, 3.1]);
      if (i % 3 === 0) block('iron', f, side * (f.w / 2 + 0.2), Math.min(h - 1, 4), [0.22, 2.4, 0.8]);
    }
    if (hall) {
      block('deck', f, 0, 24, [f.w + 6, 2.4, step + 0.5]);
      if (i % 3 === 0) block('cap', f, 0, 22.3, [f.w + 2.5, 0.8, 1.1]);
    }
    // Foundations stop under stacked roads. The spiral is carried by the central tower instead.
    if (!spiral && i % 5 === 0 && f.p[1] > groundY + 8) {
      const otherBelow = frames.some((o) => Math.abs(o.s - f.s) > 36 && o.p[1] < f.p[1] - 5 &&
        Math.hypot(o.p[0] - f.p[0], o.p[2] - f.p[2]) < (o.w + f.w) / 2 + 3);
      if (!otherBelow) supports.push({ p: [f.p[0], (groundY + f.p[1] - 4) / 2, f.p[2]], size: [f.w + 1.5, f.p[1] - 4 - groundY, 10] });
    }
  }
  for (const support of supports) worldBlock('wall', support.p, support.size);
  const stone = keepStoneMaterial('#42404b', art), deck = keepStoneMaterial('#30303a', art);
  const cap = keepStoneMaterial('#67606c', art), iron = keepStoneMaterial('#cc3b8e', art, false);
  owned.push(stone, deck, cap, iron);
  const plinths = new InstancedMesh(cylinder, stone, 24), crowns = new InstancedMesh(cylinder, cap, 24);
  plinths.name = 'keep-colossus-foundations'; crowns.name = 'keep-colossus-crowns';
  plinths.count = crowns.count = 0; group.add(plinths, crowns);
  function addPlinth(p, radius) {
    const i = plinths.count, height = p[1] - groundY;
    if (i >= 24 || height <= 0) return;
    Q.identity(); M.compose(P.set(p[0], groundY + height / 2, p[2]), Q, S.set(radius, height, radius));
    plinths.setMatrixAt(i, M); plinths.count++;
    M.compose(P.set(p[0], p[1] - 0.45, p[2]), Q, S.set(radius + 1.5, 0.9, radius + 1.5));
    crowns.setMatrixAt(i, M); crowns.count++;
    for (const mesh of [plinths, crowns]) { mesh.instanceMatrix.needsUpdate = true; mesh.computeBoundingSphere(); }
  }
  const bounds = frames.reduce((a, f) => ({ minX: Math.min(a.minX, f.p[0]), maxX: Math.max(a.maxX, f.p[0]),
    minZ: Math.min(a.minZ, f.p[2]), maxZ: Math.max(a.maxZ, f.p[2]) }), { minX: Infinity, maxX: -Infinity, minZ: Infinity, maxZ: -Infinity });
  const center = [(bounds.minX + bounds.maxX) / 2, groundY - 1, (bounds.minZ + bounds.maxZ) / 2];
  const moat = keepStoneMaterial('#151824', art, false); owned.push(moat);
  const ground = new Mesh(box, moat); ground.name = 'keep-black-moat'; ground.position.fromArray(center);
  ground.scale.set(bounds.maxX - bounds.minX + 2400, 2, bounds.maxZ - bounds.minZ + 2400); group.add(ground);
  // The spiral wraps an actual stone volume. Crown drums and crenels give it a visible skyline.
  for (const t of layout.towers || []) {
    const body = new Mesh(cylinder, stone); body.name = 'keep-tower-volume';
    body.position.set(t.x, (t.baseY + t.topY) / 2, t.z); body.scale.set(t.radiusM, t.topY - t.baseY, t.radiusM); group.add(body);
    const crown = new Mesh(cylinder, cap); crown.position.set(t.x, t.topY, t.z); crown.scale.set(t.radiusM + 2, 2.8, t.radiusM + 2); group.add(crown);
    const n = Math.max(12, Math.round(t.radiusM * 0.75));
    for (let i = 0; i < n; i++) {
      const a = i / n * Math.PI * 2;
      worldBlock('cap', [t.x + Math.sin(a) * t.radiusM, t.topY + 3, t.z + Math.cos(a) * t.radiusM], [3.6, 4, 3.6], a);
      if (i % 3 === 0) {
        const r = t.radiusM + 0.25;
        worldBlock('iron', [t.x + Math.sin(a) * r, t.topY - 9, t.z + Math.cos(a) * r], [2.3, 7, 0.55], a);
      }
    }
  }
  for (const [key, material] of [['deck', deck], ['wall', stone], ['cap', cap], ['iron', iron]]) {
    if (!batches[key].length) continue;
    const mesh = new InstancedMesh(box, material, batches[key].length); mesh.name = 'keep-' + key;
    batches[key].forEach((matrix, i) => mesh.setMatrixAt(i, matrix)); mesh.instanceMatrix.needsUpdate = true;
    mesh.computeBoundingSphere(); group.add(mesh);
  }
  return { group, addPlinth, counts: Object.fromEntries(Object.entries(batches).map(([k, a]) => [k, a.length])),
    dispose() { group.removeFromParent(); group.traverse((o) => { if (o.isInstancedMesh) o.dispose(); }); owned.forEach((o) => o.dispose()); } };
}
