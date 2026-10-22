/* input/steerCurve.js - the steering response curve for human input (FEEL.md "input"). Pure, three-free.
 *
 * A source tags its steer with `steerKind`: 'key' (digital), 'stick' (gamepad) or 'touch'. The action
 * map (core/actions.js) shapes each tagged source before the sources are merged:
 * - key: a held key BUILDS instead of snapping: it starts at input.keyStart and ramps to full over
 *   input.keyRampS; a release or a change of side starts over. A tap is a small correction.
 * - stick / touch: expo (input.stickExpo / input.touchExpo): fine near centre, full at the edge.
 * Never mirrors or inverts (owner law: left stays left); sign in = sign out. Bots never pass here.
 */

/** Slice defaults; feel.json wins when a feel is given (read through feel first). */
export const INPUT_DEFAULTS = Object.freeze({ keyStart: 0.35, keyRampS: 0.3, stickExpo: 0.35, touchExpo: 0.2 });

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

/** Key ramp value after holding one side for heldS seconds (0 when nothing is held). */
export function keyRampValue(heldS, start, rampS) {
  if (!(heldS >= 0)) return 0;
  const t = rampS > 0 ? Math.min(1, heldS / rampS) : 1;
  return start + (1 - start) * t;
}

/** One digital source's ramp state. step(raw, dtS, start, rampS) -> shaped steer. */
export function createKeyRamp() {
  let side = 0, held = 0;
  return {
    step(raw, dtS, start, rampS) {
      const s = raw > 0 ? 1 : raw < 0 ? -1 : 0;
      if (s !== side) { side = s; held = 0; } else if (s) held += Math.max(0, dtS);
      return s ? s * keyRampValue(held, start, rampS) : 0;
    },
    reset() { side = 0; held = 0; },
  };
}

/** Shape one source's steer by its kind. ramp is that source's createKeyRamp() (keys only). */
export function shapeSteer(raw, kind, dtS, feel, ramp) {
  if (kind === 'key') return ramp ? ramp.step(raw, dtS, inputNum(feel, 'keyStart'), inputNum(feel, 'keyRampS')) : raw;
  if (kind === 'stick') return expo(raw, inputNum(feel, 'stickExpo'));
  if (kind === 'touch') return expo(raw, inputNum(feel, 'touchExpo'));
  return raw;
}
