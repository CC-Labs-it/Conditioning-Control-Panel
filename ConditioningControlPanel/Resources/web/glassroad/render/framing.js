/* Shared racer proportions and screen composition for the chase rig and its preview checks.
 * Interaction wave (owner 2026-10-30): every racer 8% larger (0.78 x 1.08); the sim's kart sizes follow
 * (feel.json contact.radiusM / maxHeightM, sim/gaps.js kart.radiusM, via viewMath.js scaledRadius). */
export const RACER_GROWTH = 1.08;
export const RACER_FRAMING = Object.freeze({
  scale: 0.8424,
  centerUpM: 0.55,
  screenY: 0.62,
  airScreenMax: 0.035,
});
