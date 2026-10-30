/* render/rampLook.js - the jump ramp look numbers and the floating item bob (pure, three-free). */
export const RAMP_LOOK = Object.freeze({
  /** how far the dark palette colour lifts toward the primary on a lit face, and the lip-ward glow */
  dark: 0.25, lipGlow: 0.55, chevronGain: 0.85,
  /** floating item boxes: extra bob along the road normal (m) and its rate (Hz); Gentle holds them still */
  floatBobM: 0.22, floatBobHz: 0.45,
});

/** The extra bob (m) of a lifted item box at time t; 0 under Gentle / reduced motion. */
export function floatingBob(t, i = 0, still = false) {
  return still ? 0 : Math.sin((t * RAMP_LOOK.floatBobHz + i * 0.37) * Math.PI * 2) * RAMP_LOOK.floatBobM;
}
