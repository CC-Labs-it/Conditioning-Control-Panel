/* render/kartPose.js - the three-free pose law for every kart, so tests run it under node.
 *
 * Per kart and frame: lean into the turn (roll from yaw rate x speed, plus a drift lean), a small nose
 * lift while boosting, squash (the player's comes from FxState, a rival's from its own hop and land
 * events), a hit wobble (a damped sway, gentler and slower under Gentle Visuals, never a flicker), the
 * antenna sway from lateral acceleration and EMI's arms going up while a boost runs.
 * Plus the small helpers the render lane tests: tint mix, name tag fade, spark ladder, mote density.
 */
import { S, BOOST } from '../sim/state.js';
import { clamp, smoothstep } from '../core/math.js';

export function createPose() {
  return { roll: 0, lean: 0, pitch: 0, yawAdd: 0, sq: 0, sqPeak: 0, sqT: -1, wobT: -1, wobAmp: 0, wobHz: 0, ant: 0, arms: 0 };
}

const ease = (v, to, tc, dt) => v + (to - v) * (1 - Math.exp(-dt / Math.max(tc, 1e-4)));

/** Lean target in radians about the kart's +Z (back) axis: turning right (yawRate +) rolls the right side down. */
export function leanTarget(yawRate, speedPct, driftDir, L) {
  const turn = clamp(-yawRate * clamp(speedPct, 0, 1) * L.leanPerRadps, -L.leanMaxRad, L.leanMaxRad);
  return turn - Math.sign(driftDir || 0) * L.driftLeanRad;
}

/** A hit wobble: amplitude and rate drop under Gentle, so it reads as a sway, never a shake. */
export function kickWobble(pose, L, gentle, mul = 1) {
  pose.wobT = 0;
  pose.wobAmp = L.wobbleRad * mul * (gentle ? L.wobbleGentle : 1);
  pose.wobHz = L.wobbleHz * (gentle ? 0.5 : 1);
}

/** The wobble value at time t; 0 once the decay is under 2%. */
export function wobbleAt(t, amp, hz, tcS) {
  if (t < 0) return 0;
  const k = Math.exp(-t / tcS);
  return k < 0.02 ? 0 : amp * k * Math.sin(2 * Math.PI * hz * t);
}

/** A rival's landing or hop squash: peak at once, gone over goneS. */
export function kickSquash(pose, peak) { pose.sqPeak = Math.max(peak, pose.sqT >= 0 ? pose.sq : 0); pose.sqT = 0; }

/**
 * One frame. st = interpolated KartState, opts.squash = the player's fx.squash (null for a rival),
 * opts.spinRad = fxv.spinRad (Shatter, visual only). Writes roll, pitch, yawAdd, sq, ant, arms.
 */
export function stepPose(pose, st, dt, L, { topMps = 30, gentle = false, squash = null, spinRad = 0 } = {}) {
  pose.lean = ease(pose.lean, leanTarget(st[S.yawRate], st[S.speedPct], st[S.driftDir], L), L.leanTcS, dt);
  const boosting = st[S.boostKind] !== BOOST.NONE;
  pose.pitch = ease(pose.pitch, boosting && st[S.grounded] > 0 ? L.boostPitchRad : 0, L.pitchTcS, dt);
  let wob = 0;
  if (pose.wobT >= 0) {
    pose.wobT += dt;
    wob = wobbleAt(pose.wobT, pose.wobAmp, pose.wobHz, L.wobbleTcS);
    if (wob === 0 && pose.wobT > L.wobbleTcS) pose.wobT = -1;
  }
  pose.roll = pose.lean + wob;
  pose.yawAdd = wob * 0.5 + (spinRad || 0);
  if (squash !== null) pose.sq = squash;
  else if (pose.sqT >= 0) {
    pose.sqT += dt;
    const k = 1 - pose.sqT / L.botSquashGoneS;
    pose.sq = k > 0 ? pose.sqPeak * k * k : 0;
    if (k <= 0) pose.sqT = -1;
  } else pose.sq = 0;
  // antenna: lateral acceleration (yaw rate x speed) sways it outward, a squash pushes it down
  const latAcc = st[S.yawRate] * st[S.speedPct] * topMps;
  const antTo = clamp(latAcc * L.antSwayPerMps2, -L.antSwayMaxRad, L.antSwayMaxRad);
  pose.ant = ease(pose.ant, antTo, L.antTcS, dt);
  pose.arms = ease(pose.arms, boosting ? L.armsUpRad : 0, L.armsTcS, dt);
  return pose;
}

/** Kart tint i as linear-ish 0..1 rgb: white mixed toward LOOK.kartTints[i] by kartTintMix (i 0 = white). */
export function tintRgb(i, L) {
  const hex = L.kartTints[((i % L.kartTints.length) + L.kartTints.length) % L.kartTints.length];
  const n = parseInt(hex.slice(1), 16), m = i === 0 ? 0 : L.kartTintMix;
  return [(n >> 16) & 255, (n >> 8) & 255, n & 255].map((c) => 1 - m + m * c / 255);
}

/** Name tag opacity: full inside tagNearM - tagFadeM, gone past tagNearM. */
export function tagAlpha(distM, L) {
  return 1 - smoothstep(L.tagNearM - L.tagFadeM, L.tagNearM, distM);
}

/** The spark ladder per drift tier 1..3: rate per second, size, petal share; mul scales the rate (rivals, LOD). */
export function sparkProfile(tier, L, mul = 1) {
  const i = clamp(tier | 0, 1, 3) - 1;
  return { rate: L.sparkRateByTier[i] * mul, size: L.sparkSizeByTier[i], petal: L.sparkPetalByTier[i] };
}

/** Ambient mote share 0..1 from the deep palette weight on screen; Gentle thins them. */
export function moteDensity(deepW, L, gentle) {
  return (L.moteBase + (L.moteDeep - L.moteBase) * clamp(deepW, 0, 1)) * (gentle ? L.moteGentle : 1);
}

/** Bump spark count from the closing speed dv (m/s), between bumpSparks[0] and [1]. */
export function bumpCount(dv, L) {
  return Math.round(L.bumpSparks[0] + (L.bumpSparks[1] - L.bumpSparks[0]) * clamp(dv / 8, 0, 1));
}
