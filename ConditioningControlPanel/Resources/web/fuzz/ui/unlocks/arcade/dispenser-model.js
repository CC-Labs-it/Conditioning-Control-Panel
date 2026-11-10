/* ui/unlocks/arcade/dispenser-model.js - the Prize Dispenser's Journey upgrades (owner 2026-11-10). PURE: no DOM, no clock.
 * Rank 1 Wide Catch: 4 seconds to catch instead of 2.5. Rank 2 Double Drop: each crank drops two capsules. The second
 * capsule rolls its own prize with the same odds (sim/dispenser-rewards.js) and needs its own catch; it lands a moment
 * after the first so the two catches never ask for one tap. The first capsule stays the machine in sim/arcade.js. */

import { PRIZES, rollPrize } from '../../../sim/dispenser-rewards.js';
import { CATCH_MS, DROP_MS } from '../../../sim/arcade.js';

export const WIDE_CATCH_MS = 4000;
export const SECOND_DELAY_MS = 900;      // the second capsule lands this long after the first
const DISSOLVE_MS = 650;

export const catchWindowFor = (rank) => rank >= 1 ? WIDE_CATCH_MS : CATCH_MS;
export const dropsFor = (rank) => rank >= 2 ? 2 : 1;

const clamp = (n, max) => Number.isFinite(n) ? Math.max(0, Math.min(max, n)) : 0;

/** The second capsule, saved. 'idle' = none in flight. */
export function secondState(saved) {
  const s = saved && typeof saved === 'object' ? saved : {};
  return {
    prize: Object.hasOwn(PRIZES, s.prize) ? s.prize : 'red',
    phase: ['idle', 'drop', 'catch', 'dissolve'].includes(s.phase) ? s.phase : 'idle',
    left: clamp(s.left, DROP_MS + SECOND_DELAY_MS),
  };
}

/** Starts the second capsule for this crank. One roll of the game's RNG, after the first capsule's roll. */
export function dropSecond(c, rng = Math.random) {
  if (c.phase !== 'idle') return false;
  c.prize = rollPrize(rng);
  c.phase = 'drop';
  c.left = DROP_MS + SECOND_DELAY_MS;
  return true;
}

/** Active game time only. Carries the remainder across phases like the machine, so a slow frame never widens a catch. */
export function stepSecond(c, ms, catchMs = CATCH_MS) {
  let dt = Math.max(0, Number.isFinite(ms) ? ms : 0);
  let missed = false;
  while (dt > 0 && c.phase !== 'idle') {
    const used = Math.min(dt, c.left); c.left -= used; dt -= used;
    if (c.left > 0) break;
    if (c.phase === 'drop') { c.phase = 'catch'; c.left = catchMs; }
    else if (c.phase === 'catch') { c.phase = 'dissolve'; c.left = DISSOLVE_MS; missed = true; }
    else { c.phase = 'idle'; c.left = 0; }
  }
  return missed;
}

/** One catch per capsule. */
export function catchSecond(c) {
  if (c.phase !== 'catch' || c.left <= 0) return false;
  c.phase = 'idle'; c.left = 0;
  return true;
}
