/* input/steerCurve.js - the steering response curve for human input (FEEL.md "input"). Pure, three-free.
 *
 * A source tags its steer with `steerKind`: 'key' (digital), 'stick' (gamepad) or 'touch'. The action
 * map (core/actions.js) shapes each tagged source before the sources are merged:
 * - key: a held key BUILDS instead of snapping: it starts at input.keyStart and ramps to full over
 *   input.keyRampS on an ease-in (input.keyRampPow, 1 = linear). A fresh tap starts small. Reversing
 *   a committed hold keeps a bounded part of the ramp, so counter-steering does not start from a tap.
 *   A short neutral crossover is allowed; releasing or tapping the same side starts over.
 * - stick / touch: a power curve |x|^p (input.stickPow / input.touchPow): gentle near centre, full at
 *   the edge. expo() stays for callers that want the Phase A blend.
 * Never mirrors or inverts (owner law: left stays left); sign in = sign out. Bots never pass here.
 */

/** Slice defaults; feel.json wins when a feel is given (read through feel first). */
export const INPUT_DEFAULTS = Object.freeze({ keyStart: 0.12, keyRampS: 0.4, keyRampPow: 1.6, stickPow: 2, touchPow: 1.8 });

export const inputNum = (feel, k) => {
  if (feel) { try { const v = feel.get('input.' + k); if (typeof v === 'number') return v; } catch (e) { /* not in this feel */ } }
  return INPUT_DEFAULTS[k];
};

const clamp1 = (v) => (v < -1 ? -1 : v > 1 ? 1 : v);

/** Expo curve: (1 - e) x + e x^3, odd, 0 -> 0 and 1 -> 1, slope (1 - e) at centre. */
export function expo(x, e) {
  const v = clamp1(+x || 0), k = Math.max(0, Math.min(1, +e || 0));
  return (1 - k) * v + k * v * v * v;
}

/** Power curve sign(x) |x|^p, p clamped to 1..4: odd, 0 -> 0 and 1 -> 1, flat at centre for p > 1. */
export function powCurve(x, p) {
  const v = clamp1(+x || 0), k = Math.max(1, Math.min(4, +p || 1));
  return v < 0 ? -Math.pow(-v, k) : Math.pow(v, k);
}

/** Key ramp value after holding one side for heldS seconds (0 when nothing is held); pow eases it in. */
export function keyRampValue(heldS, start, rampS, pow = 1) {
  if (!(heldS >= 0)) return 0;
  const t = rampS > 0 ? Math.min(1, heldS / rampS) : 1;
  return start + (1 - start) * (pow === 1 ? t : Math.pow(t, Math.max(0.5, pow)));
}

// A deliberate hold can carry at most 45% of its ramp into the opposite side. Commitment uses real
// held time, never inherited ramp time: alternating taps cannot build full lock. The short crossover
// covers the gap between lifting one key and pressing the other, while neutral still outputs zero.
const REVERSE = Object.freeze({ commitS: 0.18, carry: 0.45, crossoverS: 0.08 });

/** One digital source's ramp state. step(raw, dtS, start, rampS) -> shaped steer. */
export function createKeyRamp() {
  let side = 0, held = 0, pressed = 0, releasedSide = 0, releasedHeld = 0, releasedAge = 0;
  return {
    step(raw, dtS, start, rampS, pow = 1) {
      const s = raw > 0 ? 1 : raw < 0 ? -1 : 0, dt = Math.max(0, dtS);
      if (!s) {
        if (side) {
          releasedSide = pressed >= REVERSE.commitS ? side : 0;
          releasedHeld = held; releasedAge = 0;
        }
        releasedAge += dt;
        side = 0; held = 0; pressed = 0;
        return 0;
      }
      if (s !== side) {
        const reverse = side ? s === -side && pressed >= REVERSE.commitS
          : s === -releasedSide && releasedAge + dt <= REVERSE.crossoverS;
        const carry = side ? held : releasedHeld;
        held = reverse ? Math.min(carry, Math.max(0, rampS) * REVERSE.carry) : 0;
        side = s; pressed = 0; releasedSide = 0; releasedHeld = 0; releasedAge = 0;
      } else { held += dt; pressed += dt; }
      return s * keyRampValue(held, start, rampS, pow);
    },
    reset() { side = 0; held = 0; pressed = 0; releasedSide = 0; releasedHeld = 0; releasedAge = 0; },
  };
}

/** Shape one source's steer by its kind. ramp is that source's createKeyRamp() (keys only). */
export function shapeSteer(raw, kind, dtS, feel, ramp) {
  if (kind === 'key') return ramp ? ramp.step(raw, dtS, inputNum(feel, 'keyStart'), inputNum(feel, 'keyRampS'), inputNum(feel, 'keyRampPow')) : raw;
  if (kind === 'stick') return powCurve(raw, inputNum(feel, 'stickPow'));
  if (kind === 'touch') return powCurve(raw, inputNum(feel, 'touchPow'));
  return raw;
}
