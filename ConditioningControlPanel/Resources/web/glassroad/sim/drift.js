/* sim/drift.js - drift start, cancel, charge tiers and release (plan section 6).
 *
 * A hop above drift.minSpeedMps with the stick off centre starts a drift that always curves INTO
 * that side; the stick then only sets tightness (tightMin..tightMax of yawMul x grip yaw). Charge is
 * held time times the soft-drift modifier (into the drift x softInMul, counter-steering x softOutMul),
 * and only accrues on the ground. Releasing hop pays the reached tier as a boost. Cancels: slip under
 * cancelSlipDeg for cancelSlipS, speed under minSpeed, wall, stall (and a drop). The beat release
 * bonus is off in the slice (drift.beatBonusOn false) and the sim has no music beat to judge it by.
 */
import { S, BOOST } from './state.js';
import { lerp, clamp } from '../core/math.js';
import { gap } from './gaps.js';
import { applyBoost } from './boost.js';

export function startDrift(st, dir, events) {
  st[S.driftDir] = dir; st[S.driftStage] = 0; st[S.driftCharge] = 0; st[S.slipLowT] = 0;
  events.emit('drift-start', { dir });
}

/** Start a drift if the stick and speed allow it. @returns {boolean} */
export function maybeStartDrift(st, a, feel, speed, events) {
  if (st[S.driftDir] || Math.abs(a.steer) <= gap(feel, 'drift.steerMin') || speed <= feel.get('drift.minSpeedMps')) return false;
  startDrift(st, Math.sign(a.steer), events);
  return true;
}

export function cancelDrift(st, reason, events) {
  if (!st[S.driftDir]) return;
  clearDrift(st);
  events.emit('drift-cancel', { reason });
}

function clearDrift(st) { st[S.driftDir] = 0; st[S.driftStage] = 0; st[S.driftCharge] = 0; st[S.slipLowT] = 0; }

/** Drift yaw rate (rad/s, + right) from the slewed stick. */
export function driftYaw(st, steer, feel, gripYaw) {
  const dir = st[S.driftDir];
  const tight = lerp(feel.get('drift.tightMin'), feel.get('drift.tightMax'), (clamp(steer * dir, -1, 1) + 1) / 2);
  return dir * feel.get('drift.yawMul') * gripYaw * tight;   // gripYaw here = the uncalmed base law (sim/steer.js)
}

/** One tick of a running drift: release, cancels, charge and tier steps. */
export function updateDrift(st, a, feel, dt, events, grounded, speed, slipDeg) {
  const dir = st[S.driftDir];
  if (!dir) return;
  if (!a.hop) {
    const tier = st[S.driftStage] | 0;
    clearDrift(st);
    events.emit('drift-release', { tier });
    if (tier > 0) applyBoost(st, BOOST.DRIFT1 + tier - 1, tier, feel, events);
    return;
  }
  if (st[S.stalled] > 0) return cancelDrift(st, 'stall', events);
  if (!grounded) return;
  if (speed < feel.get('drift.minSpeedMps')) return cancelDrift(st, 'slow', events);
  if (slipDeg < feel.get('drift.cancelSlipDeg')) {
    st[S.slipLowT] += dt;
    if (st[S.slipLowT] >= feel.get('drift.cancelSlipS')) return cancelDrift(st, 'slip', events);
  } else st[S.slipLowT] = 0;
  const m = gap(feel, 'drift.steerMin'), into = a.steer * dir;
  const mul = into > m ? feel.get('drift.softInMul') : into < -m ? feel.get('drift.softOutMul') : 1;
  st[S.driftCharge] += dt * mul;
  const tiers = feel.get('drift.tierS');
  let tier = 0;
  while (tier < tiers.length && st[S.driftCharge] >= tiers[tier]) tier++;
  if (tier > st[S.driftStage]) {
    st[S.driftStage] = tier;
    events.emit('drift-tier', { tier, dir });
  }
}
