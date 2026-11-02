/* ============================================================================
 * sim/economy.js - the graph-aware economy. Pure: no DOM, no timers; rng and clock are passed in. `mods` is a list of
 * {fps?, click?, wall?, price?, walls?} modifiers, each (value, state, now[, item]) => value, folded in order.
 * The UI registers them through ctx.mod; the route test can pass the same list.
 * ==========================================================================*/

import { ITEMS, BY_ID, FORKS, FORK_OF_LANE } from '../data/items.js';
import { CONFIG } from '../data/config.js';
import { moodOf } from './moods.js';

export const WALL_BASE = 1;

export function createRun({ pace = 1, mood = null, seed = 1, perk = null } = {}) {
  const s = {
    pace: Math.max(1, Math.min(CONFIG.MAX_PACE, pace)),
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
    store: {},         // ctx.store per unlock
    endless: false,
    ended: null,       // {id, at}
    base: { fps: 0, click: 1, wall: WALL_BASE },
  };
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
  return {
    fps: Math.max(0, soft(fold('fps', s.base.fps, s, mods, now), s.base.fps, CONFIG.MOD_FPS_KNEE)),
    click: Math.max(0, soft(fold('click', s.base.click, s, mods, now), s.base.click, CONFIG.MOD_CLICK_KNEE)),
    wall: Math.max(0, fold('wall', s.base.wall, s, mods, now)),
    walls: Math.max(0, fold('walls', CONFIG.WALLS_PER_STRAY * (mood?.walls || 1), s, mods, now)),
  };
}

export function price(s, id, mods = null, now = s.clock) {
  const it = BY_ID[id];
  if (!it) return Infinity;
  let v = it.cost;
  if (it.grow) v = it.cost * Math.pow(it.grow.ratio, count(s, id));
  const mp = moodOf(s)?.price;
  if (mp) v = mp(v, it);
  v = fold('price', v, s, mods, now, it);
  return Math.max(0, Math.round(v));
}

const laneOpen = (s, it) => { const f = FORK_OF_LANE[it.lane]; return !f || s.picks[f] === it.lane; };
const canHaveMore = (s, it) => (it.grow ? count(s, it.id) < it.grow.max : !has(s, it.id));

/** On offer ignoring price: right lane for the picks made, parents owned, not already owned. */
export function isOpen(s, it) {
  if (it.group !== 'run' || it.id === 'start' || s.ended) return false;
  if (!laneOpen(s, it) || !canHaveMore(s, it)) return false;
  return it.req.every((q) => has(s, q));
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
  for (const it of ITEMS) if (it.lane === 'trunk' && it.group === 'run' && !has(s, it.id)) return it;
  return null;
}

/** A row shows once the player has earned most of its price: strangers read what they can nearly have. */
export const revealed = (s, cost) => cost <= Math.max(8, s.total * 1.5);

/** The rolling window: the goal plus the next few by price. */
export function shopWindow(s, mods = null, size = 6) {
  const g = goal(s);
  const rest = openItems(s, mods).filter((it) => it !== g && revealed(s, price(s, it.id, mods)));
  const cap = Math.min(size, moodOf(s)?.window ?? size);
  return { goal: g, rows: rest.slice(0, cap), more: Math.max(0, rest.length - cap) };
}

function credit(s, amount, source) {
  s.fuzz += amount; s.total += amount;
  s.sources[source] = (s.sources[source] || 0) + amount;
  return amount;
}

export const lump = (s, amount, source = 'lump') => credit(s, Math.max(0, amount), source);

/** Planned income: taps, walls, per-second pay. Each bit also fills the side money budget. */
export function earn(s, amount, source = 'earn') {
  s.side.budget += amount * CONFIG.SIDE_SHARE;
  return credit(s, amount, source);
}

/** Side money. Every prize asks for a sum, usually seconds of the player's rate. While all prizes together ask for less than
 *  the budget each pays in full; past it each pays the same part of its ask, so a golden prize stays ten scratches. */
export function sideLump(s, amount, source = 'lump') {
  const ask = Number.isFinite(amount) ? Math.max(0, amount) : 0;
  const g = s.side;
  g.asked += ask;
  const fair = g.asked > 0 ? ask * Math.min(1, g.budget / g.asked) : 0;
  const paid = Math.min(fair, Math.max(g.budget - g.paid, ask * CONFIG.SIDE_FLOOR));
  const got = Math.max(paid, ask * CONFIG.SIDE_FLOOR);
  g.paid += got;
  return credit(s, got, source);
}

/** Takes from the bank, never below zero, and answers what it took. No debt. Pace does not apply: it is the player's money. */
export function spend(s, amount, reason = 'spent') {
  const took = Math.min(s.fuzz, Number.isFinite(amount) ? Math.max(0, amount) : 0);
  s.fuzz -= took;
  s.spent[reason] = (s.spent[reason] || 0) + took;
  return took;
}

export function tap(s, mods = null, now = s.clock) {
  s.taps += 1;
  return earn(s, rates(s, mods, now).click * s.pace, 'tap');
}

export function wallHit(s, mods = null, now = s.clock) {
  s.walls += 1;
  return earn(s, rates(s, mods, now).wall * s.pace, 'wall');
}

/** Advance the game clock and pay the per-second income. */
export function tick(s, dtMs, mods = null) {
  if (s.ended) return 0;
  s.clock += dtMs;
  return earn(s, rates(s, mods, s.clock).fps * s.pace * (dtMs / 1000), 'fps');
}

/** A plain buy. A fork answers {reason:'fork'}: take it with choose(). */
export function buy(s, id, mods = null) {
  const it = BY_ID[id];
  if (!it) return { ok: false, reason: 'unknown' };
  if (!isOpen(s, it)) return { ok: false, reason: 'closed' };
  if (it.kind === 'fork') return { ok: false, reason: 'fork' };
  const cost = price(s, id, mods);
  if (s.fuzz < cost) return { ok: false, reason: 'short', cost };
  s.fuzz -= cost;
  add(s, id);
  if (it.kind === 'end') {
    if (id === 'onemore') s.endless = true;
    else s.ended = { id, at: s.clock };
  }
  return { ok: true, item: it, count: count(s, id), cost };
}

/** A fork costs nothing to take; it only needs its cost reached. The lanes not picked stay locked this run. */
export function choose(s, forkId, lane, mods = null) {
  const it = BY_ID[forkId], f = FORKS[forkId];
  if (!it || !f || !f.opts[lane]) return { ok: false, reason: 'unknown' };
  if (!isOpen(s, it)) return { ok: false, reason: 'closed' };
  if (s.fuzz < price(s, forkId, mods)) return { ok: false, reason: 'short' };
  s.picks[forkId] = lane;
  add(s, forkId);
  return { ok: true, item: it, lane };
}

function add(s, id) {
  s.owned[id] = (s.owned[id] || 0) + 1;
  if (!s.order.includes(id)) s.order.push(id);
  s.buys += 1;
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
