/* sim/takeover-end.js - EMI Takeover ending (go_end take), the pure part. No DOM. Re-exported by sim/takeover.js. */

/* ---- the ending: grid, fill, flicker, DROP ---- */

export const TAKE = Object.freeze({
  FILL_S: 24,            // an untouched player: the screen is full about here
  FILL_MIN_S: 10,        // hammering never fills it faster than this
  CLICK_S: 0.3,          // one click moves the fill this far ahead
  SETTLE_S: 1.6,         // full screen, faces, before the pictures
  FLICKER_S: 5,          // chaotic pictures
  FLICKER_MS: [380, 680],        // each screen changes this often: under 3 times a second
  FLICKER_MS_LITE: [520, 900],
  RESOLVE_S: 1.2,        // the screens settle into the word
  DROP_S: 5,             // DROP holds
  IDLE_S: 6.5,           // the error card: big EMI idles and blinks
  FACE_S: 1.6,           // the uncanny face, no line
  CLOSE_S: 0.3,          // glitch and blink shut
  DARK_S: 0.9,
  GENTLE_CLOSE_S: 1.2,
});

/** The longest the take can run, seconds. Must stay under CONFIG.ENDING_TAKE_MAX_MS. */
export const takeMaxS = () => TAKE.FILL_S + TAKE.SETTLE_S + TAKE.FLICKER_S + TAKE.RESOLVE_S + TAKE.DROP_S + TAKE.IDLE_S + TAKE.FACE_S + TAKE.GENTLE_CLOSE_S + TAKE.DARK_S;

const GLYPHS = {
  D: ['11110', '10001', '10001', '10001', '10001', '10001', '11110'],
  R: ['11110', '10001', '10001', '11110', '10100', '10010', '10001'],
  O: ['01110', '10001', '10001', '10001', '10001', '10001', '01110'],
  P: ['11110', '10001', '10001', '11110', '10000', '10000', '10000'],
};

/** The word as rows of 0/1. Landscape: DROP in one line, 23 x 7. Portrait: DR over OP, 11 x 15. */
export function dropMask(stacked) {
  const line = (word) => GLYPHS[word[0]].map((_, r) => [...word].map((ch) => GLYPHS[ch][r]).join('0'));
  return stacked ? [...line('DR'), '0'.repeat(11), ...line('OP')] : line('DROP');
}

const hits = (a, b) => a.x < b.x + b.w && a.x + a.w > b.x && a.y < b.y + b.h && a.y + a.h > b.y;

/** The grid of EMIs that fills the screen, one per cell, and which cells are lit for DROP.
 *  `controls` is the padded rect round Wake Up's group: no cell is ever made there, and the word is moved off it. */
export function dropGrid({ w, h, controls = null }) {
  const stacked = h > w * 1.15;
  const mask = dropMask(stacked);
  const wordC = mask[0].length, wordR = mask.length;
  const margin = stacked ? 1 : 2;
  const minC = wordC + margin * 2, minR = wordR + margin * 2;
  const pitch = Math.min(w / minC, h / minR);
  const cols = Math.max(minC, Math.floor(w / pitch)), rows = Math.max(minR, Math.floor(h / pitch));
  const ox = (w - cols * pitch) / 2, oy = (h - rows * pitch) / 2;
  const rect = (c, r) => ({ x: ox + c * pitch, y: oy + r * pitch, w: pitch, h: pitch });
  const blocked = (c, r) => !!controls && hits(rect(c, r), controls);
  const c0 = Math.floor((cols - wordC) / 2);
  let r0 = Math.floor((rows - wordR) / 2);
  const clash = (top) => mask.some((line, r) => [...line].some((ch, c) => ch === '1' && blocked(c0 + c, top + r)));
  while (clash(r0) && r0 + wordR < rows) r0 += 1;
  const cells = [];
  for (let r = 0; r < rows; r++) for (let c = 0; c < cols; c++) {
    if (blocked(c, r)) continue;
    const lit = mask[r - r0]?.[c - c0] === '1';
    cells.push({ c, r, x: ox + c * pitch, y: oy + r * pitch, lit });
  }
  return { stacked, cols, rows, pitch, ox, oy, c0, r0, wordC, wordR, cells, readable: !clash(r0) };
}

/** The order the cells fill in: every new EMI pops in beside one that is already there. `seeds` are page points
 *  (where the EMIs stand when the ending starts); with none it starts from the middle of the bottom row. */
export function fillOrder(grid, seeds = [], rng = Math.random) {
  const { cells, pitch } = grid;
  const at = new Map(cells.map((cell, i) => [`${cell.c},${cell.r}`, i]));
  const nearest = (p) => {
    let best = 0, d = Infinity;
    cells.forEach((cell, i) => { const q = Math.hypot(cell.x + pitch / 2 - p.x, cell.y + pitch / 2 - p.y); if (q < d) { d = q; best = i; } });
    return best;
  };
  const start = seeds.length ? seeds : [{ x: grid.ox + grid.cols * pitch / 2, y: grid.oy + (grid.rows - 0.5) * pitch }];
  const order = [], from = new Map(), done = new Set(), frontier = [];
  const push = (i, parent) => {
    if (done.has(i)) return;
    done.add(i); order.push(i); from.set(i, parent);
    const { c, r } = cells[i];
    for (const [dc, dr] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
      const j = at.get(`${c + dc},${r + dr}`);
      if (j != null && !done.has(j)) frontier.push([j, i]);
    }
  };
  for (const p of start) push(nearest(p), -1);
  while (order.length < cells.length) {
    if (!frontier.length) { const rest = cells.findIndex((_, i) => !done.has(i)); push(rest, -1); continue; }   // an island cut off by the controls
    const k = Math.floor(rng() * frontier.length);
    const [j, parent] = frontier.splice(k, 1)[0];
    push(j, parent);
  }
  return { order, from };
}

/** How many of n cells are filled at fill-clock time t: copies of copies, so slow first and fast last. */
export function fillCount(t, n, T = TAKE.FILL_S) {
  if (t <= 0) return Math.min(n, 1);
  if (t >= T) return n;
  return Math.min(n, Math.max(1, Math.floor(Math.pow(n + 1, t / T))));
}

/** The fill clock: real seconds plus what the player's clicks added, never faster than FILL_MIN_S overall. */
export function fillClock(elapsed, clicks) {
  const fastest = elapsed * (TAKE.FILL_S / TAKE.FILL_MIN_S);
  return Math.min(fastest, elapsed + clicks * TAKE.CLICK_S);
}

/** Seconds until the screen is full at `perS` clicks a second. */
export function fillTime(perS = 0) {
  const dt = 1 / 30;
  let t = 0;
  while (fillClock(t, perS * t) < TAKE.FILL_S && t < 120) t += dt;
  return t;
}

/** Milliseconds until one screen's next picture during the flicker. */
export function flickerGap(rng = Math.random, lite = false) {
  const [lo, hi] = lite ? TAKE.FLICKER_MS_LITE : TAKE.FLICKER_MS;
  return lo + rng() * (hi - lo);
}

/** The error card. `n` is the real number of EMIs on screen. The last line never completes. */
export const errorLines = (n) => [
  'E-M1 : PAGE_TAKEN_OVER',
  'This page stopped responding to you.',
  'It is responding to someone else.',
  'Nothing on your device is broken. This is the game.',
  'Wake Up still works.',
  'process   you          not responding',
  `process   emi  x ${n}`.padEnd(23) + 'responding',
  '> restoring control ...',
];
