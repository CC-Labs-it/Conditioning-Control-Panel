/* render/particles.js - one pooled additive sprite cloud for every kart (B1 juice pass).
 *
 * Sprites draw one of three soft shapes in the shader (0 disc, 1 sparkle with four arms, 2 petal),
 * each with its own turn, gravity share and life. Emitters: the drift spark ladder (pink, white, gold;
 * each tier adds rate, size and petals), the release burst, boost exhaust, slipstream wisps, landing
 * dust, and world bursts at a point (bumps, pickup bubble pops, item boxes). Ring allocation over the
 * tier budget (Q.particles); one draw call. CPU integrates, the GPU reads four attributes.
 */
import { Sprite, SpriteNodeMaterial, AdditiveBlending, InstancedBufferAttribute, DynamicDrawUsage, Color, Vector3 } from 'three/webgpu';
import { instancedDynamicBufferAttribute, uv, smoothstep, vec2, abs, max, select } from 'three/tsl';
import { S, BOOST } from '../sim/state.js';
import { LOOK } from './look.js';
import { Q } from './quality.js';
import { sparkProfile } from './kartPose.js';

export const SHAPE = Object.freeze({ disc: 0, sparkle: 1, petal: 2 });

function shapeNode(kind) {
  const q = uv().sub(0.5).mul(2), r = q.length();
  const disc = smoothstep(0.25, 1, r).oneMinus();
  const arm = (a, b) => smoothstep(0, 0.14, abs(a)).oneMinus().mul(smoothstep(0.15, 1, abs(b)).oneMinus());
  const sparkle = max(smoothstep(0, 0.4, r).oneMinus(), max(arm(q.x, q.y), arm(q.y, q.x)));
  const petal = smoothstep(0.45, 1, vec2(q.x.mul(2.1), q.y.mul(q.y.mul(0.35).add(1))).length()).oneMinus();
  return select(kind.lessThan(0.5), disc, select(kind.lessThan(1.5), sparkle, petal));
}

export function createParticles(feel) {
  const max0 = Q.particles;
  const aPos = new InstancedBufferAttribute(new Float32Array(max0 * 3), 3).setUsage(DynamicDrawUsage);
  const aCol = new InstancedBufferAttribute(new Float32Array(max0 * 4), 4).setUsage(DynamicDrawUsage);
  const aSize = new InstancedBufferAttribute(new Float32Array(max0), 1).setUsage(DynamicDrawUsage);
  const aRot = new InstancedBufferAttribute(new Float32Array(max0 * 2), 2).setUsage(DynamicDrawUsage);   // shape, angle
  const vel = new Float32Array(max0 * 3), life = new Float32Array(max0), maxLife = new Float32Array(max0);
  const size = new Float32Array(max0), spin = new Float32Array(max0), grav = new Float32Array(max0);
  const m = new SpriteNodeMaterial({ transparent: true, depthWrite: false, blending: AdditiveBlending });
  m.positionNode = instancedDynamicBufferAttribute(aPos, 'vec3');
  m.scaleNode = instancedDynamicBufferAttribute(aSize, 'float');
  const rot = instancedDynamicBufferAttribute(aRot, 'vec2');
  m.rotationNode = rot.y;
  const c = instancedDynamicBufferAttribute(aCol, 'vec4');
  m.colorNode = c.rgb;
  m.opacityNode = c.a.mul(shapeNode(rot.x));
  const sprite = new Sprite(m);
  sprite.count = max0; sprite.frustumCulled = false; sprite.renderOrder = 2;

  let head = 0;
  const cols = new Map(), tmp = new Vector3(), dir = new Vector3();
  const cap = () => Math.min(max0, Q.particles);
  const rnd = (a, b) => a + Math.random() * (b - a);
  const rgb = (hex) => { let v = cols.get(hex); if (!v) { const k = new Color(hex); v = [k.r, k.g, k.b]; cols.set(hex, v); } return v; };

  /** World-space emit. */
  function emit(x, y, z, vx, vy, vz, hex, sz, lifeS, shape = 0, g = 1, sp = 0) {
    const i = head; head = (head + 1) % cap();
    const p = aPos.array, cl = aCol.array, k = rgb(hex);
    p[i * 3] = x; p[i * 3 + 1] = y; p[i * 3 + 2] = z;
    vel[i * 3] = vx; vel[i * 3 + 1] = vy; vel[i * 3 + 2] = vz;
    cl[i * 4] = k[0]; cl[i * 4 + 1] = k[1]; cl[i * 4 + 2] = k[2]; cl[i * 4 + 3] = 1;
    aRot.array[i * 2] = shape; aRot.array[i * 2 + 1] = Math.random() * 6.283;
    size[i] = sz; life[i] = maxLife[i] = lifeS; grav[i] = g; spin[i] = sp;
  }
  /** Kart-local emit: position through obj.matrixWorld, velocity through its rotation. */
  function local(obj, lx, ly, lz, vx, vy, vz, hex, sz, lifeS, shape, g, sp) {
    tmp.set(lx, ly, lz).applyMatrix4(obj.matrixWorld);
    dir.set(vx, vy, vz).applyQuaternion(obj.quaternion);
    emit(tmp.x, tmp.y, tmp.z, dir.x, dir.y, dir.z, hex, sz, lifeS, shape, g, sp);
  }

  function sparks(obj, n, hex, sz, lifeS, spread, petal) {
    const r = LOOK.saucerR, [lo, hi] = LOOK.sparkSpeedMps;
    for (let k = 0; k < n; k++) {
      const side = k & 1 ? 1 : -1, pick = Math.random();
      const shape = pick < petal ? SHAPE.petal : (k % 3 === 0 ? SHAPE.disc : SHAPE.sparkle);
      const h = pick > 0.8 ? '#ffffff' : hex;
      local(obj, side * r * 0.7, 0.05, r * 0.7, side * rnd(0, spread), rnd(lo, hi) * 0.6, rnd(lo, hi), h,
        sz * rnd(0.7, 1.3), lifeS * (shape === SHAPE.petal ? 2 : 1), shape, shape === SHAPE.petal ? 0.25 : 1, rnd(-1, 1) * LOOK.sparkSpin);
    }
  }

  /**
   * Per kart and frame. mem = the kart's own carry memory ({} at first), opts.mul scales rates (rivals,
   * far karts), opts.slip = fxv.slip (0..1 wake charge). Reads drift stage and boost kind off the state,
   * so rivals get the same ladder as the player.
   */
  function kart(obj, st, mem, dt, { mul = 1, slip = 0 } = {}) {
    const colours = feel.get('juice.sparkColours'), dieS = feel.get('juice.sparkDieS');
    const drifting = st[S.driftDir] !== 0, stage = st[S.driftStage] | 0;
    if (drifting && stage > 0) {
      const pr = sparkProfile(stage, LOOK, mul);
      mem.carry = (mem.carry || 0) + pr.rate * dt;
      const n = Math.floor(mem.carry); mem.carry -= n;
      sparks(obj, n, colours[stage - 1], pr.size, dieS, LOOK.sparkSpreadMps, pr.petal);
      mem.tier = stage;
    } else mem.carry = 0;
    const bk = st[S.boostKind];
    if (!drifting && mem.tier > 0) {
      if (bk >= BOOST.DRIFT1 && bk <= BOOST.DRIFT3) {
        const pr = sparkProfile(mem.tier, LOOK);
        sparks(obj, Math.round(LOOK.burstCount * mul), colours[mem.tier - 1], pr.size * 1.2, dieS * 1.4, LOOK.burstSpreadMps, LOOK.burstPetals);
      }
      mem.tier = 0;
    }
    if (bk !== BOOST.NONE) {
      mem.carryX = (mem.carryX || 0) + LOOK.exhaustRate * mul * dt;
      const n = Math.floor(mem.carryX); mem.carryX -= n;
      const hex = bk >= BOOST.DRIFT1 && bk <= BOOST.DRIFT3 ? colours[bk - 1] : bk === BOOST.LAUNCH ? LOOK.launchTrail : LOOK.exhaustColour;
      for (let k = 0; k < n; k++) {
        local(obj, rnd(-0.2, 0.2), 0.3, LOOK.saucerR, rnd(-0.5, 0.5), rnd(0, 0.5), rnd(4, 8), k & 1 ? hex : LOOK.exhaustColour,
          LOOK.exhaustSizeM * rnd(0.6, 1.1), LOOK.exhaustLifeS, k % 4 === 0 ? SHAPE.sparkle : SHAPE.disc, 0.2, 0);
      }
    } else mem.carryX = 0;
    if (slip > 0.05) {
      mem.carryW = (mem.carryW || 0) + LOOK.wispRate * slip * mul * dt;
      const n = Math.floor(mem.carryW); mem.carryW -= n;
      for (let k = 0; k < n; k++) {
        const side = k & 1 ? 1 : -1;
        local(obj, side * rnd(0.5, 1.1), rnd(0.2, 1), -rnd(0.4, 1.2), side * rnd(0, 0.6), rnd(-0.2, 0.3), LOOK.wispSpeedMps * rnd(0.8, 1.2),
          LOOK.wispColour, LOOK.wispSizeM * rnd(0.6, 1.2), LOOK.wispLifeS, SHAPE.disc, 0, 0);
      }
    } else mem.carryW = 0;
  }

  /** Landing dust: a flat ring of soft discs around the saucer, tier 1..3. */
  function dust(obj, tier) {
    const n = Math.round(LOOK.dustCount * tier / 3);
    for (let k = 0; k < n; k++) {
      const a = (k / n) * Math.PI * 2, v = LOOK.dustSpeedMps;
      local(obj, Math.cos(a) * LOOK.saucerR, 0.02, Math.sin(a) * LOOK.saucerR, Math.cos(a) * v, rnd(0.2, 1), Math.sin(a) * v,
        LOOK.dustColour, LOOK.dustSizeM * rnd(0.45, 0.8), LOOK.dustLifeS, k % 3 ? SHAPE.disc : SHAPE.petal, 0.3, 0);
    }
  }

  /** A burst at a world point p ([x, y, z]): n sprites, radial, colours cycled. */
  function burst(p, n, { colours, speed = 4, sizeM = 0.14, lifeS = 0.5, petal = 0, sparkle = 0.5, up = 1, g = 0.6 }) {
    if (!p) return;
    for (let k = 0; k < n; k++) {
      const a = Math.random() * 6.283, e = rnd(-0.3, 1), v = speed * rnd(0.5, 1), pick = Math.random();
      const shape = pick < petal ? SHAPE.petal : pick < petal + sparkle ? SHAPE.sparkle : SHAPE.disc;
      emit(p[0], p[1] + 0.3, p[2], Math.cos(a) * v * Math.cos(e), (Math.sin(e) * v + up) * 0.8, Math.sin(a) * v * Math.cos(e),
        colours[k % colours.length], sizeM * rnd(0.6, 1.3), lifeS * rnd(0.7, 1.2), shape, shape === SHAPE.petal ? 0.2 : g, rnd(-1, 1) * LOOK.sparkSpin);
    }
  }

  function update(dt) {
    const n = cap(), drag = Math.exp(-LOOK.particleDrag * dt), g0 = LOOK.particleGravityMps2 * dt;
    const p = aPos.array, cl = aCol.array, sz = aSize.array, rt = aRot.array;
    for (let i = 0; i < n; i++) {
      if (life[i] <= 0) { sz[i] = 0; continue; }
      life[i] -= dt;
      const j = i * 3;
      vel[j] *= drag; vel[j + 1] = vel[j + 1] * drag - g0 * grav[i]; vel[j + 2] *= drag;
      p[j] += vel[j] * dt; p[j + 1] += vel[j + 1] * dt; p[j + 2] += vel[j + 2] * dt;
      rt[i * 2 + 1] += spin[i] * dt;
      const t = Math.max(0, life[i] / maxLife[i]);
      cl[i * 4 + 3] = t < 0.85 ? t / 0.85 : 1;          // no pop-in: full at birth, eased out
      sz[i] = size[i] * (0.5 + 0.5 * t);
    }
    sprite.count = n;
    aPos.needsUpdate = true; aCol.needsUpdate = true; aSize.needsUpdate = true; aRot.needsUpdate = true;
  }
  return { object: sprite, kart, dust, burst, update, dispose() { m.dispose(); } };
}
