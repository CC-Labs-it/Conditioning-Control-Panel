/* ui/unlocks/fate/outside/grass-model.js - Touch Grass rules (pure, no DOM; tests/fate-outside.test.mjs). Blades along the
 * bottom edge, each a little spring. A pointer passing through a blade pushes it the way the pointer moves; a blade pays
 * once and then rests for COOLDOWN_MS of game time, so a wiggling mouse cannot farm it. Randomness comes from the caller
 * (Math.random in the page: grass is cosmetic and never touches the run's rng). */

export const COOLDOWN_MS = 30000;
export const BLADE_GAP = 13;       // px between blades on a desktop
export const MAX_BLADES = 96;
export const LITE_MAX_BLADES = 40;
export const REACH = 9;            // px either side of a blade the pointer still touches it
/** Each paying touch is worth this many seconds of the current per-second pay. */
export const PAY_SECONDS = 0.08;

/** How many blades fit `width` px. */
export function bladeCount(width, lite = false) {
  const n = Math.floor(Math.max(0, width) / BLADE_GAP);
  return Math.max(0, Math.min(lite ? LITE_MAX_BLADES : MAX_BLADES, n));
}

/** A row of blades across `width`, each `{x, h, base, a, v, hue, until}`. `rand` returns 0..1. */
export function makeBlades(width, height, rand = Math.random, lite = false) {
  const n = bladeCount(width, lite);
  if (!n) return [];
  const step = width / n;
  return Array.from({ length: n }, (_, i) => {
    const base = (rand() - 0.5) * 0.28;
    return {
      x: Math.round((i + 0.2 + rand() * 0.6) * step),
      h: Math.round(height * (0.42 + rand() * 0.55)),
      base, a: base, v: 0,
      hue: Math.floor(rand() * 3),
      phase: rand() * Math.PI * 2,
      until: 0,
    };
  });
}

/** Blades the pointer at (x, y above the ground) touches. `y` counts up from the ground, in px. */
export function touched(blades, x, y) {
  const out = [];
  for (let i = 0; i < blades.length; i++) {
    const b = blades[i];
    if (Math.abs(b.x - x) <= REACH && y <= b.h + 4 && y >= -6) out.push(i);
  }
  return out;
}

/** The pointer brushed through (x, y) moving by dx: pushes the blades and answers the indices that pay now. */
export function brush(blades, x, y, dx, now, cooldownMs = COOLDOWN_MS) {
  const paying = [];
  const push = Math.max(-1, Math.min(1, dx / 12)) * 2.4;
  for (const i of touched(blades, x, y)) {
    const b = blades[i];
    if (push) b.v += push;
    if (now >= b.until) { b.until = now + cooldownMs; paying.push(i); }
  }
  return paying;
}

/** One spring step of dt seconds. `wind` is the rest-angle offset the ambient sway adds right now; `gust(b)` an extra offset
 *  per blade (a Fresh Air gust passing). Answers the largest motion. */
export function step(blades, dt, wind = 0, gust = null) {
  const k = 38, c = 5.5, d = Math.min(0.05, Math.max(0, dt));
  let moving = 0;
  for (const b of blades) {
    const rest = b.base + wind * (0.6 + 0.4 * Math.sin(b.phase)) + (gust ? gust(b) : 0);
    b.v += (-(b.a - rest) * k - b.v * c) * d;
    b.a += b.v * d;
    b.a = Math.max(-1.1, Math.min(1.1, b.a));
    moving = Math.max(moving, Math.abs(b.v));
  }
  return moving;
}

/** What one paying touch is worth at per-second pay `fps`. */
export const touchPay = (fps) => Math.max(1, (Number(fps) || 0) * PAY_SECONDS);

/** A gust's push on a blade at x: one lean the way it blows, `ms` after the gust started, on a page `w` wide. */
export function gustPush(x, w, dir, ms, crossMs = 2400, holdMs = 1600, lean = 0.5) {
  const t = Math.max(0, Math.min(1, x / Math.max(1, w)));
  const s = (ms - (dir > 0 ? t : 1 - t) * crossMs) / holdMs;
  return s <= 0 || s >= 1 ? 0 : dir * lean * Math.sin(Math.PI * s);
}
