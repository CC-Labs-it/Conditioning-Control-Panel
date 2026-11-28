/* ============================================================================
 * sim/prestige-endings.js - the Renewal Sky ending-star perks that are behaviour, not plain numbers (ENDING_STARS in
 * data/prestige.js; the plain ones live in ui/engine.js and sim/prestige.js runMods). Pure: every feature file reads
 * one of these with `ctx.prestige` (or the raw life.prestige in the engine and main.js), so the numbers sit in one place
 * and the tests see them. An ending lights for good when a run finishes on it.
 * ==========================================================================*/

import { ENDING_STARS } from '../data/prestige.js';

const ENDING_IDS = new Set(ENDING_STARS.map((e) => e.id));

/** Lit on a ctx.prestige view (lit is a function) or on a raw prestige object (lit is a map). */
export const litOf = (v, id) => (typeof v?.lit === 'function' ? !!v.lit(id) : !!v?.lit?.[id]);

/** Sunrise (go_end): the Golden Button stays on screen this much longer. Stacks with Long Gold (active_9). */
export const SUNRISE_STAY = 1.5;
export const sunriseStay = (v) => (litOf(v, 'go_end') ? SUNRISE_STAY : 1);

/** Lost Key (lo_end): the shut Toy Box counts down this much faster, so it opens more often. Stacks with Peek. */
export const LOST_KEY_RATE = 1.25;
export const lostKeyRate = (v) => (litOf(v, 'lo_end') ? LOST_KEY_RATE : 1);

/** Count Up (tr_end): every spiral row pays 10% more of its own per-second pay, after the soft cap (a `post` mod). */
export const COUNT_UP_SHARE = 0.1;
export const SPIRAL_ROWS = Object.freeze(['e_spiral', 'e_speed', 'a_spiral', 'overlay']);
export const countUpBonus = (v, fps) => (litOf(v, 'tr_end') ? Math.max(0, Number(fps) || 0) * COUNT_UP_SHARE : 0);
/** Called from each spiral row's mount: the mod goes with the row when the Control Panel switches it off. */
export function mountCountUp(ctx) {
  const add = countUpBonus(ctx.prestige, ctx.item?.fps);
  if (add > 0) ctx.mod({ post: true, fps: (v) => v + add });
}

/** Bimbodoll (bi_end): a fresh run owns Bubble Button from the first second. */
export const BUBBLE_ROW = 'bubble';
export const startsAsBubble = (v) => litOf(v, 'bi_end');

/** Unit Complete (dr_end): Scrolling Thoughts arrives already at Rank 2 (Journey rank 1). Journey runs only: the plain
 *  shop has no ranks. Returns true when it changed the run. */
export const TICKER_ROW = 'ticker';
export function tickerHeadStart(run, v) {
  const ranks = run?.store?.journey?.ranks;
  if (!ranks || !litOf(v, 'dr_end') || !(run.owned?.[TICKER_ROW] > 0) || (ranks[TICKER_ROW] || 0) >= 1) return false;
  ranks[TICKER_ROW] = 1;
  return true;
}

/** Redacted (ce_end): a tap on a censor bar pays what scratching a spot pays, at most once per bar per gap. */
export const REDACTED_GAP_MS = 1500;
export const redactedPay = (v, rates) => (litOf(v, 'ce_end') ? Math.max(1, (rates?.fps || 0) * 0.1 + (rates?.click || 0)) : 0);

/** Flatline (in_end): every spread interval and the coverage clock run at this share, so Infection spreads a third faster. */
export const FLATLINE_SPREAD = 0.75;
export const flatlineSpread = (v) => (litOf(v, 'in_end') ? FLATLINE_SPREAD : 1);

/** Endless (onemore): after any other ending, the credits card may carry the same run on, at full pay. */
export const canKeepPlaying = (life, run) => !!run?.ended && run.ended.id !== 'onemore' && !run.prestige?.renewed
  && litOf(life?.prestige, 'onemore');
/** Carries the run on: the ending it finished on lights now (the run did finish there), and the run is no longer over. */
export function keepPlaying(life, run) {
  if (!canKeepPlaying(life, run)) return false;
  const id = run.ended.id;
  if (ENDING_IDS.has(id)) life.prestige.lit[id] = 1;
  run.ended = null;
  return true;
}
