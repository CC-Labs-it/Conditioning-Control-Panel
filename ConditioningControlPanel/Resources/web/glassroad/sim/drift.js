/* sim/drift.js - drift start, cancel, charge tiers and release (plan section 6).
 *
 * Two ways in, one slide. A hop above drift.minSpeedMps with the stick off centre starts a drift (held
 * by hop). Polish 4, the power slide: with the throttle held (drift.brakeThrottleMin), a brake press
 * (drift.brakeMin, fresh within drift.brakeEntryS) while steering past drift.steerMin on the ground
 * starts one too (held by the brake). Either way it curves INTO that side; the stick then only sets
 * tightness (tightMin..tightMax of yawMul x the uncalmed base yaw). While a power slide holds the
 * throttle, the brake does not brake: the engine loses only drift.brakeDragMps2 (the engine's brake is
 * given back here, after kart.js ran it). Charge is held time times the soft-drift modifier (into the
 * drift x softInMul, counter-steering x softOutMul), on the ground only. Release pays: letting go of
 * hop (or of the brake, for a power slide) or straightening a power slide (stick under
 * drift.straightenMax for drift.straightenS) fires the reached tier, and any drift that charged
 * drift.miniMinS pays at least the mini-turbo (tier 0). Cancels (no pay): slip under cancelSlipDeg for
 * cancelSlipS, speed under minSpeed, wall, stall, drop. Events: drift-start { dir, via }, drift-tier
 * { tier, dir }, drift-release { tier }, drift-boost { tier, dir } (every paid release), drift-cancel
 * { reason }. skidOf(st) is the per-tick skid flag (the rear slides: drifting and grounded).
 * The beat release bonus is off in the slice (drift.beatBonusOn false).
 */
import { S, BOOST } from './state.js';
import { lerp, clamp } from '../core/math.js';
import { gap } from './gaps.js';
import { applyBoost } from './boost.js';
import { tickTrick } from './trick.js';

// per-kart drift extras beside the frozen state layout, keyed by the state array
const SIDE = new WeakMap();
function side(st) {
  let r = SIDE.get(st);
  if (!r) SIDE.set(st, (r = { brake: false, brakeOnS: 0, straightT: 0, prevE: 0 }));   // brakeOnS: seconds the brake has been on, 0 off
  return r;
}

/** 1 while the rear slides (drifting on the ground), else 0: the skid mark and spark flag. */
export const skidOf = (st) => (st[S.driftDir] !== 0 && st[S.grounded] > 0 ? 1 : 0);
/** True while the running drift is a power slide (brake held). */
export const isPowerSlide = (st) => st[S.driftDir] !== 0 && side(st).brake;

export function startDrift(st, dir, events, via = 'hop') {
  st[S.driftDir] = dir; st[S.driftStage] = 0; st[S.driftCharge] = 0; st[S.slipLowT] = 0;
  const r = side(st);
  r.brake = via === 'brake'; r.straightT = 0; r.prevE = st[S.engineV];
  events.emit('drift-start', { dir, via });
}

/** Start a hop drift if the stick and speed allow it. @returns {boolean} */
export function maybeStartDrift(st, a, feel, speed, events) {
  if (st[S.driftDir] || Math.abs(a.steer) <= gap(feel, 'drift.steerMin') || speed <= feel.get('drift.minSpeedMps')) return false;
  startDrift(st, Math.sign(a.steer), events, 'hop');
  return true;
}

/** Start a power slide: throttle held, a fresh brake press, steering, fast, grounded. @returns {boolean} */
function maybePowerSlide(st, a, feel, speed, grounded, events, r) {
  if (!grounded || st[S.airborne] > 0 || st[S.stalled] > 0) return false;
  if (a.brake < feel.get('drift.brakeMin') || a.throttle < feel.get('drift.brakeThrottleMin')) return false;
  if (r.brakeOnS > feel.get('drift.brakeEntryS')) return false;
  if (Math.abs(a.steer) <= gap(feel, 'drift.steerMin') || speed <= feel.get('drift.minSpeedMps')) return false;
  startDrift(st, Math.sign(a.steer), events, 'brake');
  return true;
}

export function cancelDrift(st, reason, events) {
  if (!st[S.driftDir]) return;
  clearDrift(st);
  events.emit('drift-cancel', { reason });
}

function clearDrift(st) {
  st[S.driftDir] = 0; st[S.driftStage] = 0; st[S.driftCharge] = 0; st[S.slipLowT] = 0;
  const r = SIDE.get(st);
  if (r) { r.brake = false; r.straightT = 0; }
}

/** Pay the drift: its tier, or the mini-turbo when it charged drift.miniMinS. */
function release(st, feel, events) {
  const tier = st[S.driftStage] | 0, charge = st[S.driftCharge], dir = st[S.driftDir];
  clearDrift(st);
  events.emit('drift-release', { tier });
  if (tier > 0 || charge >= feel.get('drift.miniMinS')) {
    applyBoost(st, BOOST.DRIFT1 + Math.max(0, tier - 1), tier, feel, events);
    events.emit('drift-boost', { tier, dir });
  }
}

/** Drift yaw rate (rad/s, + right) from the slewed stick. */
export function driftYaw(st, steer, feel, gripYaw) {
  const dir = st[S.driftDir];
  const tight = lerp(feel.get('drift.tightMin'), feel.get('drift.tightMax'), (clamp(steer * dir, -1, 1) + 1) / 2);
  return dir * feel.get('drift.yawMul') * gripYaw * tight;   // gripYaw here = the uncalmed base law (sim/steer.js)
}

/** One tick: the trick clock, a power slide start, then a running drift's release, cancels, charge and tiers. */
export function updateDrift(st, a, feel, dt, events, grounded, speed, slipDeg) {
  tickTrick(st, a, feel, dt, events, grounded);
  const r = side(st);
  r.brakeOnS = a.brake > 0.05 ? r.brakeOnS + dt : 0;
  if (!st[S.driftDir] && !maybePowerSlide(st, a, feel, speed, grounded, events, r)) return;
  const dir = st[S.driftDir];
  const held = a.hop || (r.brake && a.brake >= feel.get('drift.brakeHoldMin'));
  if (r.brake && !a.hop) r.straightT = Math.abs(a.steer) < feel.get('drift.straightenMax') ? r.straightT + dt : 0;
  if (!held || (r.brake && !a.hop && r.straightT >= feel.get('drift.straightenS'))) { release(st, feel, events); return; }
  // a power slide with the throttle held: give back the brake kart.js took, run the throttle it skipped
  // (the engine's band ramp), keep a light drag
  if (r.brake && a.brake > 0 && a.brake >= a.throttle && a.throttle >= feel.get('drift.brakeThrottleMin') && st[S.engineV] > 0) {
    const top = feel.get('speed.topMps');
    let e = st[S.engineV];
    e = Math.min(Math.max(e, r.prevE), e + feel.get('engine.brakeMps2') * a.brake * dt);
    if (e < top) {
      const g = feel.get('engine.bandGains'), band = clamp(Math.floor(e / top * g.length), 0, g.length - 1);
      e = Math.min(top, e + g[band] * top * a.throttle * dt);
    }
    st[S.engineV] = Math.max(0, e - feel.get('drift.brakeDragMps2') * dt);
  }
  r.prevE = st[S.engineV];
  if (st[S.stalled] > 0) return cancelDrift(st, 'stall', events);
  if (!grounded) return;
  if (speed < feel.get('drift.minSpeedMps')) return cancelDrift(st, 'slow', events);
  if (slipDeg < feel.get('drift.cancelSlipDeg')) {
    st[S.slipLowT] += dt;
    // a slide that straightened out by itself after charging pays like a release (every completed drift of at
    // least drift.miniMinS pays something); a short one still cancels
    if (st[S.slipLowT] >= feel.get('drift.cancelSlipS')) {
      return st[S.driftCharge] >= feel.get('drift.miniMinS') ? release(st, feel, events) : cancelDrift(st, 'slip', events);
    }
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
