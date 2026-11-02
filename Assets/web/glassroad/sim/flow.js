/* sim/flow.js - water: stream, chute and pool (wave 1 WATER, BRIEF-WATER.md contracts). Pure, no state, no three.js.
 *
 * A stream node carries a current `flow = [along, lat]` in metres per second in the ROAD frame (along = the
 * road tangent, lat = the road's right, the same sign as the query's `lateral`). The kart (sim/kart.js) treats
 * that current as a moving floor: its own driving happens relative to the water and the water's velocity is
 * added on top, eased in and out over water.flowBlendTcS so entering or leaving a stream never jerks.
 * A chute is a stream node steeper than water.chuteGradeDeg: steering is scaled down and gravity along the
 * slope feeds the engine. A pool is still water: a landing on it has no bounce, loses a share of speed once
 * (water.poolLandMul) and thrust stays capped while the kart is on it.
 * The query may not carry `flow` yet (the TRACK lane adds it): a missing flow is [0, 0] and an unknown surface
 * is glass, so the game runs before and after their change.
 * Every number comes from feel (water.* keys, FEEL.md "water"); this module never reads them for the caller.
 */
import { clamp, RAD2DEG } from '../core/math.js';

export const SURFACES = Object.freeze(['glass', 'grip', 'haze', 'stream', 'pool']);
export const FLOW_ALONG_MAX = 12, FLOW_LAT_MAX = 6;   // schema bounds (track/schema.js): the sim never pushes harder

/** The surface kind the sim drives on: one of SURFACES, anything else is glass. */
export function surfaceKind(surface) { return SURFACES.indexOf(surface) >= 0 ? surface : 'glass'; }

/** Road grade in degrees from the frame tangent, positive going DOWN along +s (a descent), 0 on the flat. */
export function gradeDeg(t) {
  const flat = Math.hypot(t[0], t[2]);
  return Math.atan2(-t[1], flat) * RAD2DEG;
}

/** A fresh water result (see waterAt). */
export function waterOut() {
  return { kind: 'glass', push: [0, 0], gripMul: 1, slideMul: 1, thrustMul: 1, steerMul: 1, chute: false, pool: false, landMul: 1, dragTcS: 0 };
}

/**
 * What the ground does to a kart on this query. Pure: the same inputs give the same `out`.
 * @param {{surface?: string, flow?: number[]|null}} q the track query (only surface and flow are read)
 * @param {number} grade road grade in degrees at the kart, positive descending (gradeDeg)
 * @param {{get(path: string): any}} feel
 * @param {object} [out] a waterOut() to fill in place
 * @returns {{kind: string, push: number[], gripMul: number, slideMul: number, thrustMul: number, steerMul: number,
 *   chute: boolean, pool: boolean, landMul: number, dragTcS: number}}
 *   push = the current in the road frame [along, lat] m/s (zero off a stream); the multipliers scale the
 *   kart's grip and drift slide time constants, thrust cap and steering yaw; landMul scales speed once on a
 *   landing (pools); dragTcS > 0 asks for the extra engine drag toward the cap (pools).
 */
export function waterAt(q, grade, feel, out = waterOut()) {
  const kind = surfaceKind(q && q.surface);
  const flow = q && q.flow;
  out.kind = kind;
  out.push[0] = 0; out.push[1] = 0;
  out.gripMul = 1; out.slideMul = 1; out.thrustMul = 1; out.steerMul = 1;
  out.chute = false; out.pool = false; out.landMul = 1; out.dragTcS = 0;
  if (kind === 'stream') {
    if (flow && flow.length >= 2) {
      out.push[0] = clamp(+flow[0] || 0, -FLOW_ALONG_MAX, FLOW_ALONG_MAX);
      out.push[1] = clamp(+flow[1] || 0, -FLOW_LAT_MAX, FLOW_LAT_MAX);
    }
    out.gripMul = feel.get('water.streamGripMul');
    out.slideMul = feel.get('water.streamSlideMul');
    out.thrustMul = feel.get('water.streamThrustMul');
    if (Math.abs(grade) > feel.get('water.chuteGradeDeg')) {
      out.chute = true;
      out.steerMul = feel.get('water.chuteSteerMul');
    }
  } else if (kind === 'pool') {
    out.pool = true;
    out.thrustMul = feel.get('water.poolThrustMul');
    out.landMul = feel.get('water.poolLandMul');
    out.dragTcS = feel.get('water.poolDragTcS');
  }
  return out;
}

/** Ease the applied current toward the ground's push over water.flowBlendTcS (in place on `cur`, [along, lat]). */
export function blendPush(cur, target, dt, tcS) {
  const k = tcS > 0 ? 1 - Math.exp(-dt / tcS) : 1;
  cur[0] += (target[0] - cur[0]) * k;
  cur[1] += (target[1] - cur[1]) * k;
  return cur;
}

/** Pool landing: the ground speed left after the water takes its share (never below zero). */
export function poolLandSpeed(speed, landMul) { return Math.max(0, speed * landMul); }
