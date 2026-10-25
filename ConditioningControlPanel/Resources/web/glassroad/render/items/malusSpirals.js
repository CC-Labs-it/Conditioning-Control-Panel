/* One real Loom image, shared by eight road-aligned status discs. No runtime downloads. */
import { Mesh, BufferGeometry, BufferAttribute, MeshBasicNodeMaterial, DynamicDrawUsage, Vector3, Quaternion } from 'three/webgpu';
import { uv, uniform, texture, vec2, float, length, smoothstep, sin, cos, attribute } from 'three/tsl';
import { acquireLoomAtlas, loomAtlasUv } from '../loomAtlas.js';
import { createSpiralBeatClock } from '../spiralBeat.js';
import { S } from '../../sim/state.js';
import { statusState, statusStep } from './statusMath.js';
import { createDecalSampler } from '../surfaceDecals.js';
import { makeFrame } from '../../track/frames.js';

export function createMalusSpirals(root, feel, { track = null, waterVolume = null } = {}) {
  const atlas = acquireLoomAtlas(), beat = createSpiralBeatClock();
  const rows = 6, stride = rows + 1, verts = stride * stride, positions = new BufferAttribute(new Float32Array(8 * verts * 3), 3).setUsage(DynamicDrawUsage);
  const alphas = new BufferAttribute(new Float32Array(8 * verts), 1).setUsage(DynamicDrawUsage);
  const styles = new BufferAttribute(new Float32Array(8 * verts), 1).setUsage(DynamicDrawUsage);
  const coords = new Float32Array(8 * verts * 2), indices = [];
  for (let i = 0; i < 8; i++) for (let r = 0; r <= rows; r++) for (let c = 0; c <= rows; c++) {
    const v = i * verts + r * stride + c; coords.set([c / rows, r / rows], v * 2);
    if (r < rows && c < rows) indices.push(v,v+stride,v+1,v+1,v+stride,v+stride+1);
  }
  const alpha = attribute('malusAlpha', 'float'), turn = uniform(0);
  const p = uv().sub(.5), r = length(p).mul(2);
  const at = vec2(p.x.mul(cos(turn)).sub(p.y.mul(sin(turn))), p.x.mul(sin(turn)).add(p.y.mul(cos(turn)))).add(.5);
  const mat = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false });
  mat.colorNode = texture(atlas.texture, loomAtlasUv(at, attribute('malusStyle', 'float'))).rgb;
  // OUT erodes from the feathered perimeter toward the centre, while the whole disc fades.
  mat.opacityNode = float(1).sub(smoothstep(alpha.mul(.65), alpha.mul(.65).add(.35), r)).mul(alpha).mul(.78);
  const geo = new BufferGeometry().setAttribute('position', positions).setAttribute('uv', new BufferAttribute(coords, 2)).setAttribute('malusAlpha', alphas).setAttribute('malusStyle', styles);
  geo.setIndex(indices);
  const mesh = new Mesh(geo, mat); mesh.frustumCulled = false;
  mesh.name = 'malus-loom-under-saucer'; mesh.renderOrder = 2; root.add(mesh);
  const point = new Vector3(), rotation = new Quaternion(), frame = makeFrame(), centre = makeFrame();
  const sample = track ? createDecalSampler(track, waterVolume) : null, states = Array.from({ length: 8 }, statusState);
  let angle = 0;
  const rate = .45, kinds = ['spiral','freeze','brainDrain','blackout','pink','melt'];
  function clear() { for (const s of states) { s.kind = null; s.age = 0; s.outAge = -1; } alphas.array.fill(0); alphas.needsUpdate = true; }
  return { update(karts, dt, view = {}) {
    dt = Math.min(.1, Math.max(0, dt || 0));
    const gentle = !!feel.get('gentle.on');
    const still = typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (!still && !view.paused) angle += dt * rate * (gentle ? .5 : 1) * beat.update(dt, view, gentle);
    turn.value = angle;
    for (let i = 0; i < 8; i++) {
      const r = karts[i], st = r?.st;
      if (r?.fxv?.effect) styles.array.fill(Math.max(0, kinds.indexOf(r.fxv.effect)) % 6, i * verts, (i + 1) * verts);
      const a = Math.min(1, statusStep(states[i], st && !st[S.respawnPhase] ? r.fxv?.effect : null, dt,
        { gentle: true, riseS: Math.max(.3, (feel.get('gentle.riseMs') || 300) / 1000) }));
      alphas.array.fill(st ? a : 0, i * verts, (i + 1) * verts);
      if (!st || a <= 0) continue;
      rotation.set(st[S.quatX], st[S.quatY], st[S.quatZ], st[S.quatW]);
      const fork = track?.forkAtSeg?.(st[S.seg]), s = fork ? fork.localS(st[S.s]) : st[S.s], lateral = st[S.lateral];
      if (sample) sample(s,lateral,centre,fork);
      // Stay attached to the cup during real hops, while each point follows the wet or curved support.
      const hop = sample ? Math.max(0, st[S.posY] - centre.p[1]) : 0;
      for (let row = 0; row <= rows; row++) for (let col = 0; col <= rows; col++) {
        const x = (col / rows - .5) * 5.4, along = (row / rows - .5) * 5.4;
        if (sample) {
          sample(s + along, lateral + x, frame, fork);
          for (let k = 0; k < 3; k++) point.setComponent(k, frame.p[k] + frame.n[k] * .18);
          point.y += hop;
        } else point.set(x,.18,-along).applyQuaternion(rotation).add(new Vector3(st[S.posX],st[S.posY],st[S.posZ]));
        positions.setXYZ(i * verts + row * stride + col,point.x,point.y,point.z);
      }
    }
    alphas.needsUpdate = true; positions.needsUpdate = true; styles.needsUpdate = true;
  }, clear, dispose() { root.remove(mesh); geo.dispose(); mat.dispose(); atlas.dispose(); } };
}
