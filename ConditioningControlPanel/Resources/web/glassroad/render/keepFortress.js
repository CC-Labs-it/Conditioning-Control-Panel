/* Solid fortress construction around the authored course. One instanced draw per finish. */
import { Group, Mesh, InstancedMesh, BoxGeometry, CylinderGeometry, MeshBasicNodeMaterial, Matrix4, Quaternion, Euler, Vector3, Color } from 'three/webgpu';
import { materialColor, positionWorld, normalWorld, normalize, dot, clamp, vec3, texture, mix, float, fract, smoothstep, fwidth, uv, min, max, floor, hash, select } from 'three/tsl';
import { LOOK } from './look.js';
import { prisonPlan } from './keepPrison.js';

export function keepStoneMaterial(hex, art, mortar = true, outline = true) {
  const material = new MeshBasicNodeMaterial({ color: new Color(hex) });
  const N = normalize(normalWorld), light = clamp(dot(N, normalize(vec3(...LOOK.lightDir))).mul(0.58).add(0.64), 0.19, 1.14);
  const map = art?.tex('road-glass-roughness');
  const grain = map ? texture(map, positionWorld.xz.div(7)).r.add(texture(map, positionWorld.xy.div(7)).r).mul(0.5) : float(0.5);
  let colour = materialColor.rgb.mul(light).mul(smoothstep(.2, .8, grain).mul(.25).add(.87));
  if (mortar) {
    const y = positionWorld.y.div(2.4), edge = fract(y), aa = fwidth(y).max(0.008);
    const horizontal = smoothstep(.015, aa.add(.025), edge).mul(smoothstep(.015, aa.add(.025), float(1).sub(edge)));
    const across = select(N.x.abs().greaterThan(N.z.abs()), positionWorld.z, positionWorld.x).div(4.8);
    const staggered = across.add(fract(floor(y).mul(.5))), brick = fract(staggered), bx = fwidth(staggered).max(.006);
    const vertical = smoothstep(.008, bx.add(.018), brick).mul(smoothstep(.008, bx.add(.018), float(1).sub(brick)));
    const joint = horizontal.mul(vertical);
    const variation = hash(floor(staggered).add(floor(y).mul(37))).mul(.16).add(.9);
    colour = colour.mul(variation).mul(mix(float(.23), float(1), joint));
  }
  if (outline) {
    // A subpixel ink edge follows each primitive face. Derivatives keep it thin at every distance.
    const u = uv(), pixels = min(min(u.x, float(1).sub(u.x)).div(fwidth(u.x).max(.00001)),
      min(u.y, float(1).sub(u.y)).div(fwidth(u.y).max(.00001)));
    colour = mix(vec3(.0015, .001, .003), colour, smoothstep(.35, 1.05, pixels));
  }
  material.colorNode = colour;
  return material;
}

/** Physical construction is separate from accepted prop meshes; all driving clearances stay empty. */
export function buildKeepFortress(track, layout, art) {
  const group = new Group(); group.name = 'keep-solid-fortress';
  const owned = [], batches = { deck: [], stone: [], dark: [], cap: [], iron: [], roof: [], glow: [] };
  const box = new BoxGeometry(1, 1, 1), cylinder = new CylinderGeometry(1, 1, 1, 8);
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
  function worldBlock(batch, p, size, angle = 0, tilt = 0) {
    Q.setFromEuler(new Euler(0, angle, tilt, 'YXZ'));
    batches[batch].push(M.compose(P.fromArray(p), Q, S.fromArray(size)).clone());
  }
  const count = Math.ceil(track.lengthM / 6), step = track.lengthM / count, frames = [];
  for (let i = 0; i < count; i++) {
    const f = track.frameAt((i + 0.5) * step);
    frames.push({ s: (i + 0.5) * step, p: [...f.p], b: [...f.b], n: [...f.n], t: [...f.t], w: f.w, flags: f.flags });
  }
  const excluded = [...(track.jumps || []).map((j) => [j.s, j.landS]), ...(track.fields || []).map((f) => [f.fromS, f.toS])];
  if (layout.drawbridge) excluded.push([layout.drawbridge.rampS0, layout.drawbridge.gapS1]);
  const clearSpan = (s, half = step / 2 + 1.3) => !excluded.some(([a, b]) => s + half >= a && s - half <= b);
  for (let i = 0; i < frames.length; i++) {
    if (!clearSpan(frames[i].s)) continue;
    const f = frames[i], name = landmarkAt(f.s)?.id || '';
    const spiral = /spiral/.test(name), hall = layout.architecture.halls.some((a) => f.s >= a.s0 && f.s <= a.s1);
    // Stone supports the actual spline only. The bridge and stepping field retain open water below.
    const broken = (layout.brokenEdges || []).some((a) => f.s >= a.s0 - 5 && f.s <= a.s1 + 5);
    for (let j = 0; j < 4; j++) {
      const at = track.frameAt(f.s + (j - 1.5) * step / 4);
      block('deck', at, 0, -2.8, [at.w + (broken ? 0 : 2), 4, step / 4 + .2]);
    }
    for (const side of [-1, 1]) {
      const hasRail = [f.s - step / 2, f.s, f.s + step / 2].every((s) => {
        const flags = track.frameAt(s).flags; return flags.includes('rail') || flags.includes(side < 0 ? 'railL' : 'railR');
      });
      if (!hasRail || hall) continue;
      const x = side * (f.w / 2 + 1.35), h = spiral ? 2.3 : /rampart|battlement|crown/.test(name) ? 2.8 : 1.2;
      block('stone', f, x, h / 2 - .25, [1.8, h, step + .15]);
      block('cap', f, x, h - .1, [2.2, .45, step + .15]);
      if (h > 2 && i % 3 === 0) block('stone', f, x, h + .65, [2.1, 1.1, 2.8]);
    }
    // Foundations stop under stacked roads. The spiral is carried by the central tower instead.
    if (!spiral && i % 5 === 0 && f.p[1] > groundY + 8) {
      const otherBelow = frames.some((o) => Math.abs(o.s - f.s) > 36 && o.p[1] < f.p[1] - 5 &&
        Math.hypot(o.p[0] - f.p[0], o.p[2] - f.p[2]) < (o.w + f.w) / 2 + 3);
      if (!otherBelow && clearSpan(f.s, 12)) worldBlock('stone', [f.p[0], (groundY + f.p[1] - 4) / 2, f.p[2]], [f.w - 1, f.p[1] - 4 - groundY, 8], Math.atan2(-f.t[0], -f.t[2]));
    }
  }
  const plan = prisonPlan(track, layout, { phone: art?.tier === 'phone' });
  for (const p of plan.boxes) worldBlock(p.material, p.p, p.size, p.yaw, p.tilt);
  const materials = { stone: keepStoneMaterial('#96789e', art), deck: keepStoneMaterial('#39213f', art),
    cap: keepStoneMaterial('#d1a8bc', art), iron: keepStoneMaterial('#673955', art, false),
    dark: keepStoneMaterial('#090610', art, false), roof: keepStoneMaterial('#3c1939', art), glow: new MeshBasicNodeMaterial({ color: '#ffd198' }) };
  const { stone, cap } = materials;
  owned.push(...Object.values(materials));
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
  for (const t of plan.drums) {
    const body = new Mesh(cylinder, stone); body.name = 'keep-tower-volume';
    body.position.set(t.x, (t.baseY + t.topY) / 2, t.z); body.scale.set(t.radiusM, t.topY - t.baseY, t.radiusM); group.add(body);
  }
  for (const [key, material] of Object.entries(materials)) {
    if (!batches[key].length) continue;
    const mesh = new InstancedMesh(box, material, batches[key].length); mesh.name = 'keep-' + key;
    batches[key].forEach((matrix, i) => mesh.setMatrixAt(i, matrix)); mesh.instanceMatrix.needsUpdate = true;
    mesh.computeBoundingSphere(); group.add(mesh);
  }
  return { group, addPlinth, plan, counts: Object.fromEntries(Object.entries(batches).map(([k, a]) => [k, a.length])),
    dispose() { group.removeFromParent(); group.traverse((o) => { if (o.isInstancedMesh) o.dispose(); }); owned.forEach((o) => o.dispose()); } };
}
