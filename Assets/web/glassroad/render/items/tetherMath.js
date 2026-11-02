/* render/items/tetherMath.js - the Tether chain's shape (toys kit, 2026-10-25). Three-free.
 * A chain hangs between the tower and the towed kart: it sags in the middle and tightens as the
 * link ages (k 0..1 of items.tetherMaxS), so a long tow reads as strain. */

export const TETHER_LOOK = Object.freeze({ links: 10, liftM: 0.8, sagM: 0.9, tautSag: 0.25, wobbleM: 0.08, wobbleHz: 3 });

/** Fills out (3 * (links + 1) floats) with the chain from a to b; returns the point count. */
export function tetherPoints(a, b, k, t, out, look = TETHER_LOOK, still = false) {
  const n = look.links, sag = look.sagM * (1 - (1 - look.tautSag) * Math.min(1, Math.max(0, k)));
  for (let i = 0; i <= n; i++) {
    const u = i / n, hang = 4 * u * (1 - u);
    const wob = still ? 0 : Math.sin(2 * Math.PI * look.wobbleHz * t + u * 9) * look.wobbleM * hang;
    out[i * 3] = a[0] + (b[0] - a[0]) * u;
    out[i * 3 + 1] = a[1] + (b[1] - a[1]) * u + look.liftM - sag * hang + wob;
    out[i * 3 + 2] = a[2] + (b[2] - a[2]) * u;
  }
  return n + 1;
}

const clamp = (x) => Math.max(0, Math.min(1, Number.isFinite(x) ? x : 0));
const smooth = (x) => { const k = clamp(x); return k * k * (3 - 2 * k); };

/** A finite segment pose, including degenerate endpoints. Long axis is local +Y. */
export function tetherSegment(points, i, out = {}) {
  const p = i * 3, dx = points[p + 3] - points[p], dy = points[p + 4] - points[p + 1], dz = points[p + 5] - points[p + 2];
  const length = Math.hypot(dx, dy, dz);
  out.x = points[p]; out.y = points[p + 1]; out.z = points[p + 2]; out.length = length;
  out.dx = length > 1e-6 ? dx / length : 0;
  out.dy = length > 1e-6 ? dy / length : 1;
  out.dz = length > 1e-6 ? dz / length : 0;
  out.roll = (i & 1) * Math.PI / 2;
  return out;
}

/** Smooth readable snap: two halves recoil apart, then fade. Gentle keeps the shape still. */
export function tetherBreakPose(t, i, reason, still = false, out = {}) {
  const age = Math.max(0, Number.isFinite(t) ? t : 0), side = i < TETHER_LOOK.links / 2 ? -1 : 1;
  const q = smooth(age / 0.45);
  out.alpha = 1 - smooth((age - 0.45) / 0.35);
  out.cueAlpha = smooth(age / 0.3) * out.alpha;
  out.recoil = still ? 0 : side * q * (reason === 'pass' ? 0.65 : 0.35);
  out.lift = still ? 0 : Math.sin(Math.min(1, age / 0.8) * Math.PI) * 0.35;
  out.turn = still ? 0 : side * q * 0.6;
  out.spin = reason === 'pass' && !still ? age * Math.PI * 2 : 0;
  out.done = age >= 0.8;
  return out;
}

/** A link never widens as a tow grows. Adjacent links overlap at their rounded ends. */
export function tetherScale(span, sourceLength, age = 1, still = false, out = {}) {
  out.width = 1;
  out.length = Math.max(0.001, span) / Math.max(0.001, sourceLength) * 1.035;
  out.alpha = smooth(Math.max(0, age) / (still ? 0.3 : 0.18));
  return out;
}
