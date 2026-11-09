/* sim/takeover-end.js - EMI Takeover ending (go_end take), the pure part. No DOM. Re-exported by sim/takeover.js.
 *
 * The take (owner notes 2026-11-09 + 2026-11-10, handoff-1109/TC-EMI-TAKEOVER-V3-BRIEF.md):
 *   fill    the front grid arrives row after row like pixels, its screens flashing pictures; behind it EMIs of
 *           different sizes pile in from where the swarm stood until no page shows through
 *   shut    every screen goes dark at once
 *   drop    pictures only on the grid pixels of DROP
 *   error   error sound, a small system dialog with the logo, a short shake
 *   waiting everything frozen and silent but a quiet eerie drone; the dialog's OK is live and the take waits for it
 *           (on its own after WAIT_MAX_S)
 *   giant   OK: a hard shake and a sting, the giant EMI is there at once (jumpscare), blinks, then the cheeky face
 *   close   the eyelid shuts to black (Gentle: a plain fade) */

export const TAKE = Object.freeze({
  BACK_S: 13,            // the back pile is complete here
  GRID_START_S: 1.2,     // the first front pixel
  FILL_S: 15,            // the last front pixel: the screen is full
  FULL_S: 0.8,           // full screen, still flashing, before the shut
  SHUT_S: 1.0,           // every screen dark
  DROP_S: 3,             // DROP holds
  SHAKE_S: 0.3,          // the error shake (none under Gentle)
  WAIT_MIN_S: 0.5,       // OK takes a press only after this: a click meant for DROP never skips the wait
  WAIT_MAX_S: 45,        // nobody pressed OK: go on alone
  JUMP_SHAKE_S: 0.4,     // the hard shake on OK (none under Gentle)
  POP_S: 0.3,            // she lands (a frame slightly too big, then settles; Gentle: a 300 ms fade)
  BLINK_S: 2.5,          // idle with two or three blinks
  BLINK_LEN_S: 0.13,
  CHEEKY_S: 1.5,         // ^_~ on body-smug
  CLOSE_S: 0.3,          // eyelid shut
  DARK_S: 0.7,
  GENTLE_CLOSE_S: 1.2,
  DRONE_GAIN: 0.16,      // the eerie drone under the wait (Gentle: half)
  STING_GAIN: 0.9,       // the jumpscare sting (Gentle: half)
  FLICKER_MS: [380, 680],        // a front screen changes this often: under 3 times a second
  FLICKER_MS_LITE: [520, 900],
  BACK_FLICKER_MS: [700, 1300],  // the back pile, slower
});

/** Clamp a wait (seconds from the freeze to OK) to what the take allows. */
export const clampWait = (s) => Math.min(TAKE.WAIT_MAX_S, Math.max(TAKE.WAIT_MIN_S, Number.isFinite(s) ? s : TAKE.WAIT_MAX_S));

/** Seconds from the start of the take to the start of each phase. `waitS`: freeze to OK (the longest by default). */
export function phaseStarts({ calm = false, waitS = TAKE.WAIT_MAX_S } = {}) {
  const shut = TAKE.FILL_S + TAKE.FULL_S;
  const drop = shut + TAKE.SHUT_S;
  const error = drop + TAKE.DROP_S;
  const waiting = error + (calm ? 0 : TAKE.SHAKE_S);
  const giant = waiting + clampWait(waitS);
  const cheeky = giant + TAKE.POP_S + TAKE.BLINK_S;
  const close = cheeky + TAKE.CHEEKY_S;
  const end = close + (calm ? TAKE.GENTLE_CLOSE_S : TAKE.CLOSE_S) + TAKE.DARK_S;
  return { fill: 0, shut, drop, error, waiting, giant, cheeky, close, end };
}

/** The phase at take time t. */
export function phaseAt(t, opts) {
  const p = phaseStarts(opts);
  const order = ['end', 'close', 'cheeky', 'giant', 'waiting', 'error', 'drop', 'shut'];
  for (const k of order) if (t >= p[k]) return k;
  return 'fill';
}

/** Can a press on OK end the wait `sinceS` seconds after the freeze? */
export const okTakes = (sinceS) => sinceS >= TAKE.WAIT_MIN_S;

/** The longest the take can run (nobody pressed OK), seconds. Must stay under CONFIG.ENDING_TAKE_MAX_MS. */
export const takeMaxS = () => Math.max(phaseStarts({ calm: false }).end, phaseStarts({ calm: true }).end);

/** When the giant EMI blinks, seconds after she appeared: two or three, never in the first 0.4 s or the last 0.3 s. */
export function blinkTimes(rng = Math.random) {
  const n = rng() < 0.5 ? 2 : 3;
  const lo = TAKE.POP_S + 0.4, hi = TAKE.POP_S + TAKE.BLINK_S - 0.3 - TAKE.BLINK_LEN_S;
  const slot = (hi - lo) / n;
  return Array.from({ length: n }, (_, i) => lo + slot * i + rng() * Math.max(0, slot - TAKE.BLINK_LEN_S - 0.3));
}

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

/** The front grid of EMIs, one per cell, and which cells are lit for DROP. Cells come in raster order (row after row).
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

/** When front cell k of n arrives: evenly, row after row, from GRID_START_S to FILL_S. */
export const gridAt = (k, n) => TAKE.GRID_START_S + (TAKE.FILL_S - TAKE.GRID_START_S) * (n > 1 ? k / (n - 1) : 0);
/** How many front cells have arrived at take time t. */
export function gridCount(t, n) {
  if (t < TAKE.GRID_START_S) return 0;
  if (t >= TAKE.FILL_S) return n;
  return Math.min(n, 1 + Math.floor((t - TAKE.GRID_START_S) / (TAKE.FILL_S - TAKE.GRID_START_S) * (n - 1)));
}

/** The part of a sprite box that is solid body, as fractions of its size (the shipped 859 x 869 sprites, rounded in). */
export const BODY = Object.freeze({ l: 0.17, t: 0.22, r: 0.82, b: 0.79 });

/** The back pile: EMIs of different sizes, in arrival order, that together leave no gap anywhere outside `controls`.
 *  `seeds` are the swarm EMIs `{x, y, size}` (centres, page px): they come first, where they stand. The rest are a
 *  jittered lattice spaced by the smallest size, so every spot sits inside some EMI's solid body; they pile in nearest
 *  the seeds first. Each `{x, y, size, seed}` is the sprite's top-left and its size. */
export function backPile({ w, h, pitch, seeds = [], controls = null, lite = false, rng = Math.random }) {
  const sMin = pitch * (lite ? 3.4 : 2.6), sMax = pitch * (lite ? 4.6 : 4.2);
  const stepX = sMin * (BODY.r - BODY.l) * 0.84, stepY = sMin * (BODY.b - BODY.t) * 0.84;
  const jit = sMin * 0.03;
  const out = [];
  for (const s of seeds) {
    const size = Math.max(16, s.size || sMin * 0.6);
    out.push({ x: s.x - size / 2, y: s.y - size / 2, size, seed: true, d: 0 });
  }
  const from = seeds.length ? seeds : [{ x: w / 2, y: h }];
  const lattice = [];
  const nx = Math.ceil(w / stepX) + 1, ny = Math.ceil(h / stepY) + 1;
  for (let j = 0; j < ny; j++) for (let i = 0; i < nx; i++) {
    const size = sMin + rng() * (sMax - sMin);
    const cx = i * stepX + (j % 2 ? stepX / 2 : 0) - stepX / 4 + (rng() * 2 - 1) * jit;
    const cy = j * stepY + (rng() * 2 - 1) * jit;
    /* the body box centre sits on the lattice point */
    const x = cx - size * (BODY.l + BODY.r) / 2, y = cy - size * (BODY.t + BODY.b) / 2;
    if (controls && hits({ x, y, w: size, h: size }, controls)) continue;
    const d = Math.min(...from.map((p) => Math.hypot(p.x - cx, p.y - cy))) + rng() * stepX * 3;
    lattice.push({ x, y, size, seed: false, d });
  }
  lattice.sort((a, b) => a.d - b.d);
  return [...out, ...lattice];
}

/** When back EMI k of n arrives: the seeds at once, the rest copies of copies, slow first and fast last. */
export function backAt(k, n, seeds = 0) {
  if (k < seeds) return 0;
  const m = n - seeds;
  const f = m > 1 ? (k - seeds + 1) / m : 1;
  return TAKE.BACK_S * Math.pow(f, 1 / 1.6);
}

/** Does some back EMI's solid body cover point (x, y)? (Tests and the drive.) */
export const covered = (pile, x, y) => pile.some((p) => x >= p.x + p.size * BODY.l && x <= p.x + p.size * BODY.r && y >= p.y + p.size * BODY.t && y <= p.y + p.size * BODY.b);

/** Milliseconds until one screen's next picture during the fill. */
export function flickerGap(rng = Math.random, lite = false, back = false) {
  const [lo, hi] = back ? TAKE.BACK_FLICKER_MS : lite ? TAKE.FLICKER_MS_LITE : TAKE.FLICKER_MS;
  return lo + rng() * (hi - lo);
}

/** The heads-up error dialog: short, plain, says it is the game. */
export const errorDialog = () => ({
  title: 'emi.exe',
  lines: ['This page was taken over by EMI.', 'Nothing on your device is broken. This is the game.'],
  button: 'OK',
  close: 'Close',
});
