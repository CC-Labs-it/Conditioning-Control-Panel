/* render/items/tetherMath.js - the Tether chain's shape (toys kit, 2026-10-25). Three-free.
 * A chain hangs between the tower and the towed kart: it sags in the middle and tightens as the
 * link ages (k 0..1 of items.tetherMaxS), so a long tow reads as strain. */

export const TETHER_LOOK = Object.freeze({ links: 10, liftM: 0.8, sagM: 0.9, tautSag: 0.25, wobbleM: 0.08, wobbleHz: 3 });

/** Fills out (3 * (links + 1) floats) with the chain from a to b; returns the point count. */
export function tetherPoints(a, b, k, t, out, look = TETHER_LOOK) {
  const n = look.links, sag = look.sagM * (1 - (1 - look.tautSag) * Math.min(1, Math.max(0, k)));
  for (let i = 0; i <= n; i++) {
    const u = i / n, hang = 4 * u * (1 - u);
    const wob = Math.sin(2 * Math.PI * look.wobbleHz * t + u * 9) * look.wobbleM * hang;
    out[i * 3] = a[0] + (b[0] - a[0]) * u;
    out[i * 3 + 1] = a[1] + (b[1] - a[1]) * u + look.liftM - sag * hang + wob;
    out[i * 3 + 2] = a[2] + (b[2] - a[2]) * u;
  }
  return n + 1;
}
