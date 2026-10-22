/* sim/steer.js - the grip yaw law (FEEL.md "steer"), three-free and shared by the kart and the bots.
 *
 * Base law (Phase A): rate / (1 + v / yawSpeedRefMps). Polish 2 lays a speed calm over it so the kart
 * reads calm and precise at speed and stays nimble when slow: full base law up to steer.calmFromMps,
 * easing to steer.cruiseYawMul at steer.cruiseMps, steer.topYawMul at steer.topCalmMps and
 * steer.boostYawMul at steer.boostCalmMps (boosted) and above. steer.userMul is the player's steering
 * sensitivity (Options, 0.6 to 1.4); bots hold it at 1 (sim/racers.js).
 * Drift yaw keeps the uncalmed base law (sim/drift.js), so a drift is the strong turn.
 * The bots solve corner speeds many times a tick, so the law reads feel once into a plain record
 * (yawParams) and the math runs on that.
 */

const DEG = Math.PI / 180;
const smooth = (t) => (t <= 0 ? 0 : t >= 1 ? 1 : t * t * (3 - 2 * t));
const ease = (s, a, b, ma, mb) => ma + (mb - ma) * smooth((s - a) / Math.max(1e-6, b - a));

/** Every steering-law number, read once. @returns {object} out */
export function yawParams(feel, out = {}) {
  const g = (k) => feel.get('steer.' + k);
  out.rate = g('yawRateDegps') * DEG; out.ref = g('yawSpeedRefMps');
  out.v0 = g('calmFromMps'); out.v1 = g('cruiseMps'); out.v2 = g('topCalmMps'); out.v3 = g('boostCalmMps');
  out.m1 = g('cruiseYawMul'); out.m2 = g('topYawMul'); out.m3 = g('boostYawMul');
  out.user = g('userMul');
  return out;
}

/** Speed calm multiplier at |v| m/s from a yawParams record. */
export function calmOf(v, p) {
  const s = Math.abs(v);
  if (s <= p.v0) return 1;
  if (s <= p.v1) return ease(s, p.v0, p.v1, 1, p.m1);
  if (s <= p.v2) return ease(s, p.v1, p.v2, p.m1, p.m2);
  return ease(s, p.v2, p.v3, p.m2, p.m3);
}

/** Base yaw rate at |v| m/s and full lock, rad/s (no calm, no sensitivity). */
export const baseOf = (v, p) => p.rate / (1 + Math.abs(v) / p.ref);
/** Grip yaw rate at |v| m/s and full lock, rad/s: base x calm x userMul. */
export const gripOf = (v, p) => baseOf(v, p) * calmOf(v, p) * p.user;

const P = {};
/** Base yaw at |v| m/s, full lock, rad/s. */
export function baseYaw(v, feel) {
  return feel.get('steer.yawRateDegps') * DEG / (1 + Math.abs(v) / feel.get('steer.yawSpeedRefMps'));
}
/** Speed calm multiplier at |v| m/s. */
export function calmMul(v, feel) { return calmOf(v, yawParams(feel, P)); }
/** Grip yaw at |v| m/s, full lock, rad/s. */
export function gripYaw(v, feel) { return gripOf(v, yawParams(feel, P)); }

/**
 * Fastest speed (m/s) a grip turn holds on a bend of kDeg deg/m with `share` of the full-lock yaw:
 * where k * v meets share * gripYaw(v). k * v rises and the yaw falls with speed, so the crossing is
 * unique; bisection over 0..maxV. `p` is a yawParams record (or a feel, read once here).
 */
export function bendSpeed(kDeg, p, share = 1, maxV = 80) {
  if (p && typeof p.get === 'function') p = yawParams(p, {});
  const k = Math.abs(kDeg) * DEG;
  if (k < 1e-5) return Infinity;
  if (k * maxV <= share * gripOf(maxV, p)) return Infinity;
  let lo = 0, hi = maxV;
  for (let i = 0; i < 20; i++) {
    const m = (lo + hi) / 2;
    if (k * m <= share * gripOf(m, p)) lo = m; else hi = m;
  }
  return lo;
}
