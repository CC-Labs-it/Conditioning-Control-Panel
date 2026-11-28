/* ui/unlocks/fate/themes/logic.js - the pure part of the theme fate cards (tests/fate-themes.test.mjs): how big the
 * Little Bubble is, what the Scan Line crosses and in which order, and what the Name Tag says. No DOM here. */

/* ---- Little Bubble (ft_bubble) ---- */

export const BUBBLE_BASE = 52;        // px across on a desktop at the buy
export const BUBBLE_BASE_LITE = 38;   // on a phone
export const BUBBLE_GROW = 0.015;     // per later purchase
export const BUBBLE_CAP = 1.9;        // never much more than its first size (99 px): a corner, never the page
export const BUBBLE_CAP_LITE = 1.7;   // 65 px on a phone

/** Purchases made after the bubble arrived (fork picks count: they are purchases on the shelf too). */
export function laterBuys(order, id = 'ft_bubble') {
  const at = order.indexOf(id);
  return at < 0 ? 0 : Math.max(0, order.length - at - 1);
}

/** The bubble's diameter in px after `n` later purchases. */
export function bubbleSize(n, lite = false) {
  const base = lite ? BUBBLE_BASE_LITE : BUBBLE_BASE, cap = lite ? BUBBLE_CAP_LITE : BUBBLE_CAP;
  return Math.round(base * Math.min(cap, 1 + BUBBLE_GROW * Math.max(0, n)));
}

/* ---- Scan Line (ft_scan) ---- */

export const SCAN_EVERY_MS = 60000;   // once a minute of game time
export const SCAN_SWEEP_S = 4.5;      // top to bottom
export const SCAN_SWEEP_SOFT_S = 7;   // Gentle / reduced motion: a slower band
export const SCAN_CAP = 24;           // objects a sweep can pay for
export const SCAN_CAP_LITE = 12;
export const SCAN_PAY_S = 0.3;        // each object pays this many seconds of per-second income
export const SCAN_MIN_SIDE = 10;      // smaller than this is not a thing on the page

const overlaps = (a, b) => a.left < b.right && a.right > b.left && a.top < b.bottom && a.bottom > b.top;

/**
 * What one sweep crosses, top to bottom. cands: [{ rect, clip? }] (rect and clip are {left, top, right, bottom});
 * a candidate clipped by its scrolling parent counts only for its visible part. `skip` (the Name Tag's rect) is never
 * an object and nothing that overlaps it pays. Over the cap, the biggest win. Returns [{ i, y, rect }], `y` = the line
 * height where it is crossed (the visible part's middle), sorted by y.
 */
export function planScan(cands, { w, h, cap = SCAN_CAP, skip = null } = {}) {
  const out = [];
  cands.forEach((c, i) => {
    const r = c?.rect;
    if (!r) return;
    const m = c.clip;
    const left = Math.max(0, r.left, m?.left ?? -Infinity), right = Math.min(w, r.right, m?.right ?? Infinity);
    const top = Math.max(0, r.top, m?.top ?? -Infinity), bottom = Math.min(h, r.bottom, m?.bottom ?? Infinity);
    if (right - left < SCAN_MIN_SIDE || bottom - top < SCAN_MIN_SIDE) return;
    const rect = { left, top, right, bottom, width: right - left, height: bottom - top };
    if (skip && overlaps(rect, skip)) return;
    out.push({ i, y: (top + bottom) / 2, rect, area: rect.width * rect.height });
  });
  const kept = out.length > cap ? out.sort((a, b) => b.area - a.area).slice(0, cap) : out;
  return kept.sort((a, b) => a.y - b.y || a.rect.left - b.rect.left).map(({ i, y, rect }) => ({ i, y, rect }));
}

/** The line at height `y` with a gap round the tag: [[x0, x1], ...] segments to draw. */
export function lineSegments(y, w, skip, pad = 8) {
  if (!skip || y < skip.top - 4 || y > skip.bottom + 4) return [[0, w]];
  const segs = [];
  if (skip.left - pad > 0) segs.push([0, skip.left - pad]);
  if (skip.right + pad < w) segs.push([skip.right + pad, w]);
  return segs;
}

/** Pay for one crossed object: a tiny tick, never zero. */
export const scanPay = (fps) => Math.max(1, (Number(fps) || 0) * SCAN_PAY_S);

/* ---- Name Tag (ft_tag) ---- */

/**
 * The tag's words. Unit Number (dr_serial) renamed the player: the unit number wins. Otherwise the signed-in name, and
 * with no name a unit number all the same (the number Unit Complete would print: 1000 + seed % 9000).
 */
export function tagLabel({ displayName = '', hasSerial = false, serial = 0, seed = 0 } = {}) {
  const name = String(displayName || '').trim();
  if (name && !hasSerial) return name;
  const number = serial || 1000 + (Number.isFinite(seed) ? Math.abs(seed) : 0) % 9000;
  return 'UNIT ' + number;
}

/** A saved tag spot (fractions of the page) back in px, kept whole on the page. */
export function placeTag(spot, { w, h, tw, th, margin = 6 }) {
  const x = Math.round(Math.max(margin, Math.min(w - tw - margin, spot.fx * w)));
  const y = Math.round(Math.max(margin, Math.min(h - th - margin, spot.fy * h)));
  return { x, y };
}
