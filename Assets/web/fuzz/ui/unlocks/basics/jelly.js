/* ui/unlocks/basics/jelly.js - the elastic feel of the bouncing buttons (owner 2026-11-20: "elasticity and bounce").
 * A damped spring per copy: a kick squashes it, it overshoots back a few times and settles. Pure: no DOM, no clock;
 * the caller steps it with its own frame time. Displacement s > 0 is squashed along the hit axis, s < 0 stretched. */

/** Wall spring: about four wobbles a second, light damping so two or three overshoots show before it settles. */
export const JELLY_WALL = Object.freeze({ hz: 4.2, zeta: 0.2 });
/** Press spring: a little quicker and a little calmer, so a press reads as a squeeze and a boing, not a wobble. */
export const JELLY_PRESS = Object.freeze({ hz: 5.5, zeta: 0.26 });
/** Kicks in displacement per second. Plain is every copy; pop is Wall Pop or an EMI copy (deeper, near the old Breakout squash). */
export const WALL_KICK = Object.freeze({ plain: 5.5, pop: 12 });
export const PRESS_KICK = 4.5;

const MAX_S = 0.45, MAX_V = 18, SUB = 1 / 120;

export function createJelly({ hz, zeta } = JELLY_WALL) {
  const w = 2 * Math.PI * hz, k = w * w, c = 2 * zeta * w;
  let s = 0, v = 0;
  return {
    /** Add a squash impulse (stacks with a wobble already running, capped). */
    kick(impulse) { v = Math.max(-MAX_V, Math.min(MAX_V, v + impulse)); },
    /** Advance by dt seconds; answers the displacement (0 once settled). */
    step(dt) {
      if (!s && !v) return 0;
      let left = Math.min(Math.max(dt || 0, 0), 0.1);
      while (left > 0) {
        const h = Math.min(SUB, left);
        v += (-k * s - c * v) * h;
        s = Math.max(-MAX_S, Math.min(MAX_S, s + v * h));
        left -= h;
      }
      if (Math.abs(s) < 1e-3 && Math.abs(v) < 0.02) s = v = 0;
      return s;
    },
    moving() { return s !== 0 || v !== 0; },
    reset() { s = v = 0; },
    get value() { return s; },
  };
}

/** Scale along and across the hit axis for a displacement: flattens against the wall and bulges sideways, about volume-kept. */
export function jellyShape(s) {
  return { along: 1 - s, across: 1 + s * 0.65 };
}
