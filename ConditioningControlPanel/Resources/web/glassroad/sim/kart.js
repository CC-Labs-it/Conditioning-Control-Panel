/* sim/kart.js - one arcade kart, one fixed tick (plan section 6, CONTRACT.md section 4).
 *
 * Two layers: an engine value (engineV) that ramps through five equal bands of 0..top, and a forward
 * velocity that chases it with a linear drag. Steering slews toward the stick, then yaws by
 * steer * rate / (1 + v / ref) x the speed calm x steer.userMul (sim/steer.js). Lateral velocity bleeds into the heading with a grip time constant
 * (a longer one while drifting); the angle between them is the slip that costs engineV. "Up" is the
 * road normal from the track query, so banked and magnet surfaces need nothing special on the ground:
 * the kart is slerped to that normal, and on a magnet surface airborne gravity is minus it.
 * Rails bounce (normal x wall.normalKeep out, tangent kept); open sides drop after the edge grace.
 * Polish 6 (stacked courses): a kart in the air off its own road lands on whatever road is beneath it
 * (track.query.below, sim/falls.js); only nothing beneath lets the edge drop and rescue run.
 * Polish 6, THE LOOP: on the ground there is no gravity along the road, so a magnet stretch holds a kart at any
 * speed; in the air there gravity is minus the road normal. The road also carries the kart round: the nose eases
 * toward its own line a little way up the road (loop.guideTcS, loop.guideAheadS) and the engine keeps a forward floor (loop.carryPct of top, reached at
 * loop.carryMps2), so nobody stalls upside down at the top.
 * Wave 1 WATER (sim/flow.js): a stream node's current is a moving floor. The kart drives relative to the water
 * and the water's velocity (eased over water.flowBlendTcS) is added on top; grip and drift slide loosen and thrust
 * drops a little there. A chute (a stream steeper than water.chuteGradeDeg) scales steering down and lets gravity
 * along the slope feed the engine (no magnet). A landing on a pool has no bounce, loses a share of speed once and
 * emits `splash`; on the pool thrust is capped and the engine is dragged toward that cap. Events `surface { kind,
 * from }` (once per ground surface change) and `chute { s }` (once per chute entry). Per-kart water memory lives
 * in a WeakMap on the state array (the state layout is frozen).
 * Wave 2 STONES (sim/stones.js): inside a stone field the query answers `stone` on a stone and `hole.lip` over the
 * void. A grounded kart stepping off supported ground (the deck before the field or a stone) into that void is kicked
 * up by stones.kickUpMps along the road normal ('lip' take-off); the road's own edges and court holes never kick.
 * Over a lip void the kart counts as on its own road while it is still within the snap band of the deck (no fall
 * probe, no hole latch, no edge glow at every stone edge); below that band the ordinary hole fall runs. Events
 * `stone { id, field, s, x, p }` (distinct grounded contact) and `void { s, x }` (once per flight, first below the
 * deck over a void). Per-kart stone memory lives in a WeakMap too.
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
import { gripYaw, slewSteer } from './steer.js';
import { beneath, onLanding, firstBeneath, landRoute, catchCap, resetRoute, edgeAt, hopTrack, airCatch, turnsAway, guideLanding } from './routes.js';
import { fallBeneath, fallLand, resetFall } from './falls.js';
import { waterAt, waterOut, blendPush, gradeDeg, surfaceKind } from './flow.js';
import { lipLeave, broadPlatformEdgeKick, platformHopUp, fieldAtS } from './stones.js';
import { armGlide, glideGravity, resetGlide } from './glide.js';
import { aerialGapAt } from '../track/aerial.js';
import { airWordQuery, resetAirWords } from './airWords.js';

export { applyBoost };

// wave 2 STONES: per-kart memory (the state layout is frozen): the stone the kart stands on (or last stood on while
// airborne, so a hop on the same stone is not a new `stone`), and whether this flight already dropped below the deck
const STONES = new WeakMap();
const stoneMem = (st) => { let m = STONES.get(st); if (!m) { m = { on: null, voided: false }; STONES.set(st, m); } return m; };
/** Forget a kart's stone state (spawn, drop). */
export function resetStones(st) { const m = STONES.get(st); if (m) { m.on = null; m.voided = false; } }

// wave 1 WATER: per-kart memory (the state layout is frozen): the applied current [along, lat] in the road frame,
// the last ground surface kind and whether the kart is on a chute (for the once-per-entry events)
const WATER = new WeakMap();
const waterMem = (st) => { let m = WATER.get(st); if (!m) { m = { cur: [0, 0], kind: null, chute: false }; WATER.set(st, m); } return m; };
/** Forget a kart's water state (spawn, drop). */
export function resetWater(st) { const m = WATER.get(st); if (m) { m.cur[0] = 0; m.cur[1] = 0; m.kind = null; m.chute = false; } }
const W = waterOut(), WV = v3(), FW = { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] };
const PUSH_EPS = 1e-3;

const P = v3(), PRE = v3(), V = v3(), Q = q4(), F = v3(), U = v3(), N = v3(), FP = v3(), RP = v3(), QY = q4();
// out objects pre-shaped: the track fills arrays in place (CONTRACT.md Frame, Query)
const qOut = () => ({ normal: [0, 0, 0], p: [0, 0, 0] }), fOut = () => ({ p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] });
const QA = qOut(), QB = qOut(), QR = qOut(), QD = qOut(), FR = fOut(), FL = fOut();
const FWD = [0, 0, -1], UP = [0, 1, 0];
const HOLE_FALL = new WeakSet(), HOLE_GRACE_S = 0.45;
const has = (flags, f) => !!flags && flags.indexOf(f) >= 0;

/** At rest on track.spawnPose(slot). @returns {Float32Array} state */
export function spawn(st, track, feel, slot = 0) {
  const pose = track.spawnPose(slot);
  st.fill(0);
  HOLE_FALL.delete(st);
  setPos(st, pose.p); setQuat(st, pose.q);
  st[S.seg] = pose.seg; st[S.latchSeg] = pose.seg; st[S.s] = pose.s; st[S.lateral] = pose.lateral;
  st[S.grounded] = 1; st[S.hopT] = 1e3;          // the last hop was long ago
  st[S.sectionIdx] = track.sectionAt(pose.s);
  resetRoute(st); resetFall(st); resetWater(st); resetStones(st); resetGlide(st); resetAirWords(st);
  return st;
}

/** One fixed tick. Respawn owns the kart during PULL, HOVER and RELEASE. */
export function step(st, a, track, feel, dt, ev) {
  const ph = st[S.respawnPhase];
  if (ph === RESPAWN.PULL || ph === RESPAWN.HOVER || ph === RESPAWN.RELEASE) { HOLE_FALL.delete(st); resetGlide(st); resetAirWords(st); return; }
  const grace = ph === RESPAWN.GRACE;
  const top = feel.get('speed.topMps');
  getPos(P, st); getVel(V, st); getQuat(Q, st);
  const qa = airWordQuery(st, P, track.query(P[0], P[1], P[2], st[S.seg] | 0, QA));
  v3copy(PRE, P);
  const wasGround = st[S.grounded] > 0;
  const wasEdge = !wasGround && !has(qa.flags, 'jump') && (qa.edgeDist < 0 || HOLE_FALL.has(st));
  const prevS = st[S.s], prevLat = st[S.lateral];
  st[S.hopT] += dt;
  if (st[S.stalled] > 0) st[S.stalled] = Math.max(0, st[S.stalled] - dt);
  const stalled = st[S.stalled] > 0;

  // wave 1 WATER: what the ground does this tick (sim/flow.js). The road frame is read on a stream, or while an
  // old current is still easing out; the grade comes from its tangent (positive = a descent).
  const wm = waterMem(st);
  const wet = wasGround && (qa.surface === 'stream' || Math.abs(wm.cur[0]) > PUSH_EPS || Math.abs(wm.cur[1]) > PUSH_EPS);
  if (wet) (track.pathFrameAt ? track.pathFrameAt(qa.s, qa.seg, FW) : track.frameAt(qa.s, FW));
  waterAt(wasGround ? qa : null, wet && qa.surface === 'stream' ? gradeDeg(FW.t) : 0, feel, W);
  if (wasGround) {
    if (wm.kind === null) wm.kind = W.kind;
    else if (W.kind !== wm.kind) { ev.emit('surface', { kind: W.kind, from: wm.kind }); wm.kind = W.kind; }
    if (W.chute && !wm.chute) ev.emit('chute', { s: qa.s });
    wm.chute = W.chute;
  }

  // steering: slew toward the stick (quicker near centre), then the yaw law
  const steer = (st[S.steerSlew] = slewSteer(st[S.steerSlew], stalled ? 0 : clamp(a.steer, -1, 1), feel, dt));
  qrotate(U, Q, UP); qrotate(F, Q, FWD);
  if (wasGround) v3norm(U, v3lerp(U, U, qa.normal, 1 - Math.exp(-dt / gap(feel, 'kart.alignTcS'))));
  v3norm(F, v3reject(F, F, U));
  const fwd0 = v3dot(V, F);
  // Both steering modes share speed calm; a drift adds a bounded stronger turn.
  let yaw = st[S.driftDir] ? driftYaw(st, steer, feel, gripYaw(fwd0, feel)) : steer * gripYaw(fwd0, feel) * (fwd0 < 0 ? -1 : 1);
  if (stalled) yaw = 0;
  yaw *= W.steerMul;                               // a chute steers less (wave 1 WATER)
  st[S.yawRate] = yaw;
  qrotate(F, qaxis(QY, U, -yaw * dt), F);         // + yaw turns right: clockwise seen from above
  if (wasGround) guideLanding(track, st, qa, F, feel, dt);
  const loop = wasGround && has(qa.flags, 'magnet');
  if (loop) {
    // aim at the kart's own line a little way up the road: the wheel's sideways shift never walks it into a rail
    const la = Math.max(4, Math.abs(fwd0) * gap(feel, 'loop.guideAheadS')), f = track.pathFrameAt ? track.pathFrameAt(qa.s + la, qa.seg, FL) : track.frameAt(qa.s + la, FL);
    const ax = f.p[0] + f.b[0] * qa.lateral - P[0], ay = f.p[1] + f.b[1] * qa.lateral - P[1], az = f.p[2] + f.b[2] * qa.lateral - P[2];
    const al = Math.hypot(ax, ay, az) || 1;
    if ((F[0] * ax + F[1] * ay + F[2] * az) / al > 0.3) {
      const k = 1 - Math.exp(-dt / gap(feel, 'loop.guideTcS'));
      F[0] += (ax / al - F[0]) * k; F[1] += (ay / al - F[1]) * k; F[2] += (az / al - F[2]) * k;
      v3norm(F, v3reject(F, F, U));
    }
  }

  // a drop shortcut's soft catch caps thrust for a moment; a clean landing pays its boost as it ends
  const cc = catchCap(st, feel, dt);
  if (cc === 'boost' && !stalled) {
    applyBoost(st, BOOST.PAD, 0, feel, ev, { amount: gap(feel, 'route.cleanAmount'), holdS: gap(feel, 'route.cleanHoldS') });
    ev.emit('route-boost', {});
  }
  // a chute: gravity along the kart's own downhill forward feeds the engine (no magnet holds it)
  const slope = W.chute ? feel.get('hop.gravityMps2') * -F[1] : 0;
  engine(st, a, feel, dt, top, wasGround && qa.surface === 'haze', ev, typeof cc === 'number' ? cc : 1, loop, W.thrustMul, W.dragTcS, slope);

  // on the ground: forward speed chases engineV, lateral bleeds into the heading, slip costs engineV
  let slipDeg = 0;
  if (wasGround) {
    v3copy(N, qa.normal);
    v3norm(FP, v3reject(FP, F, N)); v3cross(RP, FP, N);
    // wave 1 WATER: the current is a moving floor. It eases toward the ground's push, the kart drives relative to
    // it (chase, grip, slip) and the water's velocity goes back on top, so neither entering nor leaving jerks.
    blendPush(wm.cur, W.push, dt, feel.get('water.flowBlendTcS'));
    const carry = Math.abs(wm.cur[0]) > PUSH_EPS || Math.abs(wm.cur[1]) > PUSH_EPS;
    if (carry) {                                   // wet held: a current only grows on a stream, where FW was read
      v3scale(WV, FW.t, wm.cur[0]); v3addScaled(WV, WV, FW.b, wm.cur[1]); v3reject(WV, WV, N);
    } else { wm.cur[0] = 0; wm.cur[1] = 0; WV[0] = 0; WV[1] = 0; WV[2] = 0; }
    let f = v3dot(V, FP) - v3dot(WV, FP), l = v3dot(V, RP) - v3dot(WV, RP);
    f += (st[S.engineV] - f) * Math.min(1, dt / feel.get('engine.chaseTcS'));
    const tc = st[S.driftDir] ? gap(feel, 'drift.slideTcS') * W.slideMul : gap(feel, 'kart.gripTcS') * W.gripMul;
    l *= Math.exp(-dt / tc);
    slipDeg = Math.atan2(Math.abs(l), Math.max(Math.abs(f), 1)) * RAD2DEG;
    v3scale(V, FP, f); v3addScaled(V, V, RP, l);
    if (carry) { V[0] += WV[0]; V[1] += WV[1]; V[2] += WV[2]; }
    if (st[S.boostKind] === BOOST.NONE && st[S.engineV] > 0) {
      const cost = feel.get(st[S.driftDir] ? 'steer.slipCostDriftPerDegS' : 'steer.slipCostGripPerDegS');
      st[S.engineV] -= st[S.engineV] * cost * slipDeg * dt;
    }
  }
  const speed = v3dot(V, F);
  updateDrift(st, a, feel, dt, ev, wasGround, speed, slipDeg);

  if (wasGround && a.hopPressed && !stalled) {
    // just before a drop shortcut's kicker lip the hop sees that lip too (the trick's early window)
    hop(st, feel, ev, V, qa.normal, speed, hopTrack(track, qa, V, speed, feel.get('trick.earlyS')));
    // Large challenge islands give a deliberate jump more hang time; coasting keeps its small lip kick.
    const platformUp = qa.airWord ? Math.max(10.5, feel.get('hop.upMps'))
      : platformHopUp(track, qa, Math.abs(speed), feel.get('hop.upMps'), feel.get('hop.gravityMps2'));
    v3addScaled(V, V, qa.normal, platformUp - feel.get('hop.upMps'));
    if (st[S.airborne] && (qa.surface === 'stream' || qa.surface === 'pool')) ev.emit('water-hop', { s: qa.s, x: qa.lateral, speed: Math.abs(speed), down: 0, kind: 'hop', p: [...P], normal: [...qa.normal] });
    armGlide(st, track, qa, V);
    maybeStartDrift(st, a, feel, speed, ev);
  }
  const air = st[S.airborne] > 0;
  if (air) {
    const g = glideGravity(st, a, qa, V, gravityMps2(st, feel), feel, dt);
    if (has(qa.flags, 'magnet')) v3addScaled(V, V, qa.normal, -g * dt); else V[1] -= g * dt;
  }

  // move, then find the ground again
  v3addScaled(P, P, V, dt);
  let qb = airWordQuery(st, P, track.query(P[0], P[1], P[2], qa.seg, QB), PRE);
  const snap = gap(feel, 'kart.snapM');
  // A low hop reaching the edge of a broad Keep platform receives its normal lip kick once.
  if (air) v3addScaled(V, V, qb.normal, broadPlatformEdgeKick(track, qa, qb, v3dot(V, qb.normal), feel.get('stones.kickUpMps')));
  // wave 2 STONES: over a field's void the kart is still on its own road while within the snap band of the deck
  // (a lip kick across a gap lands on the next stone); below that band it has fallen through: `void` once per flight
  const sm = stoneMem(st);
  const lipVoid = !!qb.hole && qb.hole.lip === true;
  const overLip = lipVoid && qb.height > -snap;
  if (air && lipVoid && !overLip && !sm.voided) { sm.voided = true; ev.emit('void', { s: qb.s, x: qb.lateral }); }
  // off its own road in the air: a drop shortcut's landing road beneath it (sim/routes.js)
  let under = null, fall = false;
  const own = (qb.onRoad || overLip) && qb.height > -snap - 0.5;
  if (air && track.routes && track.routes.length) {
    let lq = qb;
    if (!own) {
      under = beneath(track, st, P, QR, gap(feel, 'route.catchMarginM')); lq = QR;
      if (under && QR.height <= 0) qb = QR;
    } else under = onLanding(track, st, qb);      // the plain probe (its far search) already found it
    if (under && firstBeneath(st, under)) { ev.emit('route-drop', { id: under.id }); if (wasEdge) ev.emit('edge-exit', { to: 'route' }); }
    if (under) airCatch(track, lq, V, feel, dt, F);
  }
  // polish 6: any other road beneath a flying kart off its own road holds off the edge drop and catches it
  if (air && !under && !own && fallBeneath(track, st, P, QD, ev, wasEdge)) {
    fall = true;
    if (QD.height <= 0) qb = QD;
  }
  const road = !has(qb.flags, 'jump'), n = qb.normal, vn = v3dot(V, n);
  const ground = qb.onRoad && qb.height >= -(snap + Math.abs(vn) * dt) &&
    (air ? qb.height <= 0 && vn <= 0 : qb.height <= snap);
  if (ground) {
    v3addScaled(P, P, n, -qb.height); v3reject(V, V, n);
    const arrive = Math.hypot(V[0], V[1], V[2]);   // ground speed at touch-down (the splash payload)
    if (air) {
      land(st, a, feel, ev, V, n, speed, dt, !!qb.airWord || !!qb.stone && !!fieldAtS(track, qb.s)?.platforming);
      if (qb.airWord) {
        st[S.airborne] = 0; st[S.grounded] = 1; v3reject(V, V, n);
        ev.emit('air-word-land', { id: qb.airWord.id, text: qb.airWord.text, s: qb.s });
      } else resetGlide(st);
    }
    // wave 1 WATER: a pool catches the kart: no bounce back up, the water takes its share of speed once, splash
    if (air && surfaceKind(qb.surface) === 'pool') {
      if (st[S.airborne]) { st[S.airborne] = 0; st[S.grounded] = 1; v3reject(V, V, n); }
      const m = feel.get('water.poolLandMul');
      v3scale(V, V, m); st[S.engineV] *= m;
    }
    if (air && (qb.surface === 'stream' || qb.surface === 'pool')) ev.emit('splash', { s: qb.s, x: qb.lateral, speed: arrive, down: Math.max(0, -vn), p: [...P], normal: [...n] });
    if (air && under && onLanding(track, st, qb) === under) landRoute(track, st, under, qb, F, feel, ev, prevS);
    else if (air && fall && qb === QD) fallLand(track, st, qb, ev);
    else if (air) resetFall(st);
  } else if (!air) {
    // a drop shortcut's kicker lip (sim/routes.js edgeAt): driving off the opening throws the kart up a little,
    // a 'lip' take-off, so a hop right after it pays the trick
    // wave 2 STONES: every step from supported ground (deck or stone) into a field's void is a kicker lip too
    // (sim/stones.js lipLeave), the kick is stones.kickUpMps
    const lip = road && lipLeave(qa, qb);
    const kick = !lip && road && qb.edgeDist < 0 && edgeAt(track, qb, qb.lateral, gap(feel, 'kart.radiusM')) === 'kicker';
    // the kick never carries a kart further than stones.flightCapM before it is level with the deck again (a pad
    // boost at 35 m/s with the full kick flew a whole stone-and-gap period and came down in the next void)
    if (lip) v3addScaled(V, V, n, Math.min(feel.get('stones.kickUpMps'), feel.get('stones.flightCapM') * feel.get('hop.gravityMps2') / (2 * Math.max(1, speed))));
    else if (kick) v3addScaled(V, V, n, gap(feel, 'route.kickUpMps'));
    takeoff(st, ev, !kick && !lip && road && (qb.edgeDist < 0 || qb.hole) ? 'edge' : 'lip');
    if (!road) {
      armGlide(st, track, qb);
      // The authored launch lip supplies lift. The player still holds the cap to glide.
      if (!qb.forkId && aerialGapAt(track, qb.s) && !qa.airWord && qa.onRoad) V[1] += 14;
    }
  } else st[S.airTime] += dt;
  // wave 2 STONES: a stone the kart was not on before (a landing or the deck handing over to the first stone)
  if (ground) {
    const id = qb.stone ? qb.stone.id : null;
    if (id !== null && id !== sm.on) ev.emit('stone', { id, field: fieldAtS(track,qb.s)?.id || null, s: qb.s, x: qb.lateral, p: [...P] });
    sm.on = id; sm.voided = false;
  }

  // rails (and open sides under Gentle, the soft rails)
  const radius = gap(feel, 'kart.radiusM');
  let lat = qb.lateral, ed = qb.edgeDist;
  // a drop shortcut's open side is a rail but for its kicker opening, which lets every kart through (Gentle too)
  const edgeKind = ed < radius ? edgeAt(track, qb, lat, radius) : null;
  // polish 6: on the ground the opening holds a kart turning away from it (sim/routes.js turnsAway)
  // Counter-steering or a lingering drift must not block a nose deliberately aimed through the arch.
  const towardOpening = edgeKind === 'kicker' && v3dot(F, track.frameAt(qb.s, FR).b) * (lat >= 0 ? 1 : -1) > 0.35;
  const kerb = edgeKind === 'rail' || (edgeKind === 'kicker' && !air && !towardOpening && turnsAway(st, lat, feel));
  // a kart above the rail top, or already beyond the rail line in the air, flies past it (polish 4)
  const overRail = air && (qb.height > gap(feel, 'wall.topM') || ed < 0);
  const soft = feel.get('gentle.on') && edgeKind !== 'kicker';
  if (!qb.airWord && road && !overRail && ((qb.railed && edgeKind !== 'kicker') || kerb || soft) && ed < radius && qb.height > -radius) {
    const b = (track.pathFrameAt ? track.pathFrameAt(qb.s, qb.seg, FR) : track.frameAt(qb.s, FR)).b, side = lat >= 0 ? 1 : -1, pen = radius - ed;
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
  if (ground || under || fall) HOLE_FALL.delete(st);
  else if (qb.hole && qb.height <= (lipVoid ? -snap : 0)) HOLE_FALL.add(st);   // a lip void latches below the snap band
  // Keep a real hole fall latched after passing under its far lip; a hop above it does not count.
  const holeFall = HOLE_FALL.has(st);
  const past = !st[S.grounded] && road && (ed < 0 || holeFall) && !under && !fall;
  if (past && !wasEdge) ev.emit('edge-enter', { side: lat >= 0 ? 1 : -1 });
  const lim = track.limits || {};
  if (!past) st[S.edgeT] = 0;
  else if (holeFall || -ed > feel.get('drop.edgeGraceM')) st[S.edgeT] += dt;
  const fell = P[1] < (lim.fallY ?? -Infinity);
  const drop = !grace && (fell || st[S.edgeT] >= (holeFall ? Math.min(HOLE_GRACE_S, feel.get('drop.edgeGraceS')) : feel.get('drop.edgeGraceS')));
  if (drop) {
    if (wasEdge || past) ev.emit('edge-exit', { to: 'drop' });
    cancelDrift(st, 'drop', ev); endBoost(st, feel, ev); resetRoute(st); resetFall(st); resetWater(st); resetStones(st); resetGlide(st);
    st[S.respawnPhase] = RESPAWN.PULL; st[S.respawnT] = 0; st[S.edgeT] = 0;
    HOLE_FALL.delete(st);
    ev.emit('drop', { seg: qb.seg, cause: fell ? 'fallY' : holeFall ? 'hole' : 'edge' });
  } else if (wasEdge && !past && !under && !fall) ev.emit('edge-exit', { to: 'road' });

  // boost pads: once per entry, on the ground
  if (st[S.grounded] && !drop && !qb.forkId && track.boosts) {
    const L = track.lengthM;
    // State stores Float32 positions. Compare a fresh query at the same precision so an
    // accepted edge cannot round outside in prevS/prevLat and retrigger next tick.
    const inside = (pad, s, x) => wrap(Math.fround(s) - pad.s, L) <= pad.len && Math.abs(Math.fround(x) - pad.x) <= pad.w / 2;
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
 * Haze cuts thrust and the cap unless a boost runs; a running boost holds its floor (sim/boost.js).
 * Wave 1 WATER: waterMul scales thrust and the cap the same way (a boost keeps its floor as on haze); dragTcS > 0
 * (a pool) drags an engine above the cap toward it on top of the coast; slopeMps2 (a chute) is gravity along the
 * kart's downhill forward, capped at speed.maxPct of top. */
function engine(st, a, feel, dt, top, haze, ev, capMul = 1, loop = false, waterMul = 1, dragTcS = 0, slopeMps2 = 0) {
  const boosting = st[S.boostKind] !== BOOST.NONE;
  const mul = (haze && !boosting ? 1 - feel.get('offroad.hazeThrustLoss') : 1) * (boosting ? 1 : waterMul) *
    (st[S.stalled] > 0 ? feel.get('stall.thrustMul') : 1);
  const cap = top * mul * capMul, brakeR = feel.get('engine.brakeMps2'), coast = feel.get('engine.coastMps2') * dt;
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
  if (dragTcS > 0 && !boosting && e > cap) e = cap + (e - cap) * Math.exp(-dt / dragTcS);   // the pool's drag
  if (slopeMps2 && st[S.stalled] <= 0) e = clamp(e + slopeMps2 * dt, -feel.get('speed.reverseMps'), top * feel.get('speed.maxPct'));
  if (loop && e >= 0) {                             // the loop carries the kart through (never stalls inside it)
    const carry = gap(feel, 'loop.carryPct') * top;
    if (e < carry) e = Math.min(carry, e + gap(feel, 'loop.carryMps2') * dt + (a.brake > 0 ? brakeR * a.brake * dt : 0));
  }
  st[S.engineV] = e;
}
