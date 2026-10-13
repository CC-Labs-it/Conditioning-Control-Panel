/* ============================================================================
 * fallPace.js - the fall gathers pace on its own (owner, 2026-10-12).
 *
 * Two numbers on top of the director's target speed (engine/fallNav.js):
 *   the ramp - up to RAMP_MAX faster, eased in over RAMP_SEC of falling (paused
 *              time does not count); slow enough that it is felt, not seen;
 *   the rush - the dive down a committed junction tube runs VEIN_RUSH times
 *              the fall speed.
 * Pure, node tested.
 * ==========================================================================*/

export const RAMP_SEC = 360;
export const RAMP_MAX = 0.2;
export const VEIN_RUSH = 1.5;

/** The pace multiplier after `sec` seconds of falling: 1 at the start, 1 + RAMP_MAX at the end. */
export function fallRamp(sec) {
  const u = Math.max(0, Math.min(1, (sec || 0) / RAMP_SEC));
  return 1 + RAMP_MAX * u * u * (3 - 2 * u);
}
