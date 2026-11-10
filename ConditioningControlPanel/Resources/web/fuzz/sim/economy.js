/* ============================================================================
 * sim/economy.js - the graph-aware economy. Pure: no DOM, no timers; rng and clock are passed in. `mods` is a list of
 * {fps?, click?, wall?, price?, walls?} modifiers, each (value, state, now[, item]) => value, folded in order.
 * The UI registers them through ctx.mod; the route test can pass the same list.
 * ==========================================================================*/

import { ITEMS, BY_ID, FORKS, FORK_OF_LANE } from '../data/items.js';
import { CONFIG } from '../data/config.js';
import { moodOf } from './moods.js';
import { accrueActivities, trunkBoost, RANK_BOUNCE, RANK_TAP, RANK_PASSIVE } from './journey-effects.js';
import { ridersOf } from '../data/folds.js';
import { EMI_LIFT } from './tap-rate.js';

export const WALL_BASE = 1;

/** prestige: a Renewal Sky run stamp (sim/prestige.js runStamp). Its pay multiplier rides on pace, which every income path
 *  already carries; its price scale and minutes are read by the Journey plan. null = a run that plays as Depth 0. */
export function createRun({ pace = 1, mood = null, seed = 1, perk = null, prestige = null } = {}) {
  const s = {
    pace: Math.max(1, Math.min(CONFIG.MAX_PACE, pace)) * (prestige?.pay > 0 ? prestige.pay : 1),
    seed,
    mood,
    clock: 0,          // ms played: stops on pause, suspend and end
    fuzz: 0,
    total: 0,          // ever earned this run
    taps: 0,
    walls: 0,
    buys: 0,
    owned: { start: 1 },
    order: ['start'],
    picks: {},
    sources: {},
    spent: {},         // ctx.spend by reason
    side: { budget: 0, asked: 0, paid: 0 },  // side money: see sideLump
    toys: {},          // toy budgets, id -> {budget, asked, paid}: see CONFIG.TOY_SHARE
    inc: { k: 0, b: [], sum: 0 },  // recent income in 10 s buckets, caps the toy budgets
    store: {},         // ctx.store per unlock
    off: {},           // Control Panel: unlock id -> 1 while switched off (sim/switches.js)
    endless: false,
    ended: null,       // {id, at}
    base: { fps: 0, click: 1, wall: WALL_BASE },
  };
  if (prestige) s.prestige = prestige;
  if (perk && BY_ID[perk] && BY_ID[perk].lane === 'look') { s.owned[perk] = 1; s.order.push(perk); }
  recalc(s);
  return s;
}

export const has = (s, id) => (s.owned[id] || 0) > 0;
export const count = (s, id) => s.owned[id] || 0;
export const pick = (s, f) => s.picks[f] || null;

/** Income from what is owned, after the mood. Cached in s.base, rebuilt on every buy. A stacking row pays once:
 *  its count is how many bouncing buttons there are, not how much each wall pays. */
export function recalc(s) {
  let fps = 0, click = 1, wall = WALL_BASE;
  const pay = moodOf(s)?.pay;
  for (const id of s.order) {
    const it = BY_ID[id];
    if (!it) continue;
    if (it.fps) fps += pay ? pay(it.fps, it) : it.fps;
    if (it.click) click += pay ? pay(it.click, it) : it.click;
    if (it.wall) wall += pay ? pay(it.wall, it) : it.wall;
  }
  s.base = { fps, click, wall };
  return s.base;
}

function fold(field, value, s, mods, now, item) {
  if (!mods) return value;
  for (const m of mods) {
    const f = m[field];
    if (!f) continue;
    const next = item ? f(value, s, now, item) : f(value, s, now);
    if (Number.isFinite(next)) value = next;
  }
  return value;
}

/** Modifiers past the knee keep counting, at MOD_SLOPE of their strength: a run stays about 30 minutes whichever stack.
 *  The small constant leaves the first minutes alone, where base pay is a handful. */
const soft = (value, base, knee) => {
  const top = base * knee + 10;
  return value <= top ? value : top + (value - top) * CONFIG.MOD_SLOPE;
};

/** Everything that decides income right now. walls = hits per second per bouncing button. */
export function rates(s, mods = null, now = s.clock) {
  const mood = moodOf(s);
  let bounceShare = CONFIG.BOUNCE_PASSIVE_SHARE, tapShare = CONFIG.ROUND_TAP_PASSIVE_SHARE, passiveBonus = 0;
  const journey = s.store.journey;
  if (journey) {
    const { ranks, specs } = journey;
    bounceShare += RANK_BOUNCE * (ranks.stray || 0);
    tapShare += RANK_TAP * (ranks.rounded || 0);
    if (specs.spec_rebounds === 'steady') bounceShare += .002;
    if (specs.spec_mix === 'bounce') bounceShare += .002;
    if (specs.spec_mix === 'tap') tapShare += .002;
    const passiveRanks = (ranks.ticker || 0) + (ranks.e_spiral || 0) + (ranks.a_hum || 0);
    passiveBonus = s.base.fps * RANK_PASSIVE * passiveRanks;
  }
  if (journey) { tapShare += CONFIG.JOURNEY_TAP_SHARE; for (const id of CONFIG.TAP_SHARE_LOOKS) if (has(s, id)) tapShare += CONFIG.TAP_SHARE_PER_LOOK; }
  /* Timed buffs fold after the knee, so they multiply in full (see CONFIG.MOD_FPS_KNEE). So do `post` mods: the
   * player's own permanent Renewal Sky multipliers (ui/unlocks/prestige/boosts.js), which awayRate keeps. */
  const steady = mods && mods.some((m) => m.timed || m.post) ? mods.filter((m) => !m.timed && !m.post) : mods;
  const timed = steady === mods ? null : mods.filter((m) => m.timed || m.post);
  const fps = Math.max(0, fold('fps', soft(fold('fps', s.base.fps, s, steady, now), s.base.fps, CONFIG.MOD_FPS_KNEE), s, timed, now));
  const click = Math.max(0, fold('click', soft(fold('click', s.base.click, s, steady, now), s.base.click, CONFIG.MOD_CLICK_KNEE), s, timed, now));
  const wall = Math.max(0, fold('wall', s.base.wall, s, mods, now));
  /* A click buff also multiplies the share of passive pay a manual tap carries; so do per-second and wall buffs the rank
   * bonuses (a rank pays in seconds of base pay, so a Renewal Sky multiplier must reach it too). */
  const clickBuff = timed ? fold('click', 1, s, timed, now) : 1;
  const fpsBuff = timed && passiveBonus ? fold('fps', 1, s, timed, now) : 1;
  const wallBuff = timed ? fold('wall', 1, s, timed, now) : 1;
  /* The late trunk shows multiply every income path after everything else (sim/journey-effects.js trunkBoost). */
  const boost = trunkBoost(s);
  return {
    fps: (fps + passiveBonus * fpsBuff) * boost, click: click * boost, wall: wall * boost,
    manualClick: (click + (click > 0 && has(s, 'rounded') ? s.base.fps * tapShare * clickBuff : 0)) * boost,
    bounce: (wall + (wall > 0 && has(s, 'stray') ? s.base.fps * bounceShare * wallBuff : 0)) * boost,
    walls: Math.max(0, fold('walls', CONFIG.WALLS_PER_STRAY * (mood?.walls || 1), s, mods, now)),
  };
}

/** A lead's price carries every rider it still brings (data/folds.js). Every price follows CONFIG.PRICE_SCALE. */
export function price(s, id, mods = null, now = s.clock) {
  const it = BY_ID[id];
  if (!it) return Infinity;
  let v = unitPrice(s, it, mods, now);
  for (const r of ridersFor(s, id)) v += unitPrice(s, BY_ID[r], mods, now);
  return Math.max(0, Math.round(v));
}

/** The riders a purchase of id would bring right now. */
export const ridersFor = (s, id) => ridersOf(id).filter((r) => !has(s, r) && BY_ID[r]);

/** The scale comes first, so a mood or an unlock that sets a price has the last word. */
function unitPrice(s, it, mods, now) {
  let v = it.cost * CONFIG.PRICE_SCALE;
  if (it.grow) v *= Math.pow(it.grow.ratio, count(s, it.id));
  return adjustPrice(s, it, v, mods, now);
}

export function adjustPrice(s, it, v, mods, now = s.clock) {
  const mp = moodOf(s)?.price;
  if (mp) v = mp(v, it);
  v = fold('price', v, s, mods, now, it);
  return Math.max(0, v);
}

const laneOpen = (s, it) => { const f = FORK_OF_LANE[it.lane]; return !f || s.picks[f] === it.lane; };
const canHaveMore = (s, it) => (it.grow ? count(s, it.id) < it.grow.max : !has(s, it.id));

/** Parents owned, right lane, not owned yet: the plain rule, before the one-ending rule below. */
function reached(s, it) {
  if (it.group !== 'run' || it.id === 'start' || s.ended) return false;
  if (!laneOpen(s, it) || !canHaveMore(s, it)) return false;
  return it.req.every((q) => has(s, q)) && (!it.any || it.any.some((q) => has(s, q)));
}

/* ONE ENDING A RUN (owner 2026-11-10: "only 1 ending should pop up and only at the end of the run"; pick = the first ending
 * chain finished). The ending held for this run is the one whose chain the player finished first (its parent bought
 * earliest); Go Outside when no lane chain finished. It is offered only once the main path is done (Drop's old parents
 * owned) and no other ending is ever offered. Endless Mode and Drop are retired from the shop (owner 2026-11-10; Drop's
 * perk and card stay for players who already have it). Derived from the purchase order, so nothing is saved. */
export const OFF_SHELF = new Set(['onemore', 'drop']);
/** Endings on the main path itself (Go Outside hangs off Spiral Overlay) are not a chain the player builds. */
const MAIN_PATH_LANES = new Set(['trunk', 'exit']);
export const FALLBACK_ENDING = 'outside';
export const mainPathDone = (s) => BY_ID.drop.req.every((q) => has(s, q));
export function heldEnding(s) {
  let best = null, at = Infinity;
  for (const it of ITEMS) {
    if (it.kind !== 'end' || MAIN_PATH_LANES.has(it.lane) || OFF_SHELF.has(it.id) || !reached(s, it)) continue;
    const i = Math.max(...it.req.map((q) => s.order.indexOf(q)));
    if (i < at) { at = i; best = it.id; }
  }
  return best || FALLBACK_ENDING;
}

/** On offer ignoring price: right lane for the picks made, parents owned, not already owned; an ending only as above. */
export function isOpen(s, it) {
  if (!reached(s, it)) return false;
  if (it.kind !== 'end') return true;
  return !OFF_SHELF.has(it.id) && mainPathDone(s) && heldEnding(s) === it.id;
}

/** Shuffled Shop: from the second run on (a mood means not the first), open side rows sort by a price scaled per run. */
const sideKey = (s, x) => (s.mood && x.it.lane === 'side' ? x.c * (0.3 + ((Math.imul(s.seed ^ x.it.cost, 2654435761) >>> 0) / 2 ** 32) * 1.6) : x.c);

export const openItems = (s, mods = null) => ITEMS.filter((it) => isOpen(s, it))
  .map((it) => ({ it, c: price(s, it.id, mods) }))
  .sort((a, b) => sideKey(s, a) - sideKey(s, b) || a.it.cost - b.it.cost)
  .map((x) => x.it);

/** The next row on the main path: always in sight, always one press away. */
export function goal(s) {
  if (s.ended) return null;
  for (const it of ITEMS) {
    if (it.lane !== 'trunk' || it.group !== 'run' || has(s, it.id)) continue;
    if (it.kind !== 'end') return it;
    const held = BY_ID[heldEnding(s)];   // one ending a run: the held ending takes the main path's last place
    return held && isOpen(s, held) ? held : null;
  }
  return null;
}

/** A row shows once the player has earned most of its price: strangers read what they can nearly have. */
export const revealed = (s, cost) => cost <= Math.max(8, s.total * 1.5);

/** The rolling window: the goal plus the next few by price. */
export function shopWindow(s, mods = null, size = 6, featured = []) {
  const g = goal(s);
  const rest = openItems(s, mods).filter((it) => it !== g && revealed(s, price(s, it.id, mods)));
  if (featured.length) rest.sort((a, b) => Number(featured.includes(b.id)) - Number(featured.includes(a.id)));
  const cap = Math.min(size, moodOf(s)?.window ?? size);
  return { goal: g, rows: rest.slice(0, cap), more: Math.max(0, rest.length - cap) };
}

function credit(s, amount, source) {
  s.fuzz += amount; s.total += amount;
  s.sources[source] = (s.sources[source] || 0) + amount;
  return amount;
}

export const lump = (s, amount, source = 'lump') => credit(s, Math.max(0, amount), source);

/** Planned income: taps, walls, per-second pay. Each bit also fills the side money budget and the toy budgets. */
export function earn(s, amount, source = 'earn') {
  s.side.budget += amount * CONFIG.SIDE_SHARE;
  accrueToys(s, amount);
  return credit(s, amount, source);
}

/* ---- toy budgets (CONFIG.TOY_SHARE) ---- */
const TOY_SHARES = Object.entries(CONFIG.TOY_SHARE);
const INC_BUCKET_MS = 10000;
const INC_BUCKETS = Math.max(1, Math.round(CONFIG.TOY_CAP_S * 1000 / INC_BUCKET_MS));

/** True when ctx.pay from this source draws on its own toy budget instead of the shared side money. */
export const isToySource = (source) => Object.hasOwn(CONFIG.TOY_SHARE, source);

/** One toy's budget: {budget, asked, paid}, the same sum the shared side money keeps, but for that toy alone. */
export function toyPot(s, id) {
  s.toys ||= {};
  return (s.toys[id] ||= { budget: 0, asked: 0, paid: 0 });
}

/** Income of the last TOY_CAP_S of play, in INC_BUCKET_MS buckets on the run clock (paused time earns nothing). */
function recentIncome(s, amount) {
  const w = (s.inc ||= { k: 0, b: [], sum: 0 });
  const k = Math.floor((s.clock || 0) / INC_BUCKET_MS);
  if (k !== w.k) {
    if (k < w.k || k - w.k >= INC_BUCKETS) { w.b = []; w.sum = 0; }
    else for (let j = w.k + 1; j <= k; j++) { const i = j % INC_BUCKETS; w.sum -= w.b[i] || 0; w.b[i] = 0; }
    w.k = k;
  }
  const i = k % INC_BUCKETS;
  w.b[i] = (w.b[i] || 0) + amount;
  w.sum = Math.max(0, w.sum + amount);
  return w.sum;
}

/** Fills each owned, switched-on toy's budget with its share of this income, up to TOY_CAP_S of its share of recent
 *  income unspent. A toy not owned or switched off in the Control Panel gets nothing, and nothing it misses goes elsewhere. */
function accrueToys(s, amount) {
  if (!(amount > 0)) return;
  const recent = recentIncome(s, amount);
  for (const [id, share] of TOY_SHARES) {
    if (!(s.owned?.[id] > 0) || s.off?.[id]) continue;
    const fresh = !s.toys?.[id];
    const t = toyPot(s, id), cap = share * recent;
    /* A toy just bought starts with TOY_START_S of its share, so its first coin or spin is not a dud. */
    if (fresh) t.budget = cap * Math.min(1, CONFIG.TOY_START_S / CONFIG.TOY_CAP_S);
    if (t.budget - t.paid < cap) t.budget = Math.min(t.budget + amount * share, t.paid + cap);
  }
}

/** What one toy prize pays: like the shared side money, every ask of this toy gets the same part of it while the toy asks
 *  for more than its budget, never more than the budget it has left, and never under SIDE_FLOOR of the ask. */
function quoteToy(s, ask, source) {
  const t = s.toys?.[source] || { budget: 0, asked: 0, paid: 0 };
  const requested = t.asked + ask;
  const fair = requested > 0 ? ask * Math.min(1, t.budget / requested) : 0;
  return Math.max(Math.min(fair, Math.max(0, t.budget - t.paid)), ask * CONFIG.SIDE_FLOOR);
}

/** Side money. Every prize asks for a sum, usually seconds of the player's rate. While all prizes together ask for less than
 *  the budget each pays in full; past it each pays the same part of its ask, so a golden prize stays ten scratches. A source
 *  in CONFIG.SIDE_WEIGHT counts as more of itself on both sides of that sum: it keeps a bigger part of its ask, and takes it
 *  even when the rest of the budget has just gone. Its part can never be more than all the budget there is, so the others
 *  fall to the floor for a moment and the sum still holds. */
export function quoteSideLump(s, amount, source = 'lump') {
  const ask = Number.isFinite(amount) ? Math.max(0, amount) : 0;
  if (CONFIG.FULL_PRIZE_SOURCES.includes(source)) return ask;
  if (isToySource(source)) return quoteToy(s, ask, source);
  const g = s.side;
  const w = CONFIG.SIDE_WEIGHT[source] || 1;
  const requested = g.asked + ask * w;
  const fair = requested > 0 ? ask * Math.min(1, w * g.budget / requested) : 0;
  const paid = w > 1 ? fair : Math.min(fair, Math.max(g.budget - g.paid, ask * CONFIG.SIDE_FLOOR));
  return Math.max(paid, ask * CONFIG.SIDE_FLOOR);
}

export function sideLump(s, amount, source = 'lump') {
  const got = quoteSideLump(s, amount, source);
  const ask = Number.isFinite(amount) ? Math.max(0, amount) : 0;
  if (isToySource(source)) {
    const t = toyPot(s, source);
    t.asked += ask; t.paid += got;
  } else if (!CONFIG.FULL_PRIZE_SOURCES.includes(source)) {
    s.side.asked += ask * (CONFIG.SIDE_WEIGHT[source] || 1);
    s.side.paid += got;
  }
  return credit(s, got, source);
}

/** Takes from the bank, never below zero, and answers what it took. No debt. Pace does not apply: it is the player's money. */
export function spend(s, amount, reason = 'spent') {
  const took = Math.min(s.fuzz, Number.isFinite(amount) ? Math.max(0, amount) : 0);
  s.fuzz -= took;
  s.spent[reason] = (s.spent[reason] || 0) + took;
  return took;
}

export function tap(s, mods = null, now = s.clock, auto = false, mult = 1) {
  s.taps += 1;
  const rate = rates(s, mods, now);
  /* mult: the EMI button's bell curve (sim/tap-rate.js). Manual presses only, never under the base, capped at its peak. */
  const bell = auto ? 1 : Math.min(1 + EMI_LIFT, Math.max(1, Number.isFinite(mult) ? mult : 1));
  return earn(s, (auto ? rate.click : rate.manualClick) * s.pace * bell, 'tap');
}

export function wallHit(s, mods = null, now = s.clock, owner = 'stray', corner = false) {
  s.walls += 1;
  const rate = rates(s, mods, now);
  const jackpot = corner && owner === 'stray' && rate.wall > 0 && s.store.journey?.specs?.spec_rebounds === 'corner' ? s.base.fps * .2 : 0;
  return earn(s, ((owner === 'stray' ? rate.bounce : rate.wall) + jackpot) * s.pace, 'wall');
}

/** Advance the game clock and pay the per-second income. */
export function tick(s, dtMs, mods = null) {
  if (s.ended) return 0;
  s.clock += dtMs;
  /* The collect banks grow with the player's permanent multipliers (`post` mods: the Renewal Sky), never a short buff. */
  if (s.store.journey) accrueActivities(s, dtMs / 1000, mods ? fold('fps', 1, s, mods.filter((m) => m.post), s.clock) : 1);
  return earn(s, rates(s, mods, s.clock).fps * s.pace * (dtMs / 1000), 'fps');
}

/** Per-second pay for time away with the page open. Timed buffs never count, and neither does a bonus that runs out on
 *  the run clock (a graded review, a paid paywall): the rate is the lower of now and the same run AWAY_CAP_MS later, so a
 *  bonus that grows while nobody touches anything stays where it was. */
export function awayRate(s, mods = null) {
  if (s.ended) return 0;
  const steady = mods ? mods.filter((m) => !m.timed) : mods;
  const later = { ...s, clock: s.clock + CONFIG.AWAY_CAP_MS };
  return Math.max(0, Math.min(rates(s, steady, s.clock).fps, rates(later, steady, later.clock).fps));
}

/** Pays ms of away time at awayRate. Away is not play: the run clock, the activity banks and the side money budget stay
 *  where they are, so nothing timed burns down and no prize pays bigger for it. Counted with the per-second pay. */
/** One absence pays at most this: AWAY_CAP_MS, raised by an `awayCap` mod (Night Shift). */
export const awayCap = (s, mods) => fold('awayCap', CONFIG.AWAY_CAP_MS, s, mods);

export function away(s, mods, ms) {
  const t = Number.isFinite(ms) ? Math.min(awayCap(s, mods), Math.max(0, ms)) : 0;
  if (!t || s.ended) return 0;
  return lump(s, fold('away', awayRate(s, mods), s, mods, s.clock) * s.pace * (t / 1000), 'fps');
}

/** A plain buy. A fork answers {reason:'fork'}: take it with choose(). */
export function buy(s, id, mods = null, fixedCost) {
  const it = BY_ID[id];
  if (!it) return { ok: false, reason: 'unknown' };
  if (!isOpen(s, it)) return { ok: false, reason: 'closed' };
  if (it.kind === 'fork') return { ok: false, reason: 'fork' };
  const cost = fixedCost ?? price(s, id, mods);
  if (s.fuzz < cost) return { ok: false, reason: 'short', cost };
  s.fuzz -= cost;
  const riders = ridersFor(s, id);
  add(s, id);
  for (const r of riders) add(s, r, false);
  if (it.kind === 'end') {
    if (id === 'onemore') s.endless = true;
    else s.ended = { id, at: s.clock };
  }
  return { ok: true, item: it, count: count(s, id), cost, riders };
}

/** A fork costs nothing to take; it only needs its cost reached. The lanes not picked stay locked this run. */
export function choose(s, forkId, lane, mods = null, threshold, funds = s.fuzz) {
  const it = BY_ID[forkId], f = FORKS[forkId];
  if (!it || !f || !f.opts[lane]) return { ok: false, reason: 'unknown' };
  if (!isOpen(s, it)) return { ok: false, reason: 'closed' };
  if (funds < (threshold ?? price(s, forkId, mods))) return { ok: false, reason: 'short' };
  s.picks[forkId] = lane;
  add(s, forkId);
  return { ok: true, item: it, lane };
}

/** A rider comes with its lead's purchase: it is owned, but it is not a purchase of its own. */
function add(s, id, purchase = true) {
  s.owned[id] = (s.owned[id] || 0) + 1;
  if (!s.order.includes(id)) s.order.push(id);
  if (purchase) s.buys += 1;
  recalc(s);
}

/** Dev door: own an item and everything above it, free. Picks the lanes its parents need. Answers the ids it added. */
export function grant(s, id, added = []) {
  const it = BY_ID[id];
  if (!it || it.group !== 'run' || has(s, id)) return added;
  const f = FORK_OF_LANE[it.lane];
  if (f) {
    if (s.picks[f] && s.picks[f] !== it.lane) return added;
    s.picks[f] = it.lane;
    grant(s, f, added);
  }
  for (const q of it.req) grant(s, q, added);
  if (it.kind === 'fork' && !s.picks[id]) s.picks[id] = Object.keys(FORKS[id].opts)[0];
  add(s, id);
  added.push(id);
  return added;
}

/** The route as lanes picked, in fork order. */
export const routeOf = (s) => Object.keys(FORKS).map((f) => s.picks[f]).filter(Boolean);
