/* hud/countdown.js - the count's screen maths (polish 5, J2). Pure: no DOM, no three.
 *
 * chipAlpha: the rival name chips stay hidden through the count and fade back in after go (their IN), so the
 * big digits never sit on a name. kartPoint: the player's kart in view fractions (0..1 from the top left), from
 * projected device coordinates, for the Go burst; null when the point is behind the camera.
 */

const smooth = (x) => { const t = x <= 0 ? 0 : x >= 1 ? 1 : x; return t * t * (3 - 2 * t); };

/** Rival chip opacity share by race phase: 0 before go, eased back over fadeS after it, 1 otherwise. */
export function chipAlpha(phase, sinceGoS, fadeS = 0.6) {
  if (phase === 'ready' || phase === 'countdown') return 0;
  if (phase !== 'racing') return 1;
  if (!(fadeS > 0)) return 1;
  return smooth((Number(sinceGoS) || 0) / fadeS);
}

/** Device coordinates (x, y in -1..1, z depth) to view fractions, clamped a little inside the frame. */
export function kartPoint(x, y, z) {
  if (!Number.isFinite(x) || !Number.isFinite(y) || !(z < 1) || !(z > -1)) return null;
  const c = (v) => Math.min(0.95, Math.max(0.05, v));
  return { x: c((x + 1) / 2), y: c((1 - y) / 2) };
}
