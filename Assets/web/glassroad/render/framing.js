/* Shared racer proportions and screen composition for the chase rig and its preview checks.
 * Owner 2026-10-31: every racer 15% larger (0.78 x 1.15); the sim's kart sizes follow (feel.json contact.radiusM /
 * maxHeightM, sim/gaps.js kart.radiusM, via viewMath.js scaledRadius), and screenY lifts the kart to keep it in frame. */
export const RACER_GROWTH = 1.15;
export const RACER_FRAMING = Object.freeze({
  scale: 0.897,
  centerUpM: 0.55,
  screenY: 0.60,
  airScreenMax: 0.035,
});
