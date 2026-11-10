/* sim/stickers.js - the Sticker Book rules. Pure: no DOM, no clock, no rng.
 *
 * Two records, on purpose:
 *  - this run: the Achievements row's own store ('earned'), the ten-cell meter. A 'run' sticker can be earned
 *    again next run and fills the meter again.
 *  - the book: life.stickers (sticker id -> the run it was first earned in) and life.tally (lifetime counters).
 *    It never empties; Forget everything is the only way out.
 * The meter keeps the old rule (+10% per ten stickers this run) and its old ceiling: the 33 achievements could
 * never fill more than three meters, so the book's extra stickers never pay past +30%. */

import { LIFE_TALLIES, STICKER_BY_ID, STICKERS } from '../data/content/stickers.js';

export const METER_CELLS = 10;
export const METER_STEP = 0.1;
export const METER_MAX_STEPS = 3;

/** Earnings multiplier for this run's sticker count. */
export function meterMult(earnedThisRun) {
  return 1 + Math.min(METER_MAX_STEPS, Math.floor(earnedThisRun / METER_CELLS)) * METER_STEP;
}

/** The run number a sticker earned right now belongs to: runs counts finished runs. */
export const currentRun = (life) => (life?.runs || 0) + 1;

const split = (when) => { const i = when.lastIndexOf(':'); return [when.slice(0, i), when.slice(i + 1)]; };

/** Is a sticker's condition met? st = {taps, buys, total, clock, picks, owned, tally}; life = the lifetime save. */
export function met(sticker, st, life) {
  const [what, raw] = split(sticker.when);
  const need = Number(raw);
  const lt = life?.tally || {};
  if (what.startsWith('life.')) return (lt[what.slice(5)] || 0) >= need;
  switch (what) {
    case 'taps': return (st.taps || 0) >= need;
    case 'buys': return (st.buys || 0) >= need;
    case 'total': return (st.total || 0) >= need;
    case 'minutes': return (st.clock || 0) >= need * 60000;
    case 'forks': return Object.keys(st.picks || {}).length >= need;
    case 'theme': return st.picks?.f5 === raw;
    case 'owns': return !!st.owned?.[raw];
    case 'ending': return (life?.endings?.[raw] || 0) > 0;
    case 'endings': return Object.keys(life?.endings || {}).length >= need;
    case 'themes': return STICKERS.filter((s) => s.id.startsWith('t_') && s.when.startsWith('theme:') && life?.stickers?.[s.id]).length >= need;
    case 'depth': return (life?.prestige?.depth || 0) >= need;
    case 'core': return !!life?.prestige?.core;
    case 'llamas': return (life?.llamas || 0) >= need;
    default: return ((st.tally || {})[what] || 0) >= need;
  }
}

/** Run stickers whose condition is met and that this run has not earned yet, in book order. */
export function dueThisRun(st, life, earned) {
  return STICKERS.filter((s) => s.scope === 'run' && !earned.has(s.id) && met(s, st, life));
}

/** Writes a sticker into the book. Answers true when the book did not have it yet. */
export function earnSticker(life, id, run = currentRun(life)) {
  if (!life || !STICKER_BY_ID[id]) return false;
  life.stickers ||= {};
  if (life.stickers[id]) return false;
  life.stickers[id] = Math.max(1, Math.floor(run));
  return true;
}

/** Adds to a lifetime counter the book reads (life.tally). */
export function noteLife(life, key, n = 1) {
  if (!life || !LIFE_TALLIES.includes(key)) return 0;
  life.tally ||= {};
  life.tally[key] = (life.tally[key] || 0) + n;
  return life.tally[key];
}

/** Writes every lifetime sticker the save already earns (endings, themes count, Renewal, llamas, life tallies).
 *  Answers the ids that are new in the book. run defaults to the current run. */
export function syncLife(life, run = currentRun(life)) {
  const got = [];
  /* Two passes: Full Box reads the theme stickers, Every Ending reads the endings. */
  for (let pass = 0; pass < 2; pass++) {
    for (const s of STICKERS) if (s.scope === 'life' && !life?.stickers?.[s.id] && met(s, {}, life) && earnSticker(life, s.id, run)) got.push(s.id);
  }
  return got;
}

/** Book view for one page: [{sticker, run}] with run = 0 for not earned. */
export function pageView(life, pageId) {
  return STICKERS.filter((s) => s.page === pageId).map((s) => ({ sticker: s, run: life?.stickers?.[s.id] || 0 }));
}

/** Save hygiene: keeps only known sticker ids and known counters. */
export function parseStickers(src) {
  const out = {};
  if (src && typeof src === 'object') for (const [id, n] of Object.entries(src)) if (STICKER_BY_ID[id] && Number.isFinite(n) && n >= 1) out[id] = Math.floor(Math.min(n, 1e6));
  return out;
}
export function parseLifeTally(src) {
  const out = {};
  if (src && typeof src === 'object') for (const k of LIFE_TALLIES) if (Number.isFinite(src[k]) && src[k] > 0) out[k] = Math.floor(Math.min(src[k], 1e9));
  return out;
}
