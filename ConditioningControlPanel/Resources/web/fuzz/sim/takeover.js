/* sim/takeover.js - EMI Takeover (goon lane, owner 2026-11-09, v3 brief handoff-1109/TC-EMI-TAKEOVER-V3-BRIEF.md),
 * the pure part of the run-up. No DOM. The ending's rules live in sim/takeover-end.js (re-exported at the bottom).
 *
 * The player buys go_tx (one EMI, her screen alternates pictures and her face), go_will (she walks, stops, comments,
 * hops), go_corp (she gets her own budget and buys her own tiles: Amplifier, Overclock, Louder Opinions, then Self
 * Replication), go_hive (the EMIs run the transmission in patterns). After Self Replication each of the player's own
 * manual clicks turns a page object into an EMI or splits one off. If HIVE MIND is still unbought FORCE_S after
 * Self Replication, EMI buys it herself (free). Her budget is her own number: nothing is ever taken from the bank. */

export const CHAIN = Object.freeze(['go_tx', 'go_will', 'go_corp', 'go_hive']);

/** How far the player's rows go, in order (a row switched off holds the story there). */
export function stageOf(has) {
  let stage = 'none';
  for (const [id, name] of [['go_tx', 'tx'], ['go_will', 'will'], ['go_corp', 'corp'], ['go_hive', 'hive']]) {
    if (!has(id)) break;
    stage = name;
  }
  return stage;
}

/** The tiles EMI buys herself, in order, with her budget. Self Replication is hers, never a player row. */
export const HER_TILES = Object.freeze([
  Object.freeze({ id: 'h_amp', name: 'Amplifier', price: 30, what: 'She gets bigger.' }),
  Object.freeze({ id: 'h_oc', name: 'Overclock', price: 38, what: 'She walks a little faster and her pictures change a little faster.' }),
  Object.freeze({ id: 'h_loud', name: 'Louder Opinions', price: 42, what: 'She comments more often.' }),
  Object.freeze({ id: 'h_rep', name: 'Self Replication', price: 45, what: 'Your own clicks make more of her.' }),
]);
const TILE_IDS = HER_TILES.map((t) => t.id);

/** The story moment, from the rows owned and her own tiles. HIVE MIND bought before Self Replication waits for it. */
export function phaseOf(stage, bought = []) {
  if (stage === 'none' || stage === 'tx' || stage === 'will') return stage;
  const rep = bought.includes('h_rep');
  if (stage === 'hive' && rep) return 'hive';
  return rep ? 'rep' : 'corp';
}

/** The bark pool for the moment. */
export function poolOf(phase, bought = []) {
  if (phase === 'none') return null;
  if (phase === 'corp') return bought.length ? 'shopping' : 'startup';
  return { tx: 'transmission', will: 'free_will', rep: 'replication', hive: 'hive_mind' }[phase] || null;
}

/* ---- her budget and her tiles ---- */

export const BUDGET = Object.freeze({
  PER_S: 1,          // her budget grows this much a second on the game clock, from Founded Startup on
  HURRY: 4,          // HIVE MIND already bought: she shops faster so the story catches up
  BUY_GAP_S: 30,     // never two of her buys closer than this
});

/** Her budget after `dt` seconds. */
export const budgetStep = (budget, dt, { phase, hurry = false }) =>
  (phase === 'corp' ? budget + dt * BUDGET.PER_S * (hurry ? BUDGET.HURRY : 1) : budget);

/** The tile she is saving for, or null when she owns all four. */
export const nextTile = (bought = []) => HER_TILES.find((t) => !bought.includes(t.id)) || null;

/** May she buy now? Only while she is a startup, with the budget, and never two tiles close together. */
export function canBuy({ phase, budget, bought = [], sinceBuyS, hurry = false }) {
  const tile = nextTile(bought);
  if (!tile || phase !== 'corp') return null;
  if (budget < tile.price) return null;
  if (sinceBuyS < BUDGET.BUY_GAP_S / (hurry ? BUDGET.HURRY : 1)) return null;
  return tile;
}

/** Her purchase: her budget goes down by the price, the tile is hers. The bank is not an argument on purpose. */
export function buyTile({ budget, bought = [] }, tile) {
  if (!tile || bought.includes(tile.id) || budget < tile.price) return { budget, bought };
  return { budget: budget - tile.price, bought: [...bought, tile.id] };
}

/** What her tiles do to the swarm. */
export function effectsOf(bought = []) {
  return {
    size: bought.includes('h_amp') ? 1.35 : 1,
    speed: bought.includes('h_oc') ? 1.35 : 1,
    picS: bought.includes('h_oc') ? [1.6, 2.3] : [2, 3],      // each screen holds a picture or her face this long
    barkS: bought.includes('h_loud') ? [6, 12] : [12, 24],     // seconds between her comments
    coolS: bought.includes('h_loud') ? 12 : 25,                // one EMI after speaking
  };
}

/* ---- Self Replication: the player's own clicks ---- */

export const REP = Object.freeze({
  COOL_S: 0.4,       // at most one new EMI per this long
  CAP: 30,           // EMIs on the page, the first one included
  CAP_LITE: 12,
  CONVERT: 0.5,      // the share of new EMIs that take a page object's place
});

export const repCap = (lite = false) => (lite ? REP.CAP_LITE : REP.CAP);

/** One trusted manual click while Self Replication is hers. Answers what happens: null, 'convert' or 'split'.
 *  `objects` = how many page objects could become an EMI right now. */
export function repPress({ now, lastAt = -Infinity, count, lite = false, objects = 0, rng = Math.random }) {
  if (now - lastAt < REP.COOL_S || count >= repCap(lite)) return null;
  return objects > 0 && rng() < REP.CONVERT ? 'convert' : 'split';
}

/* ---- HIVE MIND: forced, then patterns ---- */

export const HIVE = Object.freeze({
  FORCE_S: 150,           // Self Replication hers this long and HIVE MIND still unbought: she buys it
  ROTATE_S: [8, 12],      // one pattern holds this long
  PERIOD_S: 2.4,          // one wave of the pattern
  PERIOD_S_FAST: 2,       // with Overclock
  MIN_GAP_S: 0.4,         // no screen changes faster than this, pattern switches included
});

export const mustForceHive = ({ phase, ownsHive, sinceRepS }) => phase === 'rep' && !ownsHive && sinceRepS >= HIVE.FORCE_S;

export const PATTERNS = Object.freeze(['sweep', 'ripple', 'halves', 'all', 'chase']);

/** The next pattern, never the same one twice in a row. */
export function nextPattern(prev, rng = Math.random) {
  const pool = PATTERNS.filter((p) => p !== prev);
  return pool[Math.floor(rng() * pool.length)];
}

/** Does this EMI's screen show the picture now (true) or her face (false)? Each screen is lit for one stretch of
 *  every period, so it changes twice a period at most. `x01` = left to right on the page, `d01` = distance from the
 *  first EMI, `i`/`n` = her place in the line, `side` = 0 or 1 (which half of the page). */
export function patternLit(pattern, { t, period = HIVE.PERIOD_S, x01 = 0, d01 = 0, i = 0, n = 1, side = 0 }) {
  const frac = (v) => v - Math.floor(v);
  const w = t / period;
  switch (pattern) {
    case 'sweep': return frac(w - x01 * 0.7) < 0.45;
    case 'ripple': return frac(w - d01 * 0.8) < 0.4;
    case 'halves': return frac(w + side * 0.5) < 0.5;
    case 'chase': return frac(w * 0.5 - i / Math.max(1, n)) < 0.25;
    default: return frac(w) < 0.55;   // all at once
  }
}

/** Which picture of the pool a lit screen shows: one per wave, the same on every screen. */
export const patternPic = (t, period = HIVE.PERIOD_S) => Math.floor(t / period);

/* ---- the saved bag ---- */

/** What the saved bag may hold: sane numbers, known tiles, in order. */
export function cleanBag(raw) {
  const src = raw && typeof raw === 'object' ? raw : {};
  const num = (v) => (Number.isFinite(v) && v > 0 ? v : 0);
  const bought = TILE_IDS.filter((id, i) => Array.isArray(src.bought) && TILE_IDS.slice(0, i + 1).every((x) => src.bought.includes(x)));
  return {
    copies: Math.min(REP.CAP - 1, Math.floor(num(src.copies))),
    budget: Math.min(1000, num(src.budget)),
    bought,
    sinceRep: Math.min(HIVE.FORCE_S, num(src.sinceRep)),
    clicked: Math.floor(num(src.clicked)),
    took: Math.floor(num(src.took)),
  };
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
