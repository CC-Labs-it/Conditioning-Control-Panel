/* Bonus timing is separate from the recorded race. Only active game time counts. */
export const RACE_BONUS_MS = 35000;
export function restoreRace(saved) {
  return { leftMs: Number.isFinite(saved?.leftMs) ? Math.max(0, Math.min(RACE_BONUS_MS, saved.leftMs)) : RACE_BONUS_MS };
}
export function advanceRace(state, dt) {
  if (Number.isFinite(dt) && dt > 0) state.leftMs = Math.max(0, state.leftMs - dt);
  return state.leftMs === 0;
}
export function takeRaceBonus(state) {
  if (state.leftMs !== 0) return false;
  state.leftMs = RACE_BONUS_MS;
  return true;
}

/* Journey upgrades (owner 2026-11-10): rank 1 Pick the Course, rank 2 Nitro (the reward timer fills 30% faster). */
export const NITRO = 1.3;
export const raceTimerRate = (rank) => rank >= 2 ? NITRO : 1;
/** Real seconds left on the reward timer at this rate, for the button text. */
export const raceSecondsLeft = (state, rate = 1) => Math.ceil(state.leftMs / Math.max(1, rate) / 1000);

/**
 * The course that plays next. `picked` (Pick the Course) is a course index the player chose; it keeps playing until
 * they choose another or go back to the rotation (null). A picked course that cannot play falls back to the rotation.
 * `blocked(i)` says a course's clip is resting after a failure. Answers -1 when every course is blocked.
 */
export function nextCourse(current, picked, count, blocked = () => false) {
  if (Number.isInteger(picked) && picked >= 0 && picked < count && !blocked(picked)) return picked;
  for (let n = 1; n <= count; n++) {
    const i = (current + n) % count;
    if (!blocked(i)) return i;
  }
  return -1;
}

export function raceCanPlay({ paused, hidden, gentle, visible, dead, failed }) {
  return !paused && !hidden && !gentle && visible && !dead && !failed;
}
