/* sim/levels.js - the Levels XP sums. XP never stops: whole levels bank until they are collected, all at once. The save holds
 * the same two numbers it always did: `lvl` (levels collected, from 1) and `xp` (everything earned since), so an old save
 * sitting on a full bar simply reads as banked levels. */

export const XP_PER_SECOND = 1;
export const XP_PER_TAP = 1.2;

/** XP the level after `lvl` costs. */
export const xpNeed = (lvl) => Math.min(180, 35 * Math.pow(1.22, lvl - 1));

const clean = (lvl, xp) => [Number.isFinite(lvl) && lvl >= 1 ? Math.floor(lvl) : 1, Number.isFinite(xp) && xp > 0 ? xp : 0];

/** What the bar shows: whole levels waiting, and how far into the next one. */
export function bankOf(lvlIn, xpIn) {
  const [lvl, xp] = clean(lvlIn, xpIn);
  let banked = 0, left = xp;
  while (left >= xpNeed(lvl + banked)) { left -= xpNeed(lvl + banked); banked += 1; }
  const need = xpNeed(lvl + banked);
  return { lvl, banked, left, need, fill: left / need };
}

/** Seconds of income one level asks for when it is collected, before the side budget. A level banks about every 30 s at 4 taps/s,
 *  so this and the budget's weight for 'levels' set how much of the side money Levels takes. */
export const levelSeconds = () => 8;
/** One press asks for no more than this, however long the levels waited: a run of saving cannot buy the Drop. */
export const COLLECT_MAX_SECONDS = 240;

/** One press takes every banked level. `seconds` is what they pay together; the part-filled bar carries over. */
export function collect(lvlIn, xpIn) {
  const { lvl, banked, left } = bankOf(lvlIn, xpIn);
  let seconds = 0;
  for (let i = 0; i < banked; i++) seconds += levelSeconds(lvl + i);
  return { lvl: lvl + banked, xp: left, levels: banked, seconds: Math.min(COLLECT_MAX_SECONDS, seconds) };
}
