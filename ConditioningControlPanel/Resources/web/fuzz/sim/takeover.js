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

export * from './takeover-end.js';
