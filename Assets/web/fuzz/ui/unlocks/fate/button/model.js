/* ui/unlocks/fate/button/model.js - the pure numbers behind the Living Button fate cards (ft_depth, ft_cursor, ft_pressed,
 * ft_glass). No DOM, no ctx: the widgets in this folder read these, the tests pin them. Every random draw here takes a
 * `rand` function so the widgets pass Math.random (cosmetic only, never ctx.rng: route timing pins its sequence). */

const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));
const TAU = Math.PI * 2;

/* ---- A Little Depth ---- */

/** How far a page object leans at full depth with the pointer at the screen edge: a few pixels, under a degree. */
export const LEAN = Object.freeze({ px: 3.4, py: 2.4, deg: 0.9, tau: 0.24, maxObjects: 14, liteObjects: 6, liteEveryMs: 80 });

/** The pointer at (x, y) in a w x h page, as -1..1 from the centre. */
export function normPointer(x, y, w, h) {
  if (!(w > 0) || !(h > 0)) return { nx: 0, ny: 0 };
  return { nx: clamp((x / w) * 2 - 1, -1, 1), ny: clamp((y / h) * 2 - 1, -1, 1) };
}

/** Depth of the i-th object, 0.45..1: neighbours never sit at the same depth, so the page reads as layered. */
export function depthOf(i) {
  const f = ((i + 1) * 0.6180339887) % 1;
  return 0.45 + 0.55 * f;
}

/** The lean of one object: it slides away from the pointer and tips toward it, by its depth. */
export function leanFor(nx, ny, depth) {
  const d = clamp(depth, 0, 1), x = clamp(nx, -1, 1), y = clamp(ny, -1, 1);
  return { x: -x * d * LEAN.px, y: -y * d * LEAN.py, r: x * d * LEAN.deg };
}

/** A touch screen has no hover: a very slow idle drift instead (periods of 19 and 27 s, well under 1 Hz). */
export function drift(seconds) {
  return { nx: 0.55 * Math.sin((TAU * seconds) / 19), ny: 0.45 * Math.sin((TAU * seconds) / 27 + 1) };
}

/** Frame-rate independent easing toward a target. */
export function approach(current, target, dt, tau = LEAN.tau) {
  if (!(dt > 0)) return current;
  return target + (current - target) * Math.exp(-dt / tau);
}

/** CSS for a lean, rounded so an object at rest writes nothing new. */
export function leanCss(l) {
  const r2 = (v) => Math.round(v * 100) / 100;
  return { translate: `${r2(l.x)}px ${r2(l.y)}px`, rotate: `${r2(l.r)}deg` };
}

/* ---- Second Cursor ---- */

export const CURSOR = Object.freeze({
  firstMs: [6000, 11000],      // right after the buy, so the player sees what they paid for
  restoredMs: [14000, 30000],  // after a reload
  gapMs: [24000, 62000],       // then now and then: irregular, tens of seconds
  presses: [1, 3],
  pressGapMs: [430, 660],      // under 3 Hz even at the fastest
  calmPressGapMs: 760,         // Gentle and reduced motion
  travelMs: [1700, 2600],
  leaveMs: [1300, 2000],
  fadeMs: 420,                 // Gentle: it fades in and out beside the button instead of travelling
  size: 26,
  pad: 40,                     // starts and ends this far outside the page
});

const between = (rand, [lo, hi]) => lo + (hi - lo) * rand();

/** Milliseconds until the next visit. */
export function nextVisitMs(rand, { first = false, restored = false } = {}) {
  return Math.round(between(rand, first ? (restored ? CURSOR.restoredMs : CURSOR.firstMs) : CURSOR.gapMs));
}

/** How many presses this visit: 1 to 3, mostly 1 or 2. */
export function pressCount(rand) {
  const r = rand();
  return r < 0.45 ? 1 : r < 0.85 ? 2 : 3;
}

/** A point just outside one of the four edges. `avoid` keeps the exit off the entry edge. */
export function edgePoint(rand, w, h, avoid = -1) {
  let side = Math.floor(rand() * 4) % 4;
  if (side === avoid) side = (side + 1 + Math.floor(rand() * 3)) % 4;
  const p = CURSOR.pad, u = 0.12 + 0.76 * rand();
  if (side === 0) return { side, x: u * w, y: -p };
  if (side === 1) return { side, x: w + p, y: u * h };
  if (side === 2) return { side, x: u * w, y: h + p };
  return { side, x: -p, y: u * h };
}

/** A control point that bends the path a little to one side, so it drifts rather than slides. */
export function bendPoint(rand, a, b) {
  const mx = (a.x + b.x) / 2, my = (a.y + b.y) / 2, dx = b.x - a.x, dy = b.y - a.y;
  const k = (rand() - 0.5) * 0.5;
  return { x: mx - dy * k, y: my + dx * k };
}

/** Quadratic bezier. */
export function curve(a, c, b, t) {
  const u = 1 - t;
  return { x: u * u * a.x + 2 * u * t * c.x + t * t * b.x, y: u * u * a.y + 2 * u * t * c.y + t * t * b.y };
}

/** Smooth start and stop. */
export function smooth(t) {
  const x = clamp(t, 0, 1);
  return x * x * (3 - 2 * x);
}

/** Where on the button it aims: off centre, inside the middle half of the face. */
export function aimOffset(rand) {
  return { fx: (rand() - 0.5) * 0.5, fy: (rand() - 0.5) * 0.4 };
}

/** The cursor, drawn in code: an arrow a little rounder and tilted off the system one, with a separate shadow that never
 *  turns with it (one lamp, top left). Its tip sits at (TIP, TIP) of a 32 x 32 box. */
export const TIP = 3;
export function cursorSvg({ fill = '#fff7fb', edge = '#2a1830', size = CURSOR.size } = {}) {
  const arrow = 'M3 3 L3.6 23.4 Q3.8 24.6 4.8 23.8 L9.4 19.6 L12.9 27.2 Q13.4 28.2 14.4 27.8 L16.9 26.7 Q17.8 26.2 17.4 25.3 L13.9 17.8 L20.3 17.6 Q21.5 17.4 20.7 16.5 Z';
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 32 32" aria-hidden="true">`
    + `<path d="${arrow}" transform="translate(2.2 2.8) rotate(-9 3 3)" fill="#000" fill-opacity=".22"/>`
    + `<path d="${arrow}" transform="rotate(-9 3 3)" fill="${fill}" stroke="${edge}" stroke-width="1.7" stroke-linejoin="round"/>`
    + '</svg>';
}

/* ---- Pressed ---- */

export const FLINCH = Object.freeze({ gapMs: 90, liteGapMs: 200, calmGapMs: 520, dipPx: 1.6, litePx: 1, ms: 190, veilMs: 260, calmMs: 440 });

/** Whether a press at `now` may flinch, given the last one. Gentle and reduced motion: at most one soft fade every
 *  520 ms (under 2 Hz), so the page never pulses at 3 Hz however fast the player taps. */
export function mayFlinch(last, now, { calm = false, lite = false } = {}) {
  const gap = calm ? FLINCH.calmGapMs : lite ? FLINCH.liteGapMs : FLINCH.gapMs;
  return !(now - last < gap);
}

/* ---- Glass ---- */

export const GLASS = Object.freeze({ sheenMs: [20000, 30000], firstSheenMs: [2500, 4500], sweepMs: 2600, calmMs: 3200 });

export function nextSheenMs(rand, first = false) {
  return Math.round(between(rand, first ? GLASS.firstSheenMs : GLASS.sheenMs));
}
