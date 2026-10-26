/* render/cameraMath.js - the chase camera law (plan section 6), three-free so tests run under node.
 *
 * The rig keeps a world yaw and a pitch, follows the kart heading with a time constant and a yaw-rate
 * cap (a wider cap once the gap is large), biases into a drift, freezes yaw while stalled, and follows
 * the kart position with velocity prediction on the road and a plain vertical ease in the air (hops do
 * not jerk the view). Horizon stays level: up is world +Y. Vertical fov goes through the lifted viewport law.
 * Polish 3, the kick layer: a short camera kick on bumps and walls (away from the hit) and landings
 * (a dip by tier). Legacy FOV spring output is isolated from the camera. Each is one damped spring lobe (no oscillation
 * run, so it is a kick, never a shake); Gentle halves every amplitude and damps critically.
 * Landings and boosts are read from state edges; bumps come from the bus (camera.js) or, without one,
 * from a velocity jump that steering cannot explain.
 * Polish 6, the loop: the heading is the kart's TRAVEL direction (forward folded through its up, so it holds
 * still all the way round a vertical loop) and pitch is capped. On a magnet stretch (or whenever the kart's up
 * leans far from world up) the rig blends into ROAD mode: eye behind and above the kart in its own smoothed
 * frame, camera up = the kart's smoothed up. The blend weight eases in and out, so the horizon follows the road
 * up through the loop with no roll snap, and stays level everywhere else.
 * Screen framing: the eye keeps that smooth orbit, then its aim holds the actual racer centre below the
 * middle of the viewport. A small eased airborne excursion preserves the hop. Contact kicks follow it;
 * effect shake, push and boost zoom never enter the camera pose.
 */
import { S, BOOST, RESPAWN } from '../sim/state.js';
import { clamp, damp, angleDiff, qrotate, rad, smoothstep } from '../core/math.js';
import { LOOK } from './look.js';
import { RACER_FRAMING } from './framing.js';
import { createLookAround } from './lookAround.js';
import { phraseCameraGuide } from '../core/phraseFlight.js';

// ---- the kick layer (polish 3) ----

/** Kick numbers; LOOK.camKick (render/look.js, integrator) overrides any of them. */
export const KICK_DEFAULTS = Object.freeze({
  hz: 3.6, zeta: 0.6, gentleMul: 0.5, gentleZeta: 1, maxM: 0.8, maxFovDeg: 9,
  bumpM: 0.45, bumpFovDeg: 2.5, bumpMinMps: 1.5, bumpFullMps: 9,
  landM: Object.freeze([0.16, 0.3, 0.46]), landFovDeg: 1.2, landMinFallMps: 3,
  // polish 5 (J1): the held boost kick is the ladder (fov.kickByTierDeg via fx/cues.js boostLadder); this is
  // only the snap on its IN, small so the peak stays near the ladder (2.5 / 3.5 / 5 / 4 / 5.5 before)
  boostFovDeg: Object.freeze({ drift1: 0.7, drift2: 1.5, drift3: 3, pad: 1.6, launch: 2 }),
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
      // No minimum-strength jump: a barely audible graze must barely move the camera.
      const k = clamp((mag - L.bumpMinMps) / (L.bumpFullMps - L.bumpMinMps), 0, 1) * g;
      // Road-plane impacts push sideways. A vertical kick falsely implies raised road or a hop.
      push(sx, -Math.sign(side || 1) * L.bumpM * k); push(sf, L.bumpFovDeg * k);
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

const FWD = [0, 0, -1], KUP = [0, 1, 0];

/** Loop camera numbers (polish 6). inTcS/outTcS: road-mode blend ease; frameTcS: the kart frame smoothing;
 * upFull..upNone: kart up.y range over which road mode takes over by itself; pitchMaxDeg caps the level pitch. */
export const LOOP_CAM = Object.freeze({ inTcS: 0.15, outTcS: 0.4, frameTcS: 0.06, upFull: 0.45, upNone: 0.8, pitchMaxDeg: 40 });

/**
 * The travel direction of a kart with forward f and up u (unit, perpendicular): f u.y - u f.y, normalised into out.
 * Flat it is f levelled; on a loop it stays the loop's horizontal heading all the way round. Falls back to f
 * levelled when the kart drives along a vertical wall. @returns {number[]} out
 */
export function travelDir(out, f, u) {
  let x = f[0] * u[1] - u[0] * f[1], z = f[2] * u[1] - u[2] * f[1];
  let l = Math.hypot(x, z);
  if (l < 0.2) { x = f[0]; z = f[2]; l = Math.hypot(x, z) || 1; }
  out[0] = x / l; out[1] = 0; out[2] = z / l;
  return out;
}

/** Road-mode target 0..1: 1 on a magnet stretch, else by how far the kart's up leans from world up. */
export function loopTarget(upY, magnet, L = LOOP_CAM) {
  return magnet ? 1 : smoothstep(L.upNone, L.upFull, upY);
}

/**
 * Aim from an already composed eye so anchor projects to x=0.5 and screenY. The eye and road-up do not
 * change. Rotating the centre ray upward by atan((2*y-1)*tan(vFov/2)) gives the requested vertical offset
 * under perspective, including phone and boost FOV. `out` is the old look point and keeps its range.
 */
export function aimAtAnchor(out, eye, up, anchor, vFov, screenY = RACER_FRAMING.screenY) {
  const ax = anchor[0] - eye[0], ay = anchor[1] - eye[1], az = anchor[2] - eye[2];
  const distance = Math.hypot(ax, ay, az);
  if (distance < 1e-6) return out;
  const dx = ax / distance, dy = ay / distance, dz = az / distance;
  let rx = dy * up[2] - dz * up[1], ry = dz * up[0] - dx * up[2], rz = dx * up[1] - dy * up[0];
  const side = Math.hypot(rx, ry, rz);
  if (side < 1e-6) return out; // a degenerate external pose keeps the previous finite aim
  rx /= side; ry /= side; rz /= side;
  const ux = ry * dz - rz * dy, uy = rz * dx - rx * dz, uz = rx * dy - ry * dx;
  const beta = Math.atan((2 * screenY - 1) * Math.tan(vFov * DEG / 2)), c = Math.cos(beta), s = Math.sin(beta);
  const reach = Math.max(1, Math.hypot(out[0] - eye[0], out[1] - eye[1], out[2] - eye[2]));
  out[0] = eye[0] + (dx * c + ux * s) * reach;
  out[1] = eye[1] + (dy * c + uy * s) * reach;
  out[2] = eye[2] + (dz * c + uz * s) * reach;
  return out;
}

/** @returns {CamRig} { eye, look, up, vFov, yaw, pitch, bias, update(kart, fx, dt, aspect), snap(kart, aspect) } */
export function createCamRig(feel) {
  const glance = createLookAround();
  const rig = { eye: [0, 0, 0], look: [0, 0, -1], up: [0, 1, 0], vFov: 60, yaw: 0, pitch: 0, bias: 0,
    glance,
    focus: [0, 0, 0], update, snap, hit, kick: null, watch: {}, useVel: true,
    loop: 0, rf: [0, 0, -1], ru: [0, 1, 0], anchor: [0, 0, 0], airScreen: 0 };
  const KL = kickNumbers(LOOK);
  rig.kick = createKick(KL);
  /** A bus kick (camera.js): kind 'bump' | 'wall', mag = closing m/s, side +1 = the hit came from the right. */
  function hit(kind, mag, side) { return rig.kick.hit(kind, mag, side, !!feel.get('gentle.on')); }
  const q = [0, 0, 0, 1], f = [0, 0, 0], u = [0, 1, 0], d = [0, 0, -1], lastPosition = [0, 0, 0];
  const guidePoint = {}; let flightGuide = null;

  function heading(kart) {
    q[0] = kart[S.quatX]; q[1] = kart[S.quatY]; q[2] = kart[S.quatZ]; q[3] = kart[S.quatW];
    qrotate(f, q, FWD); qrotate(u, q, KUP);
    return yawOf(travelDir(d, f, u));
  }
  const pitchTo = () => clamp(Math.asin(clamp(f[1], -1, 1)), -rad(LOOP_CAM.pitchMaxDeg), rad(LOOP_CAM.pitchMaxDeg));
  const magnetAt = (kart, track) => {
    if (!track || typeof track.nodeFlags !== 'function') return false;
    const fl = track.nodeFlags(kart[S.seg] | 0);
    return !!fl && fl.indexOf('magnet') >= 0;
  };
  /** Ease the smoothed kart frame (rf forward, ru up) toward f, u; k = 1 snaps. */
  function easeFrame(k) {
    const rf = rig.rf, ru = rig.ru;
    for (let i = 0; i < 3; i++) { ru[i] += (u[i] - ru[i]) * k; rf[i] += (f[i] - rf[i]) * k; }
    let l = Math.hypot(ru[0], ru[1], ru[2]) || 1;
    ru[0] /= l; ru[1] /= l; ru[2] /= l;
    const dd = rf[0] * ru[0] + rf[1] * ru[1] + rf[2] * ru[2];
    rf[0] -= ru[0] * dd; rf[1] -= ru[1] * dd; rf[2] -= ru[2] * dd;
    l = Math.hypot(rf[0], rf[1], rf[2]) || 1;
    rf[0] /= l; rf[1] /= l; rf[2] /= l;
  }
  const B = [0, 0, 0], R = [0, 0, 0], UU = [0, 0, 0];
  /** Camera-local basis from eye, look and rig.up: R right, UU up, B back (toward the eye). */
  function basis() {
    B[0] = rig.eye[0] - rig.look[0]; B[1] = rig.eye[1] - rig.look[1]; B[2] = rig.eye[2] - rig.look[2];
    let l = Math.hypot(B[0], B[1], B[2]) || 1; B[0] /= l; B[1] /= l; B[2] /= l;
    const up = rig.up;
    R[0] = up[1] * B[2] - up[2] * B[1]; R[1] = up[2] * B[0] - up[0] * B[2]; R[2] = up[0] * B[1] - up[1] * B[0];   // up x back
    l = Math.hypot(R[0], R[1], R[2]) || 1; R[0] /= l; R[1] /= l; R[2] /= l;
    UU[0] = B[1] * R[2] - B[2] * R[1]; UU[1] = B[2] * R[0] - B[0] * R[2]; UU[2] = B[0] * R[1] - B[1] * R[0];       // back x right
  }

  function compose(kart, fx, aspect, dt = 0) {
    const c = (k) => feel.get('camera.' + k);
    // finish orbit: fx.orbit 0..1 of juice.finishOrbitDeg (a partial turn, under the fast yaw cap)
    const y = rig.yaw + (fx ? fx.orbit || 0 : 0) * feel.rad('juice.finishOrbitDeg'), cp = Math.cos(rig.pitch);
    const dx = -Math.sin(y) * cp, dy = Math.sin(rig.pitch), dz = -Math.cos(y) * cp;
    const F = rig.focus, back = c('eyeBackM'), ahead = c('lookAheadM');
    rig.eye[0] = F[0] - dx * back; rig.eye[1] = F[1] - dy * back + c('eyeUpM'); rig.eye[2] = F[2] - dz * back;
    rig.look[0] = F[0] + dx * ahead; rig.look[1] = F[1] + dy * ahead + c('lookUpM'); rig.look[2] = F[2] + dz * ahead;
    // polish 6 ROAD mode (the loop): the same eye and look in the kart's smoothed frame, up = its up, blended by rig.loop
    const w = rig.loop, up = rig.up;
    if (w > 1e-4) {
      const rf = rig.rf, ru = rig.ru, eu = c('eyeUpM'), lu = c('lookUpM');
      for (let i = 0; i < 3; i++) {
        rig.eye[i] += (F[i] - rf[i] * back + ru[i] * eu - rig.eye[i]) * w;
        rig.look[i] += (F[i] + rf[i] * ahead + ru[i] * lu - rig.look[i]) * w;
        up[i] = (i === 1 ? 1 - w : 0) + ru[i] * w;
      }
      const l = Math.hypot(up[0], up[1], up[2]) || 1; up[0] /= l; up[1] /= l; up[2] /= l;
    } else { up[0] = 0; up[1] = 1; up[2] = 0; }
    const K = rig.kick.out;
    // Effect envelopes belong to the border/HUD. Push follows a tilted view ray, so even a
    // nominal forward push changes eye height; boost FOV also pumps pitch through anchor aim.
    // Keep this boundary independent of all shake, push and zoom envelopes, including future cues.
    rig.vFov = vFovForAspect(feel.get('fov.baseVDeg'), aspect);
    // Anchor the actual racer, not the lagging orbit focus. The kart's own up carries its centre through
    // slopes and loops, while rig.up retains the existing smooth horizon.
    const centre = RACER_FRAMING.centerUpM * RACER_FRAMING.scale, A = rig.anchor;
    A[0] = kart[S.posX] + u[0] * centre; A[1] = kart[S.posY] + u[1] * centre; A[2] = kart[S.posZ] + u[2] * centre;
    // Keep the horizon on the route's grade. The kart is free to rise above that quiet aim.
    if (flightGuide) A[1] += (flightGuide.y - kart[S.posY]) * flightGuide.weight * .85;
    const lagUp = (kart[S.posX] - F[0]) * up[0] + (kart[S.posY] - F[1]) * up[1] + (kart[S.posZ] - F[2]) * up[2];
    const anchorDistance = Math.max(0.5, Math.hypot(A[0] - rig.eye[0], A[1] - rig.eye[1], A[2] - rig.eye[2]));
    const airTo = kart[S.airborne] > 0.5 && !flightGuide
      ? clamp(-lagUp * 0.65 / (2 * anchorDistance * Math.tan(rig.vFov * DEG / 2)), -RACER_FRAMING.airScreenMax, RACER_FRAMING.airScreenMax) : 0;
    rig.airScreen = damp(rig.airScreen, airTo, kart[S.airborne] > 0.5 ? 0.08 : 0.14, dt);
    aimAtAnchor(rig.look, rig.eye, up, A, rig.vFov, RACER_FRAMING.screenY + rig.airScreen);
    // polish 3 kick: the eye takes the whole offset, the look point half (the view dips and leans a little)
    if (K.x || K.y || K.z) {
      basis();
      for (let i = 0; i < 2; i++) {
        const o = i ? rig.look : rig.eye, s = i ? 0.5 : 1;
        for (let k = 0; k < 3; k++) o[k] += (R[k] * K.x + UU[k] * K.y + B[k] * K.z) * s;
      }
    }
    glance.apply(rig.eye, rig.look, rig.up);
  }

  function update(kart, fx, dt, aspect, track = null, lookInput = null) {
    const c = (k) => feel.get('camera.' + k);
    const px = kart[S.posX], py = kart[S.posY], pz = kart[S.posZ];
    const F = rig.focus;
    flightGuide = phraseCameraGuide(track, kart[S.s], guidePoint);
    if (Math.hypot(px - F[0], py - F[1], pz - F[2]) > LOOK.snapM) return snap(kart, aspect, fx, track);
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
    rig.pitch = damp(rig.pitch, pitchTo() * (1 - (flightGuide?.weight || 0)), flightGuide ? .55 : tcV, dt);
    // the loop blend and the smoothed kart frame (heading() just read f and u)
    const lt = loopTarget(u[1], magnetAt(kart, track));
    rig.loop = damp(rig.loop, lt, lt > rig.loop ? LOOP_CAM.inTcS : LOOP_CAM.outTcS, dt);
    if (rig.loop < 1e-3 && lt === 0) rig.loop = 0;
    easeFrame(1 - Math.exp(-dt / LOOP_CAM.frameTcS));
    // Rescue moves the rendered pose while the sim reports zero velocity. Predict that displacement
    // directly, keeping the existing eased error. Otherwise a fast pull overtakes the chase eye and
    // the exact anchor aim swings backward. Ordinary driving and hops retain their velocity law.
    const rescue = kart[S.respawnPhase] !== RESPAWN.NONE && kart[S.respawnPhase] !== RESPAWN.GRACE;
    F[0] = damp(F[0] + (rescue ? px - lastPosition[0] : kart[S.velX] * dt), px, tcH, dt);
    F[2] = damp(F[2] + (rescue ? pz - lastPosition[2] : kart[S.velZ] * dt), pz, tcH, dt);
    // Grounded climbs need the same prediction as X/Z, or the vertical ease sinks the eye into the road.
    const predictY = kart[S.grounded] > 0.5 && kart[S.airborne] < 0.5 ? 1 : rig.loop;
    const cameraY = flightGuide ? py + (flightGuide.y - py) * flightGuide.weight * .88 : py;
    F[1] = damp(F[1] + (rescue ? py - lastPosition[1] : kart[S.velY] * dt * predictY * (1 - (flightGuide?.weight || 0))),
      cameraY, flightGuide ? .55 : tcV, dt);
    lastPosition[0] = px; lastPosition[1] = py; lastPosition[2] = pz;
    const gentle = !!feel.get('gentle.on');
    for (const [kind, mag, side] of kickEdges(rig.watch, kart, dt, KL, rig.useVel)) {
      if (!flightGuide || kind !== 'land') rig.kick.hit(kind, mag, side, gentle);
    }
    rig.kick.update(dt, gentle);
    glance.update(lookInput, dt, gentle);
    compose(kart, fx, aspect, dt);
    return rig;
  }

  function snap(kart, aspect = REF_ASPECT, fx = null, track = null) {
    flightGuide = phraseCameraGuide(track, kart[S.s], guidePoint);
    rig.yaw = heading(kart);
    rig.pitch = pitchTo();
    rig.loop = loopTarget(u[1], magnetAt(kart, track));
    easeFrame(1);
    rig.bias = 0;
    rig.airScreen = 0;
    glance.reset();
    rig.kick.reset(); rig.watch = {};
    rig.focus[0] = kart[S.posX]; rig.focus[1] = kart[S.posY]; rig.focus[2] = kart[S.posZ];
    if (flightGuide) rig.focus[1] += (flightGuide.y - kart[S.posY]) * flightGuide.weight * .88;
    lastPosition[0] = kart[S.posX]; lastPosition[1] = kart[S.posY]; lastPosition[2] = kart[S.posZ];
    compose(kart, fx, aspect);
    return rig;
  }
  return rig;
}
