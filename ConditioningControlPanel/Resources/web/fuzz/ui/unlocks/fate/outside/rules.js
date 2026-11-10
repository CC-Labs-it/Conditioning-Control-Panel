/* ui/unlocks/fate/outside/rules.js - small pure numbers for Fresh Air and Door Ajar (tests/fate-outside.test.mjs). */

/* ---- Door Ajar: a strip of white light at the right edge, a little wider with every purchase after it ---- */
export const DOOR = Object.freeze({ base: 7, step: 1.5, cap: 30, liteCap: 14 });

/** Purchases made after `id` in the run's buy order (0 when it is the latest, or missing). */
export function buysAfter(order, id) {
  const list = Array.isArray(order) ? order : [];
  const i = list.lastIndexOf(id);
  return i < 0 ? 0 : list.length - 1 - i;
}

/** The light's width in px after `later` purchases. Never above the cap: it is a door left ajar, not open. */
export function doorWidth(later, lite = false) {
  const cap = lite ? DOOR.liteCap : DOOR.cap;
  const n = Math.max(0, Math.floor(Number(later) || 0));
  return Math.min(cap, DOOR.base + DOOR.step * n);
}

/* ---- Fresh Air: a breeze now and then ---- */
export const BREEZE = Object.freeze({
  firstMs: [20000, 40000],   // after a reload: the first gust
  arriveMs: 2500,            // just bought: one gust soon, so the buy shows
  gapMs: [55000, 95000],     // between gusts
  crossMs: 2400,             // the gust front crosses the page in this long
  leanDeg: 2.2,              // the most a thing leans
  leanPx: 3,                 // and slides with the wind
  birdChance: 0.55,          // a gust brings a bird along this often
  maxLean: 28,               // things leaning in one gust, at most
  leaves: 6, liteLeaves: 3,
});

/** Milliseconds between `[min, max]` from a 0..1 draw. */
export const between = ([min, max], r) => Math.round(min + (max - min) * Math.max(0, Math.min(1, r)));

/** When the gust front reaches a thing centred at x on a page `w` wide, blowing `dir` (1 = to the right). */
export function gustDelay(x, w, dir, crossMs = BREEZE.crossMs) {
  const width = Math.max(1, w);
  const t = Math.max(0, Math.min(1, x / width));
  return Math.round((dir > 0 ? t : 1 - t) * crossMs);
}
