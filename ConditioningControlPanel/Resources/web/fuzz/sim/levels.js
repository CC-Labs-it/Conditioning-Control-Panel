/* sim/levels.js - the Levels XP sums. XP never stops: whole levels bank until they are collected, all at once. The save holds
 * the same two numbers it always did: `lvl` (levels collected, from 1) and `xp` (everything earned since), so an old save
 * sitting on a full bar simply reads as banked levels. */

export const XP_PER_SECOND = 1;
export const XP_PER_TAP = 1.2;

/** XP the level after `lvl` costs. */
const NEED_CAP = 180;
export const xpNeed = (lvl) => Math.min(NEED_CAP, 35 * Math.pow(1.22, lvl - 1));

const clean = (lvl, xp) => [Number.isFinite(lvl) && lvl >= 1 ? Math.floor(lvl) : 1, Number.isFinite(xp) && xp > 0 ? xp : 0];

/** What the bar shows: whole levels waiting, and how far into the next one. */
export function bankOf(lvlIn, xpIn) {
  const [lvl, xp] = clean(lvlIn, xpIn);
  let banked = 0, left = xp;
  while (xpNeed(lvl + banked) < NEED_CAP && left >= xpNeed(lvl + banked)) { left -= xpNeed(lvl + banked); banked += 1; }
  /* Past the cap every level costs the same, so the rest is a division. A huge bank or a doctored save never spins here. */
  if (left >= NEED_CAP) { banked += Math.floor(left / NEED_CAP); left %= NEED_CAP; }
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
  return { lvl: lvl + banked, xp: left, levels: banked, seconds: Math.min(COLLECT_MAX_SECONDS, banked * levelSeconds()) };
}
