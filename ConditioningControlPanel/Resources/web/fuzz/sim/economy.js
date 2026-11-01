/* ============================================================================
 * sim/economy.js - the whole economy as pure functions over one state object.
 * No DOM, no timers, no audio: ui/ and fx/ react to what these return.
 * Tested by tests/economy.test.mjs.
 *
 * Time is always passed in (ms); randomness is always passed in (rng in [0,1)).
 * ==========================================================================*/

import { ITEMS, BY_ID, PINNED, ENDING_ID, DEEPER_STEP } from '../data/items.js';

export const BASE_CLICK = 1;
export const CRIT_CHANCE = 0.05;
export const CRIT_MUL = 8;
export const CORNER_MUL = 10;
export const POP2_SHARE = 0.01;          // Bubble Pop II: each click also pays this share of FPS
export const SHOP_WINDOW = 5;
export const MAX_PACE = 50;

export function createState({ now = 0, pace = 1 } = {}) {
  return {
    v: 1,
    fuzz: 0,                 // the bank
    total: 0,                // lifetime earned (unlocks, achievements, the final number)
    clicks: 0,
    crits: 0,
    bounces: 0,
    corners: 0,
    owned: {},               // id -> count
    bought: [],              // ids in purchase order (pluck ladder, firsts)
    achievements: [],        // ids, in order
    sources: { clicks: 0, layers: 0, bounces: 0, boxes: 0, golden: 0, levels: 0 },
    effects: { frenzyUntil: 0, frenzyMul: 1, clickFrenzyUntil: 0, clickFrenzyMul: 1, focusUntil: 0, deeperUntil: 0, blankUntil: 0 },
    pace: Math.max(1, Math.min(MAX_PACE, pace)),
    startedAt: now,
    ended: false,
    endedAt: 0,
  };
}

export const owned = (s, id) => s.owned[id] || 0;
export const has = (s, id) => owned(s, id) > 0;

export function priceOf(item, count = 0) {
  const grow = item.grow || 1;
  return Math.round(item.cost * Math.pow(grow, count));
}

export function price(s, id) { return priceOf(BY_ID[id], owned(s, id)); }

/** Visible in the shop: prerequisites owned, achievements reached, not sold out. */
export function isUnlocked(s, item) {
  if (item.req && !item.req.every((r) => has(s, r))) return false;
  if (item.achNeed && s.achievements.length < item.achNeed) return false;
  const n = owned(s, item.id);
  if (item.grow) return n < (item.max ?? Infinity);
  return n === 0;
}

export function canBuy(s, id) {
  const item = BY_ID[id];
  return !!item && !s.ended && isUnlocked(s, item) && s.fuzz >= price(s, id);
}

/** The rolling window: pinned first, then the cheapest unlocked by current price. */
export function shopWindow(s, size = SHOP_WINDOW) {
  const open = ITEMS.filter((it) => isUnlocked(s, it));
  const pinned = open.filter((it) => PINNED.includes(it.id));
  const rest = open.filter((it) => !PINNED.includes(it.id)).sort((a, b) => price(s, a.id) - price(s, b.id) || ITEMS.indexOf(a) - ITEMS.indexOf(b));
  return [...pinned, ...rest].slice(0, size);
}

export function deeperMul(s) {
  const deepers = ['deep1', 'deep2', 'deep3'].filter((id) => has(s, id)).length;
  return 1 + DEEPER_STEP * deepers;
}

/** Fuzz per second from the layers, before pace. */
export function baseFps(s) {
  let sum = 0;
  for (const it of ITEMS) if (it.fps) sum += it.fps * owned(s, it.id);
  return sum * deeperMul(s);
}

export function frenzyMul(s, now) { return now < s.effects.frenzyUntil ? s.effects.frenzyMul : 1; }

/** What the counter shows and what tick() pays, per second. */
export function fps(s, now) { return baseFps(s) * frenzyMul(s, now) * s.pace; }

export function clickPower(s, now) {
  let p = BASE_CLICK;
  for (const it of ITEMS) if (it.click && has(s, it.id)) p += it.click;
  if (has(s, 'pop2')) p += POP2_SHARE * baseFps(s) * frenzyMul(s, now);
  return p * s.pace;
}

export function bounceValue(s, corner = false) {
  let v = 1;
  for (const it of ITEMS) if (it.bounce && has(s, it.id)) v += it.bounce;
  if (corner && has(s, 'corner')) v *= CORNER_MUL;
  return v * s.pace;
}

function pay(s, amount, source) {
  s.fuzz += amount;
  s.total += amount;
  s.sources[source] = (s.sources[source] || 0) + amount;
  return amount;
}

/** Advance the layers by ms of real time. Returns what was paid. */
export function tick(s, ms, now) {
  if (s.ended || ms <= 0) return 0;
  return pay(s, fps(s, now) * (ms / 1000), 'layers');
}

/** One bubble pop. rng decides the crit (only with Trigger Words). */
export function click(s, now, rng = Math.random) {
  if (s.ended) return { gain: 0, crit: false };
  s.clicks += 1;
  let gain = clickPower(s, now);
  let crit = false;
  if (has(s, 'trig') && rng() < CRIT_CHANCE) { crit = true; gain *= CRIT_MUL; s.crits += 1; }
  if (now < s.effects.clickFrenzyUntil || now < s.effects.focusUntil) gain *= s.effects.clickFrenzyMul || 1;
  pay(s, gain, 'clicks');
  return { gain, crit };
}

/** A drifting bubble hit an edge (or a corner). */
export function bounce(s, corner = false) {
  if (s.ended) return 0;
  s.bounces += 1;
  if (corner && has(s, 'corner')) s.corners += 1;
  return pay(s, bounceValue(s, corner), 'bounces');
}

/** A lump from a side system (box, golden bubble, level). */
export function lump(s, amount, source) {
  if (s.ended || amount <= 0) return 0;
  return pay(s, amount, source);
}

/** Buy one. Returns {ok, reason, item, count}. The ending item flips s.ended. */
export function buy(s, id, now) {
  const item = BY_ID[id];
  if (!item) return { ok: false, reason: 'unknown' };
  if (s.ended) return { ok: false, reason: 'ended' };
  if (!isUnlocked(s, item)) return { ok: false, reason: 'locked' };
  const cost = price(s, id);
  if (s.fuzz < cost) return { ok: false, reason: 'short', short: cost - s.fuzz };
  s.fuzz -= cost;
  s.owned[id] = owned(s, id) + 1;
  s.bought.push(id);
  if (id === ENDING_ID) { s.ended = true; s.endedAt = now; }
  return { ok: true, item, count: s.owned[id], cost };
}

/** Minutes played, for the final card. */
export function minutesPlayed(s, now) { return Math.max(0, Math.round(((s.ended ? s.endedAt : now) - s.startedAt) / 60000)); }
