/* One real Loom image, shared by eight road-aligned status discs. No runtime downloads. */
import { CanvasTexture, SRGBColorSpace, InstancedMesh, PlaneGeometry, MeshBasicNodeMaterial,
  InstancedBufferAttribute, DynamicDrawUsage, Object3D } from 'three/webgpu';
import { uv, uniform, texture, vec2, float, length, smoothstep, sin, cos,
  instancedDynamicBufferAttribute } from 'three/tsl';
import { createPlaceholder, loomReady, releaseLoom, spinOf } from '../mediaPlaceholders.js';
import { S } from '../../sim/state.js';
import { statusState, statusStep } from './statusMath.js';

export function createMalusSpirals(root, feel) {
  if (!loomReady()) return { update() {}, clear() {}, dispose() {} };
  const ph = createPlaceholder(0, 256, [[.95,.30,.65], [.25,.85,.9], [.9,.65,.95], [1,.8,.9]]);
  ph.draw(0); releaseLoom();
  const tex = new CanvasTexture(ph.canvas); tex.colorSpace = SRGBColorSpace;
  const alphas = new InstancedBufferAttribute(new Float32Array(8), 1).setUsage(DynamicDrawUsage);
  const alpha = instancedDynamicBufferAttribute(alphas, 'float'), turn = uniform(0);
  const p = uv().sub(.5), r = length(p).mul(2);
  const at = vec2(p.x.mul(cos(turn)).sub(p.y.mul(sin(turn))), p.x.mul(sin(turn)).add(p.y.mul(cos(turn)))).add(.5);
  const mat = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false });
  mat.colorNode = texture(tex, at).rgb;
  // OUT erodes from the feathered perimeter toward the centre, while the whole disc fades.
  mat.opacityNode = float(1).sub(smoothstep(alpha.mul(.65), alpha.mul(.65).add(.35), r)).mul(alpha).mul(.78);
  const geo = new PlaneGeometry(5.4, 5.4).rotateX(-Math.PI / 2);
  const mesh = new InstancedMesh(geo, mat, 8); mesh.frustumCulled = false;
  mesh.name = 'malus-loom-under-saucer'; mesh.renderOrder = 2; root.add(mesh);
  const pose = new Object3D(), states = Array.from({ length: 8 }, statusState);
  let angle = 0;
  const rate = spinOf(0);
  function clear() { for (const s of states) { s.kind = null; s.age = 0; s.outAge = -1; } alphas.array.fill(0); alphas.needsUpdate = true; }
  return { update(karts, dt) {
    dt = Math.min(.1, Math.max(0, dt || 0));
    const gentle = !!feel.get('gentle.on');
    const still = typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (!still) angle += dt * rate * (gentle ? .5 : 1);
    turn.value = angle;
    for (let i = 0; i < 8; i++) {
      const r = karts[i], st = r?.st;
      const a = Math.min(1, statusStep(states[i], st && !st[S.respawnPhase] ? r.fxv?.effect : null, dt,
        { gentle: true, riseS: Math.max(.3, (feel.get('gentle.riseMs') || 300) / 1000) }));
      alphas.array[i] = st ? a : 0;
      pose.scale.setScalar(st ? 1 : 0);
      if (st) {
        pose.quaternion.set(st[S.quatX], st[S.quatY], st[S.quatZ], st[S.quatW]);
        pose.position.set(st[S.posX], st[S.posY], st[S.posZ]);
        pose.translateY(.06);
      }
      pose.updateMatrix(); mesh.setMatrixAt(i, pose.matrix);
    }
    alphas.needsUpdate = true; mesh.instanceMatrix.needsUpdate = true;
  }, clear, dispose() { root.remove(mesh); geo.dispose(); mat.dispose(); tex.dispose(); } };
}
