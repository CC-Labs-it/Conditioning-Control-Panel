/* render/cameraMath.js - the chase camera law (plan section 6), three-free so tests run under node.
 *
 * The rig keeps a world yaw and a pitch, follows the kart heading with a time constant and a yaw-rate
 * cap (a wider cap once the gap is large), biases into a drift, freezes yaw while stalled, and follows
 * the kart position with a velocity-predicted horizontal ease and a plain vertical ease (hops do not
 * jerk the view). Horizon stays level: up is world +Y. Vertical fov goes through the lifted viewport law.
 * Polish 3, the kick layer: a short camera kick on bumps and walls (away from the hit) and landings
 * (a dip by tier), a slight fov punch when a boost starts. Each is one damped spring lobe (no oscillation
 * run, so it is a kick, never a shake); Gentle halves every amplitude and damps critically.
 * Landings and boosts are read from state edges; bumps come from the bus (camera.js) or, without one,
 * from a velocity jump that steering cannot explain.
 */
import { S, BOOST, RESPAWN } from '../sim/state.js';
import { clamp, damp, angleDiff, qrotate, rad } from '../core/math.js';
import { LOOK } from './look.js';

// ---- the kick layer (polish 3) ----

/** Kick numbers; LOOK.camKick (render/look.js, integrator) overrides any of them. */
export const KICK_DEFAULTS = Object.freeze({
  hz: 3.6, zeta: 0.6, gentleMul: 0.5, gentleZeta: 1, maxM: 0.8, maxFovDeg: 9,
  bumpM: 0.45, bumpUpM: 0.16, bumpFovDeg: 2.5, bumpMinMps: 1.5, bumpFullMps: 9,
  landM: Object.freeze([0.16, 0.3, 0.46]), landFovDeg: 1.2, landMinFallMps: 3,
  boostFovDeg: Object.freeze({ drift1: 2.5, drift2: 3.5, drift3: 5, pad: 4, launch: 5.5 }),
  // the fallback bump detector: a horizontal velocity jump past turning and engine can explain
  velBumpMps: 3, velAccelMps2: 30,
});

export const kickNumbers = (L) => ({ ...KICK_DEFAULTS, ...((L && L.camKick) || {}) });

const TAU = Math.PI * 2;
const spring = () => ({ x: 0, v: 0 });
/** One damped spring toward 0 at 120 Hz substeps; returns x. */
function settle(s, dt, hz, zeta) {
  if (!(dt > 0)) return s.x;
  const w = TAU * hz, n = Math.ceil(dt * 120), h = dt / n;
  for (let k = 0; k < n; k++) { s.v += (-w * w * s.x - 2 * zeta * w * s.v) * h; s.x += s.v * h; }
  return s.x;
}

/**
 * The kick layer: hit(kind, mag, side, gentle) adds an impulse; update(dt, gentle) returns the offsets
 * { x (right), y (up), z (back), fov (deg) }. kinds: 'bump' and 'wall' (mag = closing m/s, side = where
 * the hit came from, +1 right), 'land' (mag = tier 1..3), 'boost' (mag = fov degrees).
 * The impulse is sized so a critically damped lobe peaks at the amplitude (v = A * w * e).
 */
export function createKick(L = KICK_DEFAULTS) {
  const sx = spring(), sy = spring(), sz = spring(), sf = spring(), out = { x: 0, y: 0, z: 0, fov: 0 };
  const push = (s, amp) => { s.v += amp * TAU * L.hz * Math.E; };
  function hit(kind, mag = 1, side = 0, gentle = false) {
    const g = gentle ? L.gentleMul : 1;
    if (kind === 'bump' || kind === 'wall') {
      if (!(mag >= L.bumpMinMps)) return false;
      const k = clamp(mag / L.bumpFullMps, 0.25, 1) * g;
      push(sx, -Math.sign(side || 1) * L.bumpM * k); push(sy, L.bumpUpM * k); push(sf, L.bumpFovDeg * k);
      return true;
    }
    if (kind === 'land') {
      const t = clamp(mag | 0, 1, 3);
      push(sy, -L.landM[t - 1] * g); push(sf, L.landFovDeg * (t / 3) * g);
      return true;
    }
    if (kind === 'boost') { push(sf, clamp(mag, 0, L.maxFovDeg) * g); return true; }
    return false;
  }
  function update(dt, gentle = false) {
    const z = gentle ? L.gentleZeta : L.zeta;
    out.x = clamp(settle(sx, dt, L.hz, z), -L.maxM, L.maxM);
    out.y = clamp(settle(sy, dt, L.hz, z), -L.maxM, L.maxM);
    out.z = clamp(settle(sz, dt, L.hz, z), -L.maxM, L.maxM);
    out.fov = clamp(settle(sf, dt, L.hz, z), -L.maxFovDeg, L.maxFovDeg);
    return out;
  }
  function reset() { for (const s of [sx, sy, sz, sf]) { s.x = 0; s.v = 0; } out.x = out.y = out.z = out.fov = 0; }
  return { hit, update, reset, out };
}

/** The fov punch of a boost kind (degrees, 0 for none). */
export function boostPunch(kind, L = KICK_DEFAULTS) {
  const B = L.boostFovDeg;
  return kind === BOOST.DRIFT1 ? B.drift1 : kind === BOOST.DRIFT2 ? B.drift2 : kind === BOOST.DRIFT3 ? B.drift3
    : kind === BOOST.PAD ? B.pad : kind === BOOST.LAUNCH ? B.launch : 0;
}

/**
 * State edges for the kick layer: [[kind, mag, side]] since the last call. w = the watch memory ({} at
 * first). Landings (fall speed past landMinFallMps, tier by fall), boost starts and, when useVel, a
 * horizontal velocity jump that turning (yaw rate x speed) and the engine (velAccelMps2) cannot explain.
 */
export function kickEdges(w, st, dt, L = KICK_DEFAULTS, useVel = true) {
  const out = [], rescue = st[S.respawnPhase] !== RESPAWN.NONE && st[S.respawnPhase] !== RESPAWN.GRACE;
  const air = !rescue && st[S.airborne] > 0.5, bk = st[S.boostKind] | 0, vx = st[S.velX], vz = st[S.velZ];
  if (w.init && !rescue) {
    if (!air && w.air && -w.vyMin >= L.landMinFallMps) out.push(['land', -w.vyMin >= 12 ? 3 : -w.vyMin >= 7 ? 2 : 1, 0]);
    if (bk !== BOOST.NONE && bk !== w.bk) { const p = boostPunch(bk, L); if (p > 0) out.push(['boost', p, 0]); }
    if (useVel && dt > 0 && !air && st[S.grounded] > 0) {
      const dx = vx - w.vx, dz = vz - w.vz, dv = Math.hypot(dx, dz), sp = Math.hypot(vx, vz);
      const can = Math.abs(st[S.yawRate]) * sp * dt + L.velAccelMps2 * dt;
      if (dv - can > L.velBumpMps) {
        // the kart's right (+X) in world, from its quaternion: a jump to the left came from the right
        const q = [st[S.quatX], st[S.quatY], st[S.quatZ], st[S.quatW]], r = qrotate([0, 0, 0], q, [1, 0, 0]);
        out.push(['bump', (dv - can) * 2, -Math.sign(dx * r[0] + dz * r[2]) || 1]);
      }
    }
  }
  w.vyMin = air ? Math.min(w.air ? w.vyMin : 0, st[S.velY]) : 0;
  w.init = true; w.air = air; w.bk = bk; w.vx = vx; w.vz = vz;
  return out;
}

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
    focus: [0, 0, 0], update, snap, hit, kick: null, watch: {}, useVel: true };
  const KL = kickNumbers(LOOK);
  rig.kick = createKick(KL);
  /** A bus kick (camera.js): kind 'bump' | 'wall', mag = closing m/s, side +1 = the hit came from the right. */
  function hit(kind, mag, side) { return rig.kick.hit(kind, mag, side, !!feel.get('gentle.on')); }
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
    // polish 3 kick: the eye takes the whole offset, the look point half (the view dips and leans a little)
    const K = rig.kick.out;
    if (K.x || K.y || K.z) {
      let bx = rig.eye[0] - rig.look[0], by = rig.eye[1] - rig.look[1], bz = rig.eye[2] - rig.look[2];
      const bl = Math.hypot(bx, by, bz) || 1; bx /= bl; by /= bl; bz /= bl;
      let rx = bz, rz = -bx; const rl = Math.hypot(rx, rz) || 1; rx /= rl; rz /= rl;
      const ux = by * rz, uy = bz * rx - bx * rz, uz = -by * rx;
      for (let i = 0; i < 2; i++) {
        const o = i ? rig.look : rig.eye, s = i ? 0.5 : 1;
        o[0] += (rx * K.x + ux * K.y + bx * K.z) * s;
        o[1] += (uy * K.y + by * K.z) * s;
        o[2] += (rz * K.x + uz * K.y + bz * K.z) * s;
      }
    }
    rig.vFov = vFovForAspect(feel.get('fov.baseVDeg') + (fx ? fx.fovKick || 0 : 0) + K.fov, aspect);
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
    const gentle = !!feel.get('gentle.on');
    for (const [kind, mag, side] of kickEdges(rig.watch, kart, dt, KL, rig.useVel)) rig.kick.hit(kind, mag, side, gentle);
    rig.kick.update(dt, gentle);
    compose(fx, aspect);
    return rig;
  }

  function snap(kart, aspect = REF_ASPECT, fx = null) {
    rig.yaw = heading(kart);
    rig.pitch = Math.asin(clamp(f[1], -1, 1));
    rig.bias = 0;
    rig.kick.reset(); rig.watch = {};
    rig.focus[0] = kart[S.posX]; rig.focus[1] = kart[S.posY]; rig.focus[2] = kart[S.posZ];
    compose(fx, aspect);
    return rig;
  }
  return rig;
}
