/* hud/hudPulse.js - feel wave (2026-10-26, lane D): the HUD's two confirmations as Web Animations keyframes.
 *   hit  the item chip answers 300 ms (hud.hitterFlashMs) when the local racer lands a hit (hitter-hit, hitter 0)
 *   lap  the lap chip pulses on every lap (hud.lapPulseMs)
 * Each keyframe list goes IN and back OUT (nothing snaps). motion 'less' or Gentle keep a soft brightness swell with
 * no scale; motion 'off' plays nothing. One swell per event, never a strobe, never past the chip.
 */
const FULL = {
  hit: [{ transform: 'scale(1)', filter: 'brightness(1)' }, { transform: 'scale(1.14)', filter: 'brightness(1.7)', offset: 0.3 },
    { transform: 'scale(1)', filter: 'brightness(1)' }],
  lap: [{ transform: 'scale(1)' }, { transform: 'scale(1.16)', offset: 0.35 }, { transform: 'scale(1)' }],
};
const SOFT = {
  hit: [{ filter: 'brightness(1)' }, { filter: 'brightness(1.35)', offset: 0.4 }, { filter: 'brightness(1)' }],
  lap: [{ opacity: 1 }, { opacity: 0.7, offset: 0.4 }, { opacity: 1 }],
};

/** Keyframes for kind 'hit' | 'lap' at a motion level, or null when nothing plays. */
export function pulseFrames(kind, motion = 'full', gentle = false) {
  if (motion === 'off' || !FULL[kind]) return null;
  return (motion === 'less' || gentle ? SOFT : FULL)[kind];
}

/** Play one pulse on node (Web Animations; a DOM without animate() plays nothing). @returns the Animation or null */
export function playPulse(node, kind, ms, motion, gentle) {
  const frames = pulseFrames(kind, motion, gentle);
  if (!frames || !node || typeof node.animate !== 'function' || !(ms > 0)) return null;
  return node.animate(frames, { duration: ms, easing: 'cubic-bezier(.2,.8,.3,1)' });
}
