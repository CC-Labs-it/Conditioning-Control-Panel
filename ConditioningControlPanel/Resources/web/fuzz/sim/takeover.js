/* sim/takeover.js - EMI Takeover (goon lane, owner 2026-11-09), the pure part. No DOM.
 *
 * The run-up: the player buys go_auto, go_will, go_copy, go_hive. After Self Copying the player's own manual taps make
 * the copies; after HIVE MIND copies also appear on their own and EMI buys her own three tiles with her tips. Tips are
 * her own count: nothing is ever taken from the bank.
 * The ending (go_end take): the screen fills with EMIs on a grid, their screens flicker pictures, the grid spells DROP
 * with EMI screens as pixels, then the error card. Timings live in TAKE. */

export const CHAIN = Object.freeze(['go_auto', 'go_will', 'go_copy', 'go_hive']);

/** The tiles EMI buys herself, in order. Prices are in her tips. */
export const HER_TILES = Object.freeze([
  Object.freeze({ id: 'h_tx', name: 'Transmission', price: 60, what: 'EMI screens show pictures.' }),
  Object.freeze({ id: 'h_amp', name: 'Amplifier', price: 130, what: 'Every EMI gets bigger.' }),
  Object.freeze({ id: 'h_uni', name: 'Unified Transmission', price: 220, what: 'Every EMI screen shows the same picture.' }),
]);
const TILE_IDS = HER_TILES.map((t) => t.id);

/** How far the story is, from the rows owned. */
export function stageOf(has) {
  let stage = 'none';
  for (const [id, name] of [['go_auto', 'auto'], ['go_will', 'will'], ['go_copy', 'copy'], ['go_hive', 'hive']]) {
    if (!has(id)) break;
    stage = name;
  }
  return stage;
}

/** The bark pool for the moment: the stage, or her last purchase. */
export function poolOf(stage, bought = []) {
  if (stage === 'none') return null;
  if (stage !== 'hive') return { auto: 'autonomy', will: 'free_will', copy: 'self_copying' }[stage];
  if (bought.includes('h_uni')) return 'shopping_unified';
  if (bought.includes('h_amp')) return 'shopping_amplifier';
  if (bought.includes('h_tx')) return 'shopping_transmission';
  return 'hive_mind';
}

/* ---- population: copies beside the main EMI ---- */

export const POP = Object.freeze({
  PRESSES: 12,          // manual taps per copy before HIVE MIND
  PRESSES_HIVE: 8,      // and after it
  SPLIT_GAP_S: 4,       // at most one copy per this long
  SELF_S: 15,           // after HIVE MIND one copy on its own per this long
  SELF_S_LITE: 25,
});

/** The most copies (the main EMI not counted) on the page. Phones get about half. */
export function copyCap(stage, bought = [], lite = false) {
  if (stage !== 'copy' && stage !== 'hive') return 0;
  let cap = lite ? 3 : 6;
  if (stage === 'hive') cap = lite ? 4 : 8;
  if (bought.includes('h_amp')) cap = lite ? 6 : 12;
  if (bought.includes('h_uni')) cap = lite ? 8 : 16;
  return cap;
}

export const newPop = (copies = 0, meter = 0) => ({ copies, meter, lastSplit: -Infinity, selfAt: null });

/** One trusted manual tap on the button. Auto taps, EMI's own taps and bouncing-copy presses never come here. */
export function popPress(p, { stage }) {
  if (stage !== 'copy' && stage !== 'hive') return p;
  const need = stage === 'hive' ? POP.PRESSES_HIVE : POP.PRESSES;
  /* Presses made while the page is full or inside the gap are kept, up to one copy banked. */
  return { ...p, meter: Math.min(need * 2, p.meter + 1) };
}

/** Time passing (seconds on the game clock). Answers the next state and how many copies to make now (0 or 1). */
export function popStep(p, { now, stage, cap, lite = false }) {
  if (stage !== 'copy' && stage !== 'hive') return { pop: p, made: 0, self: false };
  const need = stage === 'hive' ? POP.PRESSES_HIVE : POP.PRESSES;
  let { copies, meter, lastSplit, selfAt } = p;
  if (stage === 'hive' && selfAt == null) selfAt = now + 3;   // the first one nobody clicked for comes quickly
  const room = copies < cap && now - lastSplit >= POP.SPLIT_GAP_S;
  if (room && meter >= need) return { pop: { copies: copies + 1, meter: meter - need, lastSplit: now, selfAt }, made: 1, self: false };
  if (room && stage === 'hive' && now >= selfAt) {
    return { pop: { copies: copies + 1, meter, lastSplit: now, selfAt: now + (lite ? POP.SELF_S_LITE : POP.SELF_S) }, made: 1, self: true };
  }
  return { pop: { copies, meter, lastSplit, selfAt }, made: 0, self: false };
}

/* ---- her tips and her tiles ---- */

export const TIPS = Object.freeze({ PER_S: 1, PER_TAP: 1, BUY_GAP_S: 40, FIRST_BUY_S: 25 });

/** The tile she is saving for, or null when she owns all three. */
export const nextTile = (bought = []) => HER_TILES.find((t) => !bought.includes(t.id)) || null;

/** May she buy now? Only after HIVE MIND, with the tips, and never two tiles close together. */
export function canBuy({ stage, tips, bought = [], sinceBuyS, sinceHiveS }) {
  const tile = nextTile(bought);
  if (!tile || stage !== 'hive') return null;
  if (tips < tile.price) return null;
  if (sinceHiveS < TIPS.FIRST_BUY_S || sinceBuyS < TIPS.BUY_GAP_S) return null;
  return tile;
}

/** Her purchase: tips go down by the price, the tile is hers. The bank is not an argument on purpose. */
export function buyTile({ tips, bought = [] }, tile) {
  if (!tile || bought.includes(tile.id) || tips < tile.price) return { tips, bought };
  return { tips: tips - tile.price, bought: [...bought, tile.id] };
}

/** What the saved bag may hold: sane numbers, known tiles, in order. */
export function cleanBag(raw) {
  const src = raw && typeof raw === 'object' ? raw : {};
  const num = (v) => (Number.isFinite(v) && v > 0 ? Math.floor(v) : 0);
  const bought = TILE_IDS.filter((id, i) => Array.isArray(src.bought) && TILE_IDS.slice(0, i + 1).every((x) => src.bought.includes(x)));
  return { copies: Math.min(16, num(src.copies)), meter: Math.min(24, num(src.meter)), tips: num(src.tips), bought, clicked: num(src.clicked), self: num(src.self) };
}

/* ---- barks: text bubbles, never speech ---- */

export const BARKS = Object.freeze({
  MAX_VISIBLE: 2,          // 3 in the ending fill
  PER_10S: 3,
  PER_10S_LITE: 2,
  COOL_S: 25,              // one EMI after speaking
  REPEAT_S: 360,           // no line twice inside this
  LIFE_MS: 2200,
  LIFE_PER_CHAR_MS: 60,
  ANSWER_MS: [900, 1400],
});

export const barkLife = (text) => BARKS.LIFE_MS + BARKS.LIFE_PER_CHAR_MS * String(text).length;

export const newBarks = () => ({ shown: [], said: {} });

/** May a bubble start now? `cost` is 1, or 2 for a call and answer. */
export function barkRoom(b, { now, visible, lite = false, maxVisible = BARKS.MAX_VISIBLE, cost = 1 }) {
  const recent = b.shown.filter((t) => now - t < 10);
  return visible + cost <= maxVisible && recent.length + cost <= (lite ? BARKS.PER_10S_LITE : BARKS.PER_10S);
}

/** Pick a line (or a pair) from a pool that was not said lately. Null when the pool is dry: silence. */
export function pickBark(b, pool, { now, rng = Math.random }) {
  const key = (l) => (Array.isArray(l) ? l.join('|') : l);
  const fresh = (pool || []).filter((l) => !(now - (b.said[key(l)] ?? -Infinity) < BARKS.REPEAT_S));
  if (!fresh.length) return null;
  return fresh[Math.floor(rng() * fresh.length)];
}

export function barkSaid(b, line, { now, cost = 1 }) {
  const key = Array.isArray(line) ? line.join('|') : line;
  const shown = [...b.shown.filter((t) => now - t < 10), ...Array(cost).fill(now)];
  return { shown, said: { ...b.said, [key]: now } };
}

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
