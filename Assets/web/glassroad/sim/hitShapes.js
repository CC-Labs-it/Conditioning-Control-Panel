/* Hazard contact shapes: a kart is hit only where the drawn hazard actually reaches it (owner 2026-11-01).
 * Pure numbers, one clock, no reads of the art at run time: tests/hit-sizes.test.mjs measures the art against these. */

// The kart the live 15% scale draws (feel.json contact.radiusM, contact.maxHeightM). Hazards never pad beyond it.
export const HAZARD_KART = Object.freeze({ radiusM: .62, heightM: 1.21 });
// In the online course hash: a change to any hazard contact shape moves this.
export const HAZARD_CONTACT_REVISION = 1;

/** How far a round body reaches sideways at a kart spanning h..h+height. Negative: it passes clear above or below. */
export function roundReach(radius, centreY, h, height = HAZARD_KART.heightM) {
  const gap = Math.max(0, centreY - (h + height), h - centreY);
  return gap >= radius ? -1 : Math.sqrt(radius * radius - gap * gap);
}

/** First entry of a segment into an origin-centred ellipse. Already inside answers null, the same as gateSweep. */
export function ellipseSweep(a, b, rx, rz) {
  const ax = a[0] / rx, az = a[1] / rz, dx = (b[0] - a[0]) / rx, dz = (b[1] - a[1]) / rz;
  const c = ax * ax + az * az - 1;
  if (c <= 0) return null;
  const A = dx * dx + dz * dz, B = ax * dx + az * dz, disc = B * B - A * c;
  if (A < 1e-12 || disc < 0) return null;
  const t = (-B - Math.sqrt(disc)) / A;
  return t >= 0 && t <= 1 ? t : null;
}

/** First fraction of a step where solid(f) holds, refined by bisection. Inside at the start answers null: a kart can always leave. */
export function firstContact(solid, steps) {
  if (solid(0)) return null;
  for (let i = 1; i <= steps; i++) {
    if (!solid(i / steps)) continue;
    let lo = (i - 1) / steps, hi = i / steps;
    for (let j = 0; j < 6; j++) { const mid = (lo + hi) / 2; if (solid(mid)) hi = mid; else lo = mid; }
    return hi;
  }
  return null;
}
