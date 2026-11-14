/* preview/frame.js - framing and turntable law for the preview box (lane B3-W5). Pure numbers, no three. */

/** The resting yaw: a three-quarter view, also the whole pose when motion is off. */
export const HOME_YAW = -0.55;
/** How much of a Rain avatar a head view keeps (metres down from the top of the bounds). */
export const HEAD_SPAN = 0.62;
/** A body view keeps head to knees: a dress reads bigger than the whole avatar in a wide box. */
export const BODY_SPAN = 1.25;

/**
 * Turntable speed in radians per second. Motion off: still. Less (or reduced motion, or Gentle visuals): a slow drift.
 * Gentle never adds movement over 'less'; it only ever lengthens fades.
 */
export function spinRate({ motion = 'full', reducedMotion = false, gentle = false } = {}) {
  if (motion === 'off') return 0;
  return motion === 'less' || reducedMotion || gentle ? 0.18 : 0.5;
}

/** Fade lengths (ms) for the preview's IN and OUT: Gentle lengthens to the 300 ms floor, motion off cuts. */
export function fadeMs({ motion = 'full', gentle = false } = {}) {
  if (motion === 'off') return { in: 0, out: 0 };
  return gentle ? { in: 320, out: 300 } : { in: 180, out: 120 };
}

/**
 * The box to frame: min/max [x, y, z] of the model, cut to its top HEAD_SPAN (head view) or BODY_SPAN (body view).
 * Returns { center:[x,y,z], size:[w,h,d] }.
 */
export function viewBox(min, max, view = 'full') {
  const lo = [...min], hi = [...max];
  const span = view === 'head' ? HEAD_SPAN : view === 'body' ? BODY_SPAN : 0;
  if (span) lo[1] = Math.max(lo[1], hi[1] - span);
  return { center: lo.map((v, i) => (v + hi[i]) / 2), size: lo.map((v, i) => Math.max(1e-3, hi[i] - v)) };
}

/**
 * Camera distance so a turning box fits a perspective view (vertical fov in degrees, aspect = width / height),
 * with `pad` of air on every side. The box turns: its width runs between its widest side and its footprint diagonal
 * (most models are round in plan, so the corners of the box rarely carry anything).
 */
export function fitDistance(size, fovDeg, aspect, pad = 0.08) {
  const [w, h, d] = size;
  const turn = (Math.max(w, d) + Math.hypot(w, d)) / 2, half = (fovDeg * Math.PI) / 360;
  const byH = (h * (1 + pad * 2)) / 2 / Math.tan(half);
  const byW = (turn * (1 + pad * 2)) / 2 / (Math.tan(half) * Math.max(0.1, aspect));
  return Math.max(byH, byW) + Math.min(w, d) / 2;
}
