/* fx/trauma.js - one trauma value drives every screen-side shake (feel wave 2026-10-26, Lane A).
 *
 * Vlambeer / Eiserloh: hits ADD trauma (0..1, clamped), it decays linearly at trauma.decayPerS, and what the
 * channels read is out = trauma^2 * motionMul, so small knocks barely move and big ones bite. The shaking itself
 * is smooth noise, ONE fixed seed per axis (never Math.random per frame), so it never strobes.
 * The chase camera pose NEVER reads this (render/cameraMath.js compose() reads only fx.orbit); the channels are
 * the screen border, gamepad rumble, the audio duck, post chromatic aberration and the kart wobble.
 * motionMul: the Options Motion row (full 1, less trauma.lessMul, off 0); Gentle preview forces 'less'.
 */

/** Motion level -> multiplier (unknown -> full). */
export function motionMulOf(level, lessMul = 0.45) {
  return level === 'off' ? 0 : level === 'less' ? lessMul : 1;
}

// integer hash -> [-1, 1]; seeds are fixed per axis so a replay shakes the same way
function hash(i, seed) {
  let h = (Math.imul(i | 0, 374761393) + Math.imul(seed | 0, 668265263)) | 0;
  h = Math.imul(h ^ (h >>> 13), 1274126177);
  h ^= h >>> 16;
  return ((h >>> 0) / 4294967295) * 2 - 1;
}
const SEEDS = [17, 4099, 92821, 300007];

/** Smooth 1D gradient noise on axis (0..3) at time t (in noise periods): [-1, 1], 0 at every integer t. */
export function noise(axis, t) {
  const seed = SEEDS[(axis | 0) & 3], i = Math.floor(t), f = t - i;
  const g0 = hash(i, seed), g1 = hash(i + 1, seed);
  const u = f * f * (3 - 2 * f);
  // gradient form: g * distance, blended; * 2 brings the peak near +-1
  return Math.max(-1, Math.min(1, 2 * (g0 * f * (1 - u) + g1 * (f - 1) * u)));
}

/** @returns {{add(amount: number): void, step(dt: number): number, value: number, out: number, motionMul: number, setMotion(level: string): void, reset(): void}} */
export function createTrauma(feel) {
  const get = (k, d) => { try { const v = feel?.get?.("trauma." + k); return Number.isFinite(v) ? v : d; } catch (e) { return d; } };
  let value = 0, mul = 1, level = 'full';
  return {
    add(amount) { if (amount > 0) value = Math.min(1, value + amount); },
    step(dt) { if (dt > 0) value = Math.max(0, value - get('decayPerS', 1.6) * dt); return value; },
    get value() { return value; },
    get out() { return value * value * mul; },
    get motionMul() { return mul; },
    get level() { return level; },
    setMotion(l) { level = l === 'off' || l === 'less' ? l : 'full'; mul = motionMulOf(level, get('lessMul', 0.45)); },
    reset() { value = 0; },
  };
}


/**
 * The finish slow frame on the drawn pose (boot.js). While renderRate < 1 the picture falls behind the sim by
 * lagS += (1 - rate) * dt (0.4x for 0.5 s = 0.3 s behind); once the rate is back to 1 the lag closes over
 * catchS (exponential). Each frame record() keeps the views' positions with the frame's renderTime, then apply()
 * draws each view at renderTime - lagS (linear between records). The sim, the clock and renderTime never change.
 */
export function createPoseLag({ X = 0, keepS = 1.2, catchS = 0.3 } = {}) {
  const ring = [];   // { t, p: number[] }
  let lagS = 0;
  return {
    get lagS() { return lagS; },
    step(dt, rate) {
      if (!(dt > 0)) return lagS;
      if (rate < 1) lagS += (1 - rate) * dt;
      else if (lagS > 0) { lagS *= Math.exp(-dt / catchS); if (lagS < 1e-3) lagS = 0; }
      return lagS;
    },
    record(t, views) {
      const p = [];
      for (const v of views) p.push(v ? v[X] : 0, v ? v[X + 1] : 0, v ? v[X + 2] : 0);
      ring.push({ t, p });
      while (ring.length > 2 && ring[0].t < t - keepS) ring.shift();
      if (lagS === 0) return;
      const want = t - lagS;
      let i = ring.length - 1;
      while (i > 0 && ring[i - 1].t > want) i--;
      const b = ring[i], a = ring[Math.max(0, i - 1)];
      const k = b.t > a.t ? Math.min(1, Math.max(0, (want - a.t) / (b.t - a.t))) : 1;
      for (let n = 0; n < views.length; n++) {
        const v = views[n];
        if (!v || a.p.length <= n * 3 + 2) continue;
        for (let c = 0; c < 3; c++) v[X + c] = a.p[n * 3 + c] + (b.p[n * 3 + c] - a.p[n * 3 + c]) * k;
      }
    },
    reset() { ring.length = 0; lagS = 0; },
  };
}
