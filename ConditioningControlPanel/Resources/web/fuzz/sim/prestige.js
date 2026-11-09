/* ============================================================================
 * sim/prestige.js - Renewal Sky rules (data in data/prestige.js, brief handoff-1109/TC-PRESTIGE-WIRE-BRIEF.md). Pure: no
 * DOM, no timers. The prestige record lives in the life save (life.prestige); a run carries a small stamp of what it
 * started with (run.prestige, made by runStamp) so a reload mid-run keeps its numbers.
 *
 * Money model: every star raises the run multiplier (mult(): the Sky's pill). Income AND prices scale with it, so a
 * renewed run is never moot; the run gets shorter only through T_RUN (Journey timing). What a star does on top of that
 * (taps, idle, prizes...) is each lane's own entry. A lit ending's plain perk (Drop +5% pay) is a pure gain: it scales
 * pay, not prices.
 * ==========================================================================*/

import {
  LANES, LANE_BY_ID, STAR_BY_ID, ENDING_STARS, CORE, FINE_RULES, FINE_SLOTS, FULL_CONTRACT, WORD_BANK,
  T_RUN, P_RUN, MAX_DEPTH, STEPS,
} from '../data/prestige.js';
import { BY_ID, FORKS } from '../data/items.js';
import { planTotal } from './journey.js';
import { perks as ghostPerks } from './ghost.js';
import * as BOOST from '../ui/unlocks/prestige/boosts.js';

const ENDING_BY_ID = Object.fromEntries(ENDING_STARS.map((e) => [e.id, e]));
const RULE_BY_ID = Object.fromEntries(FINE_RULES.map((r) => [r.id, r]));
/** What counts as a toy for Keepsake (head_7 / head_8): the side games a run can own. */
export const TOY_IDS = Object.freeze(['toy_box', 'wheel', 'mini_breakout', 'mini_slots', 'dispenser', 'mini_race', 'mini_deep', 'levels', 'quiz']
  .filter((id) => BY_ID[id]));

const num = (v, lo = 0, hi = Number.MAX_SAFE_INTEGER) => (Number.isFinite(v) ? Math.min(hi, Math.max(lo, v)) : lo);
const obj = (v) => (v && typeof v === 'object' && !Array.isArray(v) ? v : {});
const strs = (v, ok, max) => (Array.isArray(v) ? [...new Set(v.filter((x) => typeof x === 'string' && ok(x)))].slice(0, max) : []);

export function createPrestige() {
  return {
    depth: 0, dust: 0, spent: 0, own: {}, lit: {}, core: false,
    next: { rules: [], words: [], keep: [] },
    store: Object.fromEntries(LANES.map((l) => [l.id, {}])),
    last: null,
  };
}

function parseLast(v) {
  if (!v || typeof v !== 'object') return null;
  const route = {};
  for (const [f, lane] of Object.entries(obj(v.route))) if (FORKS[f]?.opts[lane]) route[f] = lane;
  return { route, toys: strs(v.toys, (id) => TOY_IDS.includes(id), TOY_IDS.length), ms: Math.round(num(v.ms)) };
}

/** Never throws; unknown ids dropped, numbers clamped. A save without prestige reads as Depth 0. */
export function parsePrestige(src) {
  const p = createPrestige();
  if (!src || typeof src !== 'object') return p;
  p.depth = Math.floor(num(src.depth, 0, MAX_DEPTH));
  p.dust = Math.floor(num(src.dust, 0, 1e15));
  p.spent = Math.floor(num(src.spent, 0, 1e15));
  for (const id of Object.keys(obj(src.own))) if (STAR_BY_ID[id] && src.own[id]) p.own[id] = 1;
  for (const id of Object.keys(obj(src.lit))) if (ENDING_BY_ID[id] && src.lit[id]) p.lit[id] = 1;
  p.core = !!src.core;
  const next = obj(src.next);
  p.next = {
    rules: strs(next.rules, (id) => !!RULE_BY_ID[id], FINE_RULES.length),
    words: strs(next.words, (w) => WORD_BANK.includes(w), 3),
    keep: strs(next.keep, (id) => TOY_IDS.includes(id), 2),
  };
  for (const [lane, bag] of Object.entries(obj(src.store))) if ((LANE_BY_ID[lane] || lane === 'looks') && bag && typeof bag === 'object' && !Array.isArray(bag)) p.store[lane] = bag;
  p.last = parseLast(src.last);
  return p;
}

export const has = (p, starId) => !!p?.own?.[starId];
export const lit = (p, endingId) => !!p?.lit?.[endingId];

/** Stars owned in a row from the tip: 0..10. */
export function level(p, laneId) {
  let n = 0;
  while (n < STEPS && has(p, `${laneId}_${n + 1}`)) n += 1;
  return n;
}

export function canBuy(p, starId) {
  const s = STAR_BY_ID[starId];
  if (!s) return { ok: false, reason: 'unknown' };
  if (has(p, starId)) return { ok: false, reason: 'owned' };
  if (p.depth < s.depth) return { ok: false, reason: 'depth' };
  if (s.n > 1 && !has(p, `${s.lane}_${s.n - 1}`)) return { ok: false, reason: 'previous' };
  if (p.dust < s.cost) return { ok: false, reason: 'dust' };
  return { ok: true, reason: null };
}

export function buyStar(p, starId) {
  const c = canBuy(p, starId);
  if (!c.ok) return c;
  const cost = STAR_BY_ID[starId].cost;
  p.dust -= cost; p.spent += cost; p.own[starId] = 1;
  return { ok: true, cost };
}

export function canCore(p) {
  if (p.core) return { ok: false, reason: 'owned' };
  if (p.depth < MAX_DEPTH) return { ok: false, reason: 'depth' };
  if (!LANES.every((l) => level(p, l.id) === STEPS)) return { ok: false, reason: 'lanes' };
  if (p.dust < CORE.cost) return { ok: false, reason: 'dust' };
  return { ok: true, reason: null };
}

export function buyCore(p) {
  const c = canCore(p);
  if (!c.ok) return c;
  p.dust -= CORE.cost; p.spent += CORE.cost; p.core = true;
  return { ok: true, cost: CORE.cost };
}

/* ---- Fine Print ---- */
export function rulesOpen(p) { return FINE_RULES.filter((r) => has(p, r.star)).map((r) => r.id); }
export function fineSlots(p) { return has(p, FULL_CONTRACT.star) ? FINE_RULES.length : FINE_SLOTS.filter((id) => has(p, id)).length; }
/** The rules a run really carries: open ones only, at most fineSlots. */
export const activeRules = (rules, p) => strs(rules, (id) => rulesOpen(p).includes(id), fineSlots(p));
export function fineMult(rules, p) {
  const live = activeRules(rules, p);
  if (!live.length) return 1;
  if (has(p, FULL_CONTRACT.star) && live.length === FINE_RULES.length) return FULL_CONTRACT.mult;
  return live.reduce((m, id) => {
    const r = RULE_BY_ID[id];
    return m * (r.better && has(p, r.better.star) ? r.better.mult : r.mult);
  }, 1);
}

/* ---- the run multiplier (the Sky's pill, as the approved mockup counts it) ---- */
const STAR_STEP = Object.freeze({ pay: 1.15 });
const STEP_ELSE = 1.08;
export function starMult(p) {
  return LANES.reduce((m, l) => m * (STAR_STEP[l.id] || STEP_ELSE) ** level(p, l.id), 1);
}
/** The Sky's pill: stars plus 5% for every lit ending. */
export const mult = (p) => starMult(p) * (1 + 0.05 * Object.keys(p?.lit || {}).length);
/** What a typical player has by Depth d: the dust factor compares against it. Fitted to the cheapest-first player of
 *  dev/prestige-measure.mjs, who owns about every star the Depth opens (mult about 1.97^d). */
export const expectMult = (d) => 2 ** (EXPECT_EXP * d);
const EXPECT_EXP = 0.97;

export const runMinutes = (p) => (p.core ? T_RUN[T_RUN.length - 1] : T_RUN[Math.min(p.depth, T_RUN.length - 1)]);

/* ---- the price side (Balance, 2026-11-09; measured by dev/prestige-measure.mjs) ----
 * The Pay, Idle, Active, Lucky, Trigger Words and Ghost entries pay `post: true` on top of the soft cap: priced at the star
 * multiplier alone, a full sky ran about 15 times faster than T_RUN (3 minutes at Depth 10). Every star still pays what its
 * line says; prices carry that speed instead (boostSpeed), so the numbers grow and the run lasts about T_RUN[depth].
 * Prices follow the REFERENCE sky of the Depth (every star it opens), not only what is owned: a player behind it runs slower,
 * so the stars bought for the next run are a real jump (BEHIND), but never slower than the first run (T_RUN[0]).
 * SPEED_K: how much of each lane's factor reaches a staying player's whole income. AHEAD: the share of boost speed left
 * unpriced, so the reference sky runs slightly ahead of T_RUN early and lands near 45 minutes at Depth 10. */
const SPEED_K = Object.freeze({ idle: 0.5, active: 0.6, hands: 0.15, side: 0.11 });
const AHEAD = 0.12;
const BEHIND = 0.15;
export function boostSpeed(p) {
  if (!p || !Object.keys(p.own || {}).length) return 1;
  const v = { has: (id) => has(p, id), dust: p.dust };
  const pay = BOOST.payMult(v);
  const idle = BOOST.idleMult(v) ** SPEED_K.idle;
  const tap = (BOOST.tapMult(v) * (has(p, 'active_6') ? 1 + BOOST.COMBO_MAX : 1)) ** SPEED_K.active;
  const g = ghostPerks(level(p, 'ghost'));
  const ghost = (1 + g.beat) * (1 + SPEED_K.hands * g.taps);
  const side = 1 + SPEED_K.side * Math.min(1, (BOOST.prizeMult(v) - 1) + (has(p, 'trig_1') ? 0.5 : 0) + (has(p, 'active_7') ? 0.5 : 0));
  return (pay * idle * tap * ghost * side) ** (1 - AHEAD);
}
const ownPrice = (p) => starMult(p) * boostSpeed(p);
const refPrices = new Map();
/** The price scale of the reference sky at Depth d: every star the Depth opens, nothing saved. */
export function refPrice(d) {
  const k = Math.max(0, Math.min(MAX_DEPTH, Math.floor(d) || 0));
  if (!refPrices.has(k)) {
    const q = createPrestige();
    q.depth = k;
    for (const l of LANES) for (const s of l.stars) if (s.n <= k) q.own[s.id] = 1;
    refPrices.set(k, ownPrice(q));
  }
  return refPrices.get(k);
}

/** { payMult, priceMult, minutes }. Depth 0 with no stars is exactly { 1, 1, 90 }. Prices carry the boost speed; pay does
 *  not (the boost entries pay it themselves). */
export function runMods(p) {
  const m = starMult(p), minutes = runMinutes(p);
  const own = ownPrice(p);
  const behind = Math.min(T_RUN[0] / minutes, Math.max(1, refPrice(p.depth) / own) ** BEHIND);
  return { payMult: m * (lit(p, 'drop') ? 1.05 : 1), priceMult: own * behind, minutes };
}

const MONEY = [['head_6', 100000], ['head_3', 10000], ['head_1', 1000]];
/** What a new run starts with. The engine applies it once, at the first second of a fresh run (ui/engine.js). */
export function runStamp(p) {
  const m = runMods(p);
  const prepick = {};
  if (p.last && has(p, 'head_10')) Object.assign(prepick, p.last.route);
  else if (p.last?.route?.f1 && has(p, 'head_4')) prepick.f1 = p.last.route.f1;
  const keepSlots = has(p, 'head_8') ? 2 : has(p, 'head_7') ? 1 : 0;
  return {
    depth: p.depth,
    pay: m.payMult, price: m.priceMult, minutes: m.minutes,
    start: (MONEY.find(([id]) => has(p, id)) || [0, 0])[1],
    free: has(p, 'head_5') ? 6 : has(p, 'head_2') ? 3 : 0,
    skip: has(p, 'head_9'),
    prepick,
    freeChange: has(p, 'head_10'),
    keep: p.next.keep.filter((id) => p.last?.toys?.includes(id)).slice(0, keepSlots),
    wheel: lit(p, 'p_life'),
    rules: activeRules(p.next.rules, p),
    words: [...p.next.words],
    gift: 0,       // money the run was handed (start money, chapter skip): never counts as score
    renewed: false,
  };
}

/** A run stamp from a save: clamped, unknown ids dropped. null = an old run, which plays as Depth 0. */
export function parseStamp(v) {
  if (!v || typeof v !== 'object') return null;
  const prepick = {};
  for (const [f, lane] of Object.entries(obj(v.prepick))) if (FORKS[f]?.opts[lane]) prepick[f] = lane;
  return {
    depth: Math.floor(num(v.depth, 0, MAX_DEPTH)),
    pay: num(v.pay, 1, 1e6), price: num(v.price, 1, 1e6), minutes: num(v.minutes, 1, 600) || T_RUN[0],
    start: num(v.start), free: Math.floor(num(v.free, 0, 99)), skip: !!v.skip, prepick, freeChange: !!v.freeChange,
    keep: strs(v.keep, (id) => TOY_IDS.includes(id), 2), wheel: !!v.wheel,
    rules: strs(v.rules, (id) => !!RULE_BY_ID[id], FINE_RULES.length),
    words: strs(v.words, (w) => WORD_BANK.includes(w), 3),
    gift: num(v.gift), renewed: !!v.renewed,
  };
}

/** Stardust for renewing this run now. A normal finished run at Depth d earns about P_RUN[d]: its score (what it earned,
 *  less what it was handed, over its price scale) against the Journey plan's spend for its length, square-rooted so a
 *  short run still pays something; times the mockup's multiplier factor and the Fine Print rules it carried. */
export function dustFor(run, p) {
  const st = run?.prestige || null;
  const d = Math.min(st ? st.depth : p.depth, P_RUN.length - 1);
  const minutes = st?.minutes || T_RUN[0];
  const score = Math.max(0, (run?.total || 0) - (st?.gift || 0)) / (st?.price || 1);
  if (!(score > 0)) return 0;
  const ref = planTotal(minutes) || 1;
  const scoreF = Math.min(1.5, Math.sqrt(score / ref));
  const multF = Math.min(1.6, Math.max(0.6, Math.sqrt(mult(p) / expectMult(st ? st.depth : p.depth))));   // Depth 10 compares against Depth 10
  const rules = st ? st.rules : p.next.rules;
  return Math.max(1, Math.round(P_RUN[d] * scoreF * multF * fineMult(rules, p)));
}

const toysOf = (run) => TOY_IDS.filter((id) => (run?.owned?.[id] || 0) > 0);

/** Finish + New run: adds the dust, one Depth (max 10), lights the ending, remembers the run. Twice on one run = nothing. */
export function renew(life, run) {
  const p = (life.prestige ||= createPrestige());
  if (run?.prestige?.renewed) return { dust: 0, depth: p.depth };
  const dust = dustFor(run, p);
  p.dust += dust;
  p.depth = Math.min(MAX_DEPTH, p.depth + 1);
  const end = run?.ended?.id;
  if (end && ENDING_BY_ID[end]) p.lit[end] = 1;
  p.last = { route: { ...(run?.picks || {}) }, toys: toysOf(run), ms: Math.round(run?.clock || 0) };
  p.next.keep = p.next.keep.filter((id) => p.last.toys.includes(id));
  p.next.rules = strs(p.next.rules, (id) => rulesOpen(p).includes(id), fineSlots(p));
  if (run) {
    run.prestige ||= { depth: 0 };
    run.prestige.renewed = true;
  }
  return { dust, depth: p.depth };
}

/** The read-only face ctx.prestige shows an unlock. */
export function prestigeView(getP, getStamp = () => null) {
  return Object.freeze({
    get depth() { return getP().depth; },
    get dust() { return getP().dust; },
    get next() { return Object.freeze({ rules: [...getP().next.rules], words: [...getP().next.words], keep: [...getP().next.keep] }); },
    /** What this run started with (rules, words, minutes, pay...). */
    get run() { const s = getStamp(); return s ? Object.freeze({ ...s }) : null; },
    level: (laneId) => level(getP(), laneId),
    has: (starId) => has(getP(), starId),
    lit: (endingId) => lit(getP(), endingId),
  });
}
