/* sim/prestige-words.js - Trigger Words (Renewal Sky lane `trig`), the pure numbers. ui/unlocks/prestige/trig.js draws.
 *
 * The player's word floats across the page every 40 to 70 s of game time; a tap pays a burst of income through ctx.pay,
 * so it sits inside the side money budget (CONFIG.SIDE_SHARE) with every other prize. Sizing (owner brief 2026-11-09:
 * "meaningful but not dominant"): a bare catch asks CATCH_S seconds of income, about 9% of a run's income when every
 * word is caught; Often + Sharper + Sharper Still + a fully Conditioned word ask about 40%, which the side budget then
 * shares fairly with the other prizes. Extra words do not add appearances: each appearance shows one of them, so a
 * second word spreads the catches (and the Conditioned growth) instead of doubling the pay.
 *
 * Conditioned (trig_4): every catch of a word adds GROW (Deep Groove trig_9: GROW_DEEP) to that word's pay, linear,
 * up to GROW_CAP, kept in the lane's life store so it carries into later runs. Instant Response (trig_10): a catch
 * within INSTANT_MS of the word being readable pays x3. */

import { WORD_BANK } from '../data/prestige.js';

export const GAP_MIN_S = 40;
export const GAP_MAX_S = 70;
/** trig_2 Often: shows up 25% more often. */
export const OFTEN = 1.25;
/** Seconds of income a bare catch asks for. */
export const CATCH_S = 5;
/** trig_3 Sharper +25%, trig_6 Sharper Still +50% (added, so both = +75%). */
export const SHARPER = 0.25;
export const SHARPER_STILL = 0.5;
export const GROW = 0.01;
export const GROW_DEEP = 0.02;
/** Conditioned stops adding past +100% (a word caught 100 times, 50 with Deep Groove). */
export const GROW_CAP = 1;
export const INSTANT = 3;
export const INSTANT_MS = 500;
/** How long a word stays up, ms (Gentle: longer, it fades slower). */
export const LIFE_MS = 7000;
export const LIFE_GENTLE_MS = 9000;
export const FADE_IN_MS = 160;
export const FADE_IN_GENTLE_MS = 450;

const clean = (w) => (typeof w === 'string' ? w.trim().toLowerCase() : '');

/** How many words the player may carry: trig_1 one, trig_5 two, trig_8 three. `has(starId)`. */
export function wordSlots(has) {
  if (!has('trig_1')) return 0;
  return 1 + (has('trig_5') ? 1 : 0) + (has('trig_8') ? 1 : 0);
}

/** The run's words: the picks from next.words that are in the bank, no repeats, at most `slots`. None picked = the
 *  first bank word. */
export function runWords(picks, slots) {
  if (slots <= 0) return [];
  const out = [];
  for (const w of Array.isArray(picks) ? picks : []) {
    const c = clean(w);
    if (WORD_BANK.includes(c) && !out.includes(c)) out.push(c);
    if (out.length >= slots) break;
  }
  return out.length ? out : [WORD_BANK[0]];
}

/** Game ms until the next word. `rng` in [0, 1). */
export function nextGapMs(rng, { often = false } = {}) {
  const r = Number.isFinite(rng) ? Math.min(1, Math.max(0, rng)) : 0.5;
  return Math.round((GAP_MIN_S + r * (GAP_MAX_S - GAP_MIN_S)) * 1000 / (often ? OFTEN : 1));
}

/** Pay factor from Conditioned for a word caught `catches` times before. */
export function conditionMult(catches, { conditioned = false, deep = false } = {}) {
  if (!conditioned) return 1;
  const n = Number.isFinite(catches) ? Math.max(0, catches) : 0;
  return 1 + Math.min(GROW_CAP, n * (deep ? GROW_DEEP : GROW));
}

/** True when the press landed inside the Instant Response window (counted from the moment the word is readable). */
export function isInstant(appearedAt, now, gentle = false) {
  const t = now - appearedAt;
  return Number.isFinite(t) && t >= 0 && t <= INSTANT_MS + (gentle ? FADE_IN_GENTLE_MS : FADE_IN_MS);
}

/** Seconds of income one catch asks for. `has(starId)`; `catches` = how often this word was caught before. */
export function catchSeconds(has, catches = 0, instant = false) {
  const sharp = 1 + (has('trig_3') ? SHARPER : 0) + (has('trig_6') ? SHARPER_STILL : 0);
  const cond = conditionMult(catches, { conditioned: has('trig_4'), deep: has('trig_9') });
  return CATCH_S * sharp * cond * (instant && has('trig_10') ? INSTANT : 1);
}

/** Share of a run's income the lane asks for if every word is caught (no Instant): catch seconds over the mean gap. */
export function askShare(has, catches = 0) {
  const gap = (GAP_MIN_S + GAP_MAX_S) / 2 / (has('trig_2') ? OFTEN : 1);
  return catchSeconds(has, catches, false) / gap;
}

/** The lane's life store, repaired: `{ catches: { word: n }, total }`. Never throws. */
export function parseWordStore(src) {
  const catches = {};
  const raw = src && typeof src === 'object' && src.catches && typeof src.catches === 'object' ? src.catches : {};
  for (const [k, v] of Object.entries(raw)) {
    const c = clean(k);
    if (WORD_BANK.includes(c) && Number.isFinite(v) && v > 0) catches[c] = Math.min(1e6, Math.floor(v));
  }
  const total = Number.isFinite(src?.total) && src.total > 0 ? Math.min(1e7, Math.floor(src.total)) : 0;
  return { catches, total };
}

/** Counts one catch of `word` into a parsed store (mutates it) and answers the count BEFORE this catch. */
export function recordCatch(store, word) {
  const c = clean(word);
  const before = store.catches[c] || 0;
  store.catches[c] = before + 1;
  store.total = (store.total || 0) + 1;
  return before;
}
