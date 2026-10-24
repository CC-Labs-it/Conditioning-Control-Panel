/* Accepted Keep assets, placed before shader warmup. All colour is supplied by the host. */
import { Group, Matrix4, Vector3, Color } from 'three/webgpu';
import { vec3, texture, uv, vec2 } from 'three/tsl';
import { getKeepLayout } from '../track/keep-layout.js';
import { createSoftMaterial } from './materials/colossus.js';

export function applyKeepRoad(road, art) {
  const map = art?.tex('road-bands-mask');
  if (!map) return;
  const seam = texture(map, vec2(uv().x.div(12), uv().y.div(16))).r;
  // Preserve music response and edge markings under the black glass; the seams stay fixed to it.
  road.material.colorNode = road.material.colorNode.mul(0.65)
    .add(vec3(0.8, 0.035, 0.31).mul(seam).mul(road.uniforms.contrast).mul(0.55));
}

export function buildKeepDressing(track, art, pal, samples) {
  const group = new Group(); group.name = 'keep-fortress';
  const materials = new Map(), placed = [], layout = getKeepLayout(track);
  const lod = art?.tier === 'phone' ? 1 : 0;
  const M = new Matrix4(), X = new Vector3(), Y = new Vector3(), Z = new Vector3();
  const tint = (src) => {
    if (materials.has(src)) return materials.get(src);
    const paint = src.clone(), seam = src.name.includes('seam'), glass = src.name.includes('blackglass');
    paint.color.set(seam ? '#ed3e9d' : glass ? '#151522' : '#626372');
    const c = new Color('#df5baa');
    const material = createSoftMaterial(paint, pal, { haze: 0.4, fill: 0.2,
      rim: vec3(c.r, c.g, c.b), rimGain: seam ? 0.15 : 0.35, rimPow: 2.8 }).material;
    paint.dispose(); materials.set(src, material); return material;
  };
  function place(id, s, x, { scale = 1, wide = 1, turn = 0, y = 0 } = {}) {
    const source = art?.model(id + '-lod' + lod)?.scene;
    if (!source) return;
    const f = track.frameAt(s), holder = new Group(), body = source.clone(true);
    M.makeBasis(X.fromArray(f.b), Y.fromArray(f.n), Z.fromArray(f.t).negate());
    holder.quaternion.setFromRotationMatrix(M);
    holder.position.fromArray(f.p).addScaledVector(X, x).addScaledVector(Y, y);
    body.scale.set(scale * wide, scale, scale); body.rotation.y = turn;
    body.traverse((o) => { if (o.isMesh) { o.material = Array.isArray(o.material) ? o.material.map(tint) : tint(o.material); o.castShadow = true; } });
    holder.name = id; holder.add(body); group.add(holder);
    placed.push({ id, s, x, p: holder.position.toArray() });
  }
  // A 28 m opening keeps the columns outside both 6 m bypass lanes. Gate width remains 14 m.
  for (const hall of layout?.halls || []) for (const s of hall.gates) place('keep-portcullis-arch', s, 0, { wide: 2 });
  const heroes = [
    ['forecourt', 0.65, -1, 'padlock'], ['outer-exit', 0.35, 1, 'chainpile'],
    ['east-ascent', 0.55, -1, 'padlock'], ['high-gate', 0.7, 1, 'chainpile'],
    ['north-tower', 0.5, -1, 'padlock'], ['crown-walk', 0.6, 1, 'chainpile'],
    ['west-descent', 0.5, -1, 'padlock'], ['west-tower', 0.3, 1, 'chainpile'],
    ['moat-return', 0.6, -1, 'padlock'], ['south-tower', 0.5, 1, 'chainpile'],
  ];
  for (const [name, share, side, kind] of heroes) {
    const landmark = layout?.landmarks.find((r) => r.id === name);
    if (!landmark) continue;
    const s = landmark.s0 + (landmark.s1 - landmark.s0) * share, f = track.frameAt(s);
    // The whole horizontal bound is checked against every road sample, including other stretches.
    const radius = kind === 'padlock' ? 24 : 39;
    let offset = f.w / 2 + radius + 9, fit = false;
    for (let attempt = 0; attempt < 30; attempt++, offset += 8) {
      const p = f.p.map((v, i) => v + f.b[i] * side * offset);
      if (samples.every((a) => Math.hypot(p[0] - a.p[0], p[2] - a.p[2]) > radius + a.w / 2 + 5)) { fit = true; break; }
    }
    if (fit) place('keep-colossus-' + kind, s, side * offset, { turn: side * 0.22, y: -1 });
  }
  return { group, placed, casters: [group],
    dispose() { group.removeFromParent(); materials.forEach((m) => m.dispose()); } };
}
