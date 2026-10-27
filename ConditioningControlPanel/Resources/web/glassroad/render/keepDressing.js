/* Accepted Keep assets, placed before shader warmup. All colour is supplied by the host. */
import { Group, Matrix4, Vector3, Color } from 'three/webgpu';
import { vec3, texture, uv, vec2, mix, step, attribute, float, smoothstep, normalWorld, positionWorld, cameraPosition, normalize, dot, sin, cos, fwidth, fract, abs, min, max } from 'three/tsl';
import { getKeepLayout } from '../track/keep-layout.js';
import { createSoftMaterial } from './materials/colossus.js';
import { buildKeepFortress, keepStoneMaterial } from './keepFortress.js';
import { keepMusicFloor } from './materials/keepMusicFloor.js';

/** Musical mortar follows the real 2/4/4/2 m slabs, independent of the artwork's broad grey relief. */
export function keepMusicSeam(p) {
  const x = fract(p.x.div(12)).mul(12), y = fract(p.y.div(8).add(step(6, x).mul(.5)));
  const dx = min(min(x, float(12).sub(x)), min(abs(x.sub(2)), min(abs(x.sub(6)), abs(x.sub(10)))));
  const dy = min(y, float(1).sub(y)).mul(8), distance = min(dx, dy);
  const width = max(fwidth(distance).mul(1.2), .07);
  return float(1).sub(smoothstep(width.mul(.4), width, distance)).mul(float(.07).div(width));
}

export function applyKeepRoad(road, art) {
  const map = art?.tex('road-bands-mask');
  const seam = map ? texture(map, vec2(uv().x.div(12), uv().y.div(16))).r : float(0);
  const stone = keepStoneMaterial('#664b69', art, false, false), original = road.material.colorNode;
  // Keep the opaque slate and fixed joints, with song-lit fills on the final visible surface.
  const cut = smoothstep(.12, .5, seam), bevel = smoothstep(.06, .22, seam).sub(smoothstep(.4, .7, seam));
  // Broad slate lamination stays on the dry stone, below the existing music and navigation marks.
  const slatePhase = uv().x.mul(2.1).add(sin(uv().y.mul(.37)).mul(.6));
  const slateDetail = float(1).sub(smoothstep(.5, 1.6, fwidth(slatePhase)));
  const slate = sin(slatePhase).mul(.055).mul(slateDetail).add(1);
  const floor = mix(stone.colorNode.mul(slate), vec3(.006, .002, .009), cut.mul(.78)).add(original.mul(.02))
    .add(vec3(.8, .035, .31).mul(bevel).mul(road.uniforms.contrast).mul(.17));
  const litFloor = keepMusicFloor({ p: uv(), base: floor, seam: keepMusicSeam(uv()), reactive: road.uniforms.reactive, music: road.uniforms.music });
  road.material.colorNode = mix(litFloor, original, step(2.5, attribute('surface', 'float')));
  road.material.color.copy(stone.color); road.material.transparent = false; road.material.opacity = 1;
  stone.dispose();
}

export function buildKeepDressing(track, art, pal, samples, { reactive = null, music = null } = {}) {
  const group = new Group(); group.name = 'keep-fortress';
  const materials = new Map(), placed = [], layout = getKeepLayout(track);
  const fortress = buildKeepFortress(track, layout, art), artGroup = new Group();
  group.add(fortress.group, artGroup);
  const lod = art?.tier === 'phone' ? 1 : 0;
  const M = new Matrix4(), X = new Vector3(), Y = new Vector3(), Z = new Vector3();
  const tint = (src) => {
    if (materials.has(src)) return materials.get(src);
    const paint = src.clone(), seam = src.name.includes('seam'), glass = src.name.includes('blackglass');
    paint.color.set(seam ? '#ff3b9e' : glass ? '#090610' : '#a486ad');
    const c = new Color('#df5baa');
    const material = createSoftMaterial(paint, pal, { haze: 0.18, fill: 0.18,
      rim: vec3(c.r, c.g, c.b), rimGain: seam ? 0.15 : 0.35, rimPow: 2.8 }).material;
    if (!seam) {
      const edge = float(1).sub(dot(normalize(normalWorld), normalize(cameraPosition.sub(positionWorld))).abs());
      material.colorNode = mix(material.colorNode, vec3(.002, .001, .004), smoothstep(.965, .996, edge));
    }
    paint.dispose(); materials.set(src, material); return material;
  };
  const archMaterials = new Map();
  function archFinish(src) {
    if (archMaterials.has(src)) return archMaterials.get(src);
    const seam = /seam|glow/.test(src.name), iron = /iron|metal|blackglass/.test(src.name);
    // The accepted arch GLBs have no UVs; grain and mortar use their geometry/world positions.
    const material = keepStoneMaterial(seam ? '#ca8652' : iron ? '#673955' : '#a486ad', art, !seam && !iron, false, iron ? 'iron' : 'stone');
    material.name = 'keep-arch-' + src.name;
    if (music && reactive) {
      const parade = cos(positionWorld.y.mul(.24).sub(music.tileBeat.div(4).mul(Math.PI * 2))).mul(.5).add(.5);
      const energy = reactive.energy.mul(3).clamp(0, 1).mul(mix(float(1), float(.25), reactive.gentle));
      // Warm light catches the dressed stone and forged seams; motion uses the same song as the floor.
      material.colorNode = material.colorNode.add(vec3(.8, .29, .08).mul(parade).mul(energy).mul(seam ? .65 : .14));
    }
    archMaterials.set(src, material); return material;
  }
  function place(id, s, x, { scale = 1, wide = 1, turn = 0, y = 0 } = {}) {
    const source = art?.model(id + '-lod' + lod)?.scene;
    if (!source) return;
    const f = track.frameAt(s), holder = new Group(), body = source.clone(true);
    M.makeBasis(X.fromArray(f.b), Y.fromArray(f.n), Z.fromArray(f.t).negate());
    holder.quaternion.setFromRotationMatrix(M);
    holder.position.fromArray(f.p).addScaledVector(X, x).addScaledVector(Y, y);
    if (id.includes('colossus')) holder.quaternion.setFromAxisAngle(new Vector3(0, 1, 0), Math.atan2(-f.t[0], -f.t[2]));
    body.scale.set(scale * wide, scale, scale); body.rotation.y = turn;
    const finish = id === 'keep-portcullis-arch' ? archFinish : tint;
    body.traverse((o) => { if (o.isMesh) { o.material = Array.isArray(o.material) ? o.material.map(finish) : finish(o.material); o.castShadow = true; } });
    holder.name = id; holder.add(body); artGroup.add(holder);
    placed.push({ id, s, x, p: holder.position.toArray() });
    return holder;
  }
  // The opening follows the whole road width, leaving every bypass lane clear.
  for (const hall of layout?.halls || []) for (const s of hall.gates)
    place('keep-portcullis-arch', s, 0, { wide: (track.frameAt(s).w + 2) / 14 });
  for (const chain of layout?.chains || []) place('keep-portcullis-arch', chain.s, 0, { wide: (track.frameAt(chain.s).w + 2) / 14 });
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
      const towersClear = (layout.towers || []).every((t) => Math.hypot(p[0] - t.x, p[2] - t.z) > radius + t.radiusM + 3);
      const buildingsClear = fortress.plan.buildings.every((a) => {
        const dx = p[0] - a.x, dz = p[2] - a.z, yaw = a.yaw || 0;
        const x = dx * Math.cos(yaw) - dz * Math.sin(yaw), z = dx * Math.sin(yaw) + dz * Math.cos(yaw);
        return Math.hypot(Math.max(0, Math.abs(x) - a.w / 2), Math.max(0, Math.abs(z) - a.d / 2)) > radius + 6;
      });
      if (towersClear && buildingsClear && samples.every((a) => Math.hypot(p[0] - a.p[0], p[2] - a.p[2]) > radius + a.w / 2 + 5)) { fit = true; break; }
    }
    if (fit) {
      const holder = place('keep-colossus-' + kind, s, side * offset, { turn: side * 0.22, y: -1 });
      if (holder) fortress.addPlinth(holder.position.toArray(), radius * 0.84);
    }
  }
  return { group, placed, fortress, casters: [artGroup],
    dispose() { group.removeFromParent(); fortress.dispose(); materials.forEach((m) => m.dispose()); archMaterials.forEach((m) => m.dispose()); } };
}
