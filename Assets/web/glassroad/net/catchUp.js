/* net/catchUp.js - online, the kart sim keeps the room's clock (NET.md section 2). core/clock.js drops time past
 * maxSubsteps on a long frame (solo: never caught up, so a hitch is not a teleport). Online that dropped time was a
 * late start for good: a phone that hitched at GO (shader or decode work) started about a second behind the room,
 * and one under 30 fps fell further behind every frame. A frame that finds the sim behind the room's race time runs
 * extra fixed steps, a few per frame, until it is level. Pure.
 */

/** Extra sim steps this frame: what the sim owes the room past the clock's own steps, at most capSteps.
 * behindS = room race seconds minus simulated race seconds, steps = the clock's own steps this frame. */
export const CATCH_UP_STEPS = 12;
export function catchUpSteps(behindS, steps, dt, capSteps = CATCH_UP_STEPS) {
  if (!(behindS > 0) || !(dt > 0)) return 0;
  const owed = Math.floor((behindS + 1e-6) / dt) - steps;
  return owed > 0 ? Math.min(capSteps, owed) : 0;
}
