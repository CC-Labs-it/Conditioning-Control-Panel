/* hud/slotMath.js - the item slot's states and the moment each change plays (pure, no DOM; polish 6 JUICE).
 *
 * States: empty (nothing held), rolling (a card was grabbed, the icons spin), ready (the drawn item sits in
 * the slot), used (the item just left: its icon shrinks out and a ring flares, then the slot is empty).
 * slotMoment(from, to, fired) names the animation for a change, one per edge, each with an IN and an OUT
 * in styles.css: catch (a card grabbed: the slot punches in), land (the roll lands with a snap), use (the
 * player fired it), drop (the item went some other way, a plain fade), none.
 * Gentle (and OS reduced motion, in the CSS) swaps every scale for a fade: gentleMoment.
 */

export const SLOT_STATES = Object.freeze(['empty', 'rolling', 'ready', 'used']);

/** How long the used state shows before the slot reads empty (s); matches the CSS hud-slot-use keyframes. */
export const USED_S = 0.42;
/** How long the pips' release flash runs (s); matches the CSS hud-pip-out keyframes. */
export const PIP_OUT_S = 0.34;

/**
 * The state the slot shows. item: { id, rolling, rollT, rollS } | null; frame: the rollFrame result while
 * rolling ({ id, tick }, tick < 0 once the roll settles on the drawn id); usedUntil / nowS keep `used` up for
 * USED_S after the player fires.
 * @returns {{ state: string, id: string | null }}
 */
export function slotState(item, frame, { nowS = 0, usedUntil = 0, usedId = null } = {}) {
  if (item && (item.id || item.rolling)) {
    if (item.rolling && frame) return { state: frame.tick < 0 ? 'ready' : 'rolling', id: frame.id || null };
    if (item.rolling) return { state: 'rolling', id: item.id || null };
    return { state: 'ready', id: item.id };
  }
  if (nowS < usedUntil) return { state: 'used', id: usedId };
  return { state: 'empty', id: null };
}

/** The moment a state change plays: 'catch' | 'land' | 'use' | 'drop' | 'none'. */
export function slotMoment(from, to) {
  if (from === to) return 'none';
  if (to === 'rolling' && from !== 'rolling') return 'catch';
  if (to === 'ready' && from === 'rolling') return 'land';
  if (to === 'ready' && (from === 'empty' || from === 'used')) return 'catch';
  if (to === 'used') return 'use';
  if (to === 'empty' && (from === 'ready' || from === 'rolling')) return 'drop';
  return 'none';
}

/** The CSS class a moment plays; Gentle keeps the fades only (no punch, no snap, no flare). */
export function gentleMoment(moment, gentle) {
  if (moment === 'none') return null;
  if (!gentle) return 'm-' + moment;
  return moment === 'use' ? 'm-use-soft' : 'm-soft';
}

/**
 * Drift pips: which pip pops on a tier change (index), and whether the release flashes them out.
 * @returns {{ pop: number, out: boolean }} pop -1 = none
 */
export function pipMoment(prevTier, tier, { released = false } = {}) {
  const p = prevTier | 0, t = tier | 0;
  if (released) return { pop: -1, out: p > 0 };
  return { pop: t > p && t >= 1 && t <= 3 ? t - 1 : -1, out: false };
}
