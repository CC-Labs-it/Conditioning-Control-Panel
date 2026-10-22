/* render/cameraMath.js - the chase camera law (plan section 6), three-free so tests run under node.
 *
 * The rig keeps a world yaw and a pitch, follows the kart heading with a time constant and a yaw-rate
 * cap (a wider cap once the gap is large), biases into a drift, freezes yaw while stalled, and follows
 * the kart position with a velocity-predicted horizontal ease and a plain vertical ease (hops do not
 * jerk the view). Horizon stays level: up is world +Y. Vertical fov goes through the lifted viewport law.
 */
import { S } from '../sim/state.js';
import { clamp, damp, angleDiff, qrotate, rad } from '../core/math.js';
import { LOOK } from './look.js';

// ---- viewport fov law, lifted from dtrh/race/viewport.js ----
const DEG = Math.PI / 180;
export const REF_ASPECT = 16 / 9;
export const MAX_VFOV = 102;
export const MIN_VFOV = 28;
/** The vertical fov (degrees) that holds `baseVFovDeg`'s 16:9 width at `aspect`. */
export function vFovForAspect(baseVFovDeg, aspect, maxVFovDeg = MAX_VFOV) {
  const a = Math.max(0.01, +aspect || REF_ASPECT);
  const halfW = Math.tan(clamp(+baseVFovDeg || 1, 1, 175) * 0.5 * DEG) * REF_ASPECT;
  return clamp(2 * Math.atan(halfW / a) / DEG, MIN_VFOV, Math.max(MIN_VFOV, maxVFovDeg));
}

/** Heading yaw of a forward vector: forward = [-sin y, 0, -cos y], so turning right lowers yaw. */
export const yawOf = (f) => Math.atan2(-f[0], -f[2]);

const FWD = [0, 0, -1];

/** @returns {CamRig} { eye, look, up, vFov, yaw, pitch, bias, update(kart, fx, dt, aspect), snap(kart, aspect) } */
export function createCamRig(feel) {
  const rig = { eye: [0, 0, 0], look: [0, 0, -1], up: [0, 1, 0], vFov: 60, yaw: 0, pitch: 0, bias: 0,
    focus: [0, 0, 0], update, snap };
  const q = [0, 0, 0, 1], f = [0, 0, 0];

  function heading(kart) {
    q[0] = kart[S.quatX]; q[1] = kart[S.quatY]; q[2] = kart[S.quatZ]; q[3] = kart[S.quatW];
    qrotate(f, q, FWD);
    return yawOf(f);
  }

  function compose(fx, aspect) {
    const c = (k) => feel.get('camera.' + k);
    // finish orbit: fx.orbit 0..1 of juice.finishOrbitDeg (a partial turn, under the fast yaw cap)
    const y = rig.yaw + (fx ? fx.orbit || 0 : 0) * feel.rad('juice.finishOrbitDeg'), cp = Math.cos(rig.pitch);
    const dx = -Math.sin(y) * cp, dy = Math.sin(rig.pitch), dz = -Math.cos(y) * cp;
    const F = rig.focus, back = c('eyeBackM'), ahead = c('lookAheadM');
    rig.eye[0] = F[0] - dx * back; rig.eye[1] = F[1] - dy * back + c('eyeUpM'); rig.eye[2] = F[2] - dz * back;
    rig.look[0] = F[0] + dx * ahead; rig.look[1] = F[1] + dy * ahead + c('lookUpM'); rig.look[2] = F[2] + dz * ahead;
    if (fx) {
      const push = fx.camPush || 0;
      if (push) {
        const ex = F[0] - rig.eye[0], ey = F[1] - rig.eye[1], ez = F[2] - rig.eye[2];
        const l = Math.hypot(ex, ey, ez) || 1;
        rig.eye[0] += ex / l * push; rig.eye[1] += ey / l * push; rig.eye[2] += ez / l * push;
      }
      const sh = fx.shake;
      if (sh && (sh[0] || sh[1] || sh[2])) {
        // camera-local: x right, y up, z back (toward the eye)
        let bx = rig.eye[0] - rig.look[0], by = rig.eye[1] - rig.look[1], bz = rig.eye[2] - rig.look[2];
        const bl = Math.hypot(bx, by, bz) || 1; bx /= bl; by /= bl; bz /= bl;
        let rx = bz, rz = -bx; const rl = Math.hypot(rx, rz) || 1; rx /= rl; rz /= rl; // up x back
        const ux = by * rz, uy = bz * rx - bx * rz, uz = -by * rx;                      // back x right
        for (let i = 0; i < 2; i++) {
          const o = i ? rig.look : rig.eye;
          o[0] += rx * sh[0] + ux * sh[1] + bx * sh[2];
          o[1] += uy * sh[1] + by * sh[2];
          o[2] += rz * sh[0] + uz * sh[1] + bz * sh[2];
        }
      }
    }
    rig.vFov = vFovForAspect(feel.get('fov.baseVDeg') + (fx ? fx.fovKick || 0 : 0), aspect);
  }

  function update(kart, fx, dt, aspect) {
    const c = (k) => feel.get('camera.' + k);
    const px = kart[S.posX], py = kart[S.posY], pz = kart[S.posZ];
    const F = rig.focus;
    if (Math.hypot(px - F[0], py - F[1], pz - F[2]) > LOOK.snapM) return snap(kart, aspect, fx);
    const speed = Math.abs(kart[S.speedPct]) * feel.get('speed.topMps');
    const lowHop = kart[S.airborne] > 0 && speed < c('lowHopMps');
    const tcH = lowHop ? c('followHopTcS') : c('followHTcS');
    const tcV = lowHop ? c('followHopTcS') : c('followVTcS');

    const dir = kart[S.driftDir];
    let biasTo = 0;
    if (dir) biasTo = -Math.sign(dir) * rad(kart[S.lateral] * dir < 0 ? c('driftBiasOutDeg') : c('driftBiasDeg'));
    rig.bias = damp(rig.bias, biasTo, c('driftBiasEaseS'), dt);

    const ky = heading(kart);
    if (!(kart[S.stalled] > 0)) {
      const gap = angleDiff(rig.yaw, ky + rig.bias);
      const cap = rad(Math.abs(gap) > rad(c('yawFarGapDeg')) ? c('yawCapFarDegps') : c('yawCapDegps')) * dt;
      // heading follow has its own, softer time constant than the position follow: a steering
      // correction turns the kart on screen first, the world swings after it (never exaggerated)
      const tcY = lowHop ? tcH : c('yawFollowTcS');
      rig.yaw += clamp(gap * (1 - Math.exp(-dt / Math.max(1e-4, tcY))), -cap, cap);
    }
    rig.pitch = damp(rig.pitch, Math.asin(clamp(f[1], -1, 1)), tcV, dt);
    F[0] = damp(F[0] + kart[S.velX] * dt, px, tcH, dt);
    F[2] = damp(F[2] + kart[S.velZ] * dt, pz, tcH, dt);
    F[1] = damp(F[1], py, tcV, dt);
    compose(fx, aspect);
    return rig;
  }

  function snap(kart, aspect = REF_ASPECT, fx = null) {
    rig.yaw = heading(kart);
    rig.pitch = Math.asin(clamp(f[1], -1, 1));
    rig.bias = 0;
    rig.focus[0] = kart[S.posX]; rig.focus[1] = kart[S.posY]; rig.focus[2] = kart[S.posZ];
    compose(fx, aspect);
    return rig;
  }
  return rig;
}
