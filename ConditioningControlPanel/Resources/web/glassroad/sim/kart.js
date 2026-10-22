/* sim/kart.js - one arcade kart, one fixed tick (plan section 6, CONTRACT.md section 4).
 *
 * Two layers: an engine value (engineV) that ramps through five equal bands of 0..top, and a forward
 * velocity that chases it with a linear drag. Steering slews toward the stick, then yaws by
 * steer * rate / (1 + v / ref) x the speed calm x steer.userMul (sim/steer.js). Lateral velocity bleeds into the heading with a grip time constant
 * (a longer one while drifting); the angle between them is the slip that costs engineV. "Up" is the
 * road normal from the track query, so banked and magnet surfaces need nothing special on the ground:
 * the kart is slerped to that normal, and on a magnet surface airborne gravity is minus it.
 * Rails bounce (normal x wall.normalKeep out, tangent kept); open sides drop after the edge grace.
 * No three.js; every tuning number comes from feel each tick (sim/gaps.js for the few it lacks).
 */
import { S, BOOST, RESPAWN, getPos, getVel, getQuat, setPos, setVel, setQuat } from './state.js';
import { clamp, lerp, damp, approach, wrap, RAD2DEG, v3, q4, v3copy, v3dot, v3cross, v3norm, v3reject,
  v3addScaled, v3scale, v3lerp, qrotate, qaxis, qlook } from '../core/math.js';
import { gap } from './gaps.js';
import { applyBoost, boostFloor, endBoost } from './boost.js';
import { updateDrift, cancelDrift, driftYaw, maybeStartDrift } from './drift.js';
import { hop, gravityMps2 } from './hop.js';
import { takeoff, land } from './air.js';
import { baseYaw, gripYaw } from './steer.js';

export { applyBoost };

const P = v3(), V = v3(), Q = q4(), F = v3(), U = v3(), N = v3(), FP = v3(), RP = v3(), QY = q4();
// out objects pre-shaped: the track fills arrays in place (CONTRACT.md Frame, Query)
const qOut = () => ({ normal: [0, 0, 0], p: [0, 0, 0] }), fOut = () => ({ p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] });
const QA = qOut(), QB = qOut(), FR = fOut();
const FWD = [0, 0, -1], UP = [0, 1, 0];
const has = (flags, f) => !!flags && flags.indexOf(f) >= 0;

/** At rest on track.spawnPose(slot). @returns {Float32Array} state */
export function spawn(st, track, feel, slot = 0) {
  const pose = track.spawnPose(slot);
  st.fill(0);
  setPos(st, pose.p); setQuat(st, pose.q);
  st[S.seg] = pose.seg; st[S.latchSeg] = pose.seg; st[S.s] = pose.s; st[S.lateral] = pose.lateral;
  st[S.grounded] = 1; st[S.hopT] = 1e3;          // the last hop was long ago
  st[S.sectionIdx] = track.sectionAt(pose.s);
  return st;
}

/** One fixed tick. Respawn owns the kart during PULL, HOVER and RELEASE. */
export function step(st, a, track, feel, dt, ev) {
  const ph = st[S.respawnPhase];
  if (ph === RESPAWN.PULL || ph === RESPAWN.HOVER || ph === RESPAWN.RELEASE) return;
  const grace = ph === RESPAWN.GRACE;
  const top = feel.get('speed.topMps');
  getPos(P, st); getVel(V, st); getQuat(Q, st);
  const qa = track.query(P[0], P[1], P[2], st[S.seg] | 0, QA);
  const wasGround = st[S.grounded] > 0;
  const wasEdge = !wasGround && !has(qa.flags, 'jump') && qa.edgeDist < 0;
  const prevS = st[S.s], prevLat = st[S.lateral];
  st[S.hopT] += dt;
  if (st[S.stalled] > 0) st[S.stalled] = Math.max(0, st[S.stalled] - dt);
  const stalled = st[S.stalled] > 0;

  // steering: slew toward the stick (quicker near centre), then the yaw law
  const slewTc = lerp(feel.get('steer.slewCentreS'), feel.get('steer.slewFullS'), Math.abs(st[S.steerSlew]));
  const steer = (st[S.steerSlew] = damp(st[S.steerSlew], stalled ? 0 : clamp(a.steer, -1, 1), slewTc, dt));
  qrotate(U, Q, UP); qrotate(F, Q, FWD);
  if (wasGround) v3norm(U, v3lerp(U, U, qa.normal, 1 - Math.exp(-dt / gap(feel, 'kart.alignTcS'))));
  v3norm(F, v3reject(F, F, U));
  const fwd0 = v3dot(V, F);
  // grip: the calmed law (sim/steer.js); a drift keeps the uncalmed base, so it stays the strong turn
  let yaw = st[S.driftDir] ? driftYaw(st, steer, feel, baseYaw(fwd0, feel)) : steer * gripYaw(fwd0, feel) * (fwd0 < 0 ? -1 : 1);
  if (stalled) yaw = 0;
  st[S.yawRate] = yaw;
  qrotate(F, qaxis(QY, U, -yaw * dt), F);         // + yaw turns right: clockwise seen from above

  engine(st, a, feel, dt, top, wasGround && qa.surface === 'haze', ev);

  // on the ground: forward speed chases engineV, lateral bleeds into the heading, slip costs engineV
  let slipDeg = 0;
  if (wasGround) {
    v3copy(N, qa.normal);
    v3norm(FP, v3reject(FP, F, N)); v3cross(RP, FP, N);
    let f = v3dot(V, FP), l = v3dot(V, RP);
    f += (st[S.engineV] - f) * Math.min(1, dt / feel.get('engine.chaseTcS'));
    l *= Math.exp(-dt / gap(feel, st[S.driftDir] ? 'drift.slideTcS' : 'kart.gripTcS'));
    slipDeg = Math.atan2(Math.abs(l), Math.max(Math.abs(f), 1)) * RAD2DEG;
    v3scale(V, FP, f); v3addScaled(V, V, RP, l);
    if (st[S.boostKind] === BOOST.NONE && st[S.engineV] > 0) {
      const cost = feel.get(st[S.driftDir] ? 'steer.slipCostDriftPerDegS' : 'steer.slipCostGripPerDegS');
      st[S.engineV] -= st[S.engineV] * cost * slipDeg * dt;
    }
  }
  const speed = v3dot(V, F);
  updateDrift(st, a, feel, dt, ev, wasGround, speed, slipDeg);

  if (wasGround && a.hopPressed && !stalled) {
    hop(st, feel, ev, V, qa.normal, speed);
    maybeStartDrift(st, a, feel, speed, ev);
  }
  const air = st[S.airborne] > 0;
  if (air) {
    const g = gravityMps2(st, feel);
    if (has(qa.flags, 'magnet')) v3addScaled(V, V, qa.normal, -g * dt); else V[1] -= g * dt;
  }

  // move, then find the ground again
  v3addScaled(P, P, V, dt);
  const qb = track.query(P[0], P[1], P[2], qa.seg, QB);
  const road = !has(qb.flags, 'jump'), n = qb.normal, vn = v3dot(V, n);
  const snap = gap(feel, 'kart.snapM');
  const ground = qb.onRoad && qb.height >= -(snap + Math.abs(vn) * dt) &&
    (air ? qb.height <= 0 && vn <= 0 : qb.height <= snap);
  if (ground) {
    v3addScaled(P, P, n, -qb.height); v3reject(V, V, n);
    if (air) land(st, a, feel, ev, V, n, speed, dt);
  } else if (!air) takeoff(st, ev, road && qb.edgeDist < 0 ? 'edge' : 'lip');
  else st[S.airTime] += dt;

  // rails (and open sides under Gentle, the soft rails)
  const radius = gap(feel, 'kart.radiusM');
  let lat = qb.lateral, ed = qb.edgeDist;
  if (road && (qb.railed || feel.get('gentle.on')) && ed < radius && qb.height > -radius) {
    const b = track.frameAt(qb.s, FR).b, side = lat >= 0 ? 1 : -1, pen = radius - ed;
    v3addScaled(P, P, b, -side * pen); lat -= side * pen; ed = radius;
    const into = v3dot(V, b) * side;
    if (into > 0) {
      const keep = feel.get('wall.normalKeep');
      v3addScaled(V, V, b, -side * into * (1 + keep));
      if (v3dot(F, b) * side > 0) v3norm(F, v3reject(F, F, b));   // the nose turns along the rail
      if (into >= gap(feel, 'wall.minHitMps')) {
        st[S.engineV] *= 1 - feel.get('wall.engineLoss');
        if (!grace) { cancelDrift(st, 'wall', ev); endBoost(st, feel, ev); }
        ev.emit('wall', { dv: into * (1 + keep), side });
      }
    }
  }

  // edge grace and drop
  const past = !st[S.grounded] && road && ed < 0;
  if (past && !wasEdge) ev.emit('edge-enter', { side: lat >= 0 ? 1 : -1 });
  const lim = track.limits || {};
  if (!past) st[S.edgeT] = 0;
  else if (-ed > feel.get('drop.edgeGraceM')) st[S.edgeT] += dt;   // feel owns the grace; track limits only fallY
  const fell = P[1] < (lim.fallY ?? -Infinity);
  const drop = !grace && (fell || st[S.edgeT] >= feel.get('drop.edgeGraceS'));
  if (drop) {
    if (wasEdge || past) ev.emit('edge-exit', { to: 'drop' });
    cancelDrift(st, 'drop', ev); endBoost(st, feel, ev);
    st[S.respawnPhase] = RESPAWN.PULL; st[S.respawnT] = 0; st[S.edgeT] = 0;
    ev.emit('drop', { seg: qb.seg, cause: fell ? 'fallY' : 'edge' });
  } else if (wasEdge && !past) ev.emit('edge-exit', { to: 'road' });

  // boost pads: once per entry, on the ground
  if (st[S.grounded] && !drop && track.boosts) {
    const L = track.lengthM;
    const inside = (pad, s, x) => wrap(s - pad.s, L) <= pad.len && Math.abs(x - pad.x) <= pad.w / 2;
    track.boosts.forEach((pad, i) => {
      if (inside(pad, qb.s, lat) && !(wasGround && inside(pad, prevS, prevLat))) {
        ev.emit('pad', { index: i });
        applyBoost(st, BOOST.PAD, 0, feel, ev);
      }
    });
  }

  setPos(st, P); setVel(st, V); setQuat(st, qlook(Q, F, U));
  st[S.seg] = qb.seg; st[S.segT] = qb.segT; st[S.s] = qb.s; st[S.lateral] = lat;
  st[S.height] = ground ? 0 : qb.height;
  if (st[S.grounded]) st[S.latchSeg] = qb.seg;     // frozen from take-off until the next touch-down
  st[S.speedPct] = clamp(v3dot(V, F) / top, 0, feel.get('speed.maxPct'));
}

/* Engine value: five-band ramp under throttle (crawl floor speed.minMps), brake then reverse, coast.
 * Haze cuts thrust and the cap unless a boost runs; a running boost holds its floor (sim/boost.js). */
function engine(st, a, feel, dt, top, haze, ev) {
  const boosting = st[S.boostKind] !== BOOST.NONE;
  const mul = (haze && !boosting ? 1 - feel.get('offroad.hazeThrustLoss') : 1) * (st[S.stalled] > 0 ? feel.get('stall.thrustMul') : 1);
  const cap = top * mul, brakeR = feel.get('engine.brakeMps2'), coast = feel.get('engine.coastMps2') * dt;
  let e = st[S.engineV];
  if (a.brake > 0 && a.brake >= a.throttle) e = Math.max(-feel.get('speed.reverseMps'), e - brakeR * a.brake * dt);
  else if (a.throttle > 0) {
    if (e < 0) e = Math.min(0, e + brakeR * dt);    // out of reverse first
    else {
      e = Math.max(e, feel.get('speed.minMps'));
      if (e < cap) {
        const g = feel.get('engine.bandGains');
        const band = clamp(Math.floor(e / top * g.length), 0, g.length - 1);
        e = Math.min(cap, e + g[band] * top * a.throttle * mul * dt);
      }
    }
  } else e = approach(e, 0, coast);
  const floor = boostFloor(st, feel, top, dt, ev);
  if (boosting) e = e > cap ? Math.max(cap, floor) : Math.max(e, floor);
  else if (e > cap) e = Math.max(cap, e - coast);
  st[S.engineV] = e;
}
