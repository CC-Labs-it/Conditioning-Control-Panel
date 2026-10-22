/* render/particles.js - one pooled additive sprite cloud: drift sparks (tier colours from
 * feel juice.sparkColours), the release burst, landing dust and boost exhaust. Ring allocation over
 * the tier budget (Q.particles); one draw call. CPU integrates, the GPU only reads three attributes.
 */
import { Sprite, SpriteNodeMaterial, AdditiveBlending, InstancedBufferAttribute, DynamicDrawUsage, Color, Vector3 } from 'three/webgpu';
import { instancedDynamicBufferAttribute, uv, smoothstep } from 'three/tsl';
import { S, BOOST } from '../sim/state.js';
import { LOOK } from './look.js';
import { Q } from './quality.js';

export function createParticles(feel) {
  const max = Q.particles;
  const aPos = new InstancedBufferAttribute(new Float32Array(max * 3), 3).setUsage(DynamicDrawUsage);
  const aCol = new InstancedBufferAttribute(new Float32Array(max * 4), 4).setUsage(DynamicDrawUsage);
  const aSize = new InstancedBufferAttribute(new Float32Array(max), 1).setUsage(DynamicDrawUsage);
  const vel = new Float32Array(max * 3), life = new Float32Array(max), maxLife = new Float32Array(max), size = new Float32Array(max);
  const m = new SpriteNodeMaterial({ transparent: true, depthWrite: false, blending: AdditiveBlending });
  m.positionNode = instancedDynamicBufferAttribute(aPos, 'vec3');
  m.scaleNode = instancedDynamicBufferAttribute(aSize, 'float');
  const c = instancedDynamicBufferAttribute(aCol, 'vec4');
  m.colorNode = c.rgb;
  m.opacityNode = c.a.mul(smoothstep(0.15, 0.5, uv().sub(0.5).length()).oneMinus());
  const sprite = new Sprite(m);
  sprite.count = max; sprite.frustumCulled = false; sprite.renderOrder = 2;

  let head = 0, carry = 0, carryX = 0, lastTier = 1, prevBurst = 0, prevDust = 0;
  const col = new Color(), tmp = new Vector3(), dir = new Vector3();
  const cap = () => Math.min(max, Q.particles);
  const rnd = (a, b) => a + Math.random() * (b - a);

  function emit(obj, lx, ly, lz, vx, vy, vz, hex, sz, lifeS) {
    const i = head; head = (head + 1) % cap();
    tmp.set(lx, ly, lz).applyMatrix4(obj.matrixWorld);
    dir.set(vx, vy, vz).applyQuaternion(obj.quaternion);
    const p = aPos.array, cl = aCol.array; p[i * 3] = tmp.x; p[i * 3 + 1] = tmp.y; p[i * 3 + 2] = tmp.z;
    vel[i * 3] = dir.x; vel[i * 3 + 1] = dir.y; vel[i * 3 + 2] = dir.z;
    col.set(hex); cl[i * 4] = col.r; cl[i * 4 + 1] = col.g; cl[i * 4 + 2] = col.b; cl[i * 4 + 3] = 1;
    size[i] = sz; life[i] = maxLife[i] = lifeS;
  }

  function sparks(obj, n, hex, lifeS, spread) {
    const r = LOOK.saucerR, [lo, hi] = LOOK.sparkSpeedMps;
    for (let k = 0; k < n; k++) {
      const side = k & 1 ? 1 : -1;
      emit(obj, side * r * 0.7, 0.05, r * 0.7, side * rnd(0, spread), rnd(lo, hi) * 0.6, rnd(lo, hi), hex, LOOK.sparkSizeM, lifeS);
    }
  }

  /** Per frame. kart = interpolated state, fx = FxState, obj = the kart Object3D (matrixWorld current). */
  function update(dt, kart, fx, obj) {
    obj.updateMatrixWorld();
    const colours = feel.get('juice.sparkColours'), dieS = feel.get('juice.sparkDieS');
    if (fx.sparkTier > 0) {
      lastTier = fx.sparkTier;
      carry += LOOK.sparkRate * dt;
      const n = Math.floor(carry); carry -= n;
      sparks(obj, n, colours[lastTier - 1], dieS, LOOK.sparkSpreadMps);
    } else carry = 0;
    if (fx.sparkBurst > 0 && prevBurst <= 0) sparks(obj, LOOK.burstCount, colours[Math.max(0, lastTier - 1)], dieS, LOOK.burstSpreadMps);
    prevBurst = fx.sparkBurst;
    if (fx.dust > prevDust + 0.05) {
      const n = Math.round(LOOK.dustCount * fx.dust);
      for (let k = 0; k < n; k++) {
        const a = (k / n) * Math.PI * 2, v = LOOK.dustSpeedMps;
        emit(obj, Math.cos(a) * LOOK.saucerR, 0.02, Math.sin(a) * LOOK.saucerR, Math.cos(a) * v, rnd(0.2, 1), Math.sin(a) * v,
          LOOK.dustColour, LOOK.dustSizeM, LOOK.dustLifeS);
      }
    }
    prevDust = fx.dust;
    if (kart[S.boostKind] !== BOOST.NONE) {
      carryX += LOOK.exhaustRate * dt;
      const n = Math.floor(carryX); carryX -= n;
      for (let k = 0; k < n; k++) {
        emit(obj, rnd(-0.2, 0.2), 0.3, LOOK.saucerR, rnd(-0.5, 0.5), rnd(0, 0.5), rnd(4, 8), LOOK.exhaustColour, LOOK.exhaustSizeM, LOOK.exhaustLifeS);
      }
    } else carryX = 0;

    const n = cap(), drag = Math.exp(-LOOK.particleDrag * dt), g = LOOK.particleGravityMps2 * dt;
    const p = aPos.array, cl = aCol.array, sz = aSize.array;
    for (let i = 0; i < n; i++) {
      if (life[i] <= 0) { sz[i] = 0; continue; }
      life[i] -= dt;
      const j = i * 3;
      vel[j] *= drag; vel[j + 1] = vel[j + 1] * drag - g; vel[j + 2] *= drag;
      p[j] += vel[j] * dt; p[j + 1] += vel[j + 1] * dt; p[j + 2] += vel[j + 2] * dt;
      const t = Math.max(0, life[i] / maxLife[i]);
      cl[i * 4 + 3] = t; sz[i] = size[i] * (0.5 + 0.5 * t);
    }
    sprite.count = n;
    aPos.needsUpdate = true; aCol.needsUpdate = true; aSize.needsUpdate = true;
  }
  return { object: sprite, update, dispose() { m.dispose(); } };
}
