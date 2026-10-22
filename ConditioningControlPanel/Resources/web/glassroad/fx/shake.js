/* fx/shake.js - the camera shake law (plan 6).
 *
 * One kick is a run of parabolas f(t) = a*t - b*t^2 over shake.periodS, so each lobe
 * starts and ends at rest and peaks at its amplitude (a = 4*amp/T, b = a/T). Each lobe
 * flips sign and is shake.repeatMul of the last; the run stops once a lobe would be under
 * shake.stopBelowM. Hits shake the vertical axis, boosts the forward axis (-Z, camera
 * local). Kicks add. Under Gentle nothing shakes.
 */

const AXIS = { hit: [0, 1, 0], boost: [0, 0, -1] };

/** One lobe value at t in [0, T] for peak amp. */
export function lobe(amp, t, T) {
  const a = 4 * amp / T, b = a / T;
  return a * t - b * t * t;
}

/** @returns {{kick(kind: 'hit'|'boost'): void, update(dt: number): number[], reset(): void}} */
export function createShake(feel) {
  const runs = [];      // { axis, amp, sign, t }
  const out = [0, 0, 0];

  function kick(kind) {
    if (feel.get('gentle.on')) return;
    const axis = AXIS[kind];
    if (!axis) return;
    const amp = feel.get(kind === 'hit' ? 'shake.hitAmpM' : 'shake.boostAmpM');
    if (amp >= feel.get('shake.stopBelowM')) runs.push({ axis, amp, sign: 1, t: 0 });
  }

  function update(dt) {
    out[0] = out[1] = out[2] = 0;
    if (feel.get('gentle.on')) { runs.length = 0; return out; }
    const T = feel.get('shake.periodS'), mul = feel.get('shake.repeatMul'), stop = feel.get('shake.stopBelowM');
    for (let i = runs.length - 1; i >= 0; i--) {
      const r = runs[i];
      r.t += dt;
      while (r.t >= T) {
        r.t -= T; r.amp *= mul; r.sign = -r.sign;
        if (r.amp < stop) break;
      }
      if (r.amp < stop) { runs.splice(i, 1); continue; }
      const v = r.sign * lobe(r.amp, r.t, T);
      out[0] += r.axis[0] * v; out[1] += r.axis[1] * v; out[2] += r.axis[2] * v;
    }
    return out;
  }

  return { kick, update, reset() { runs.length = 0; out[0] = out[1] = out[2] = 0; } };
}
