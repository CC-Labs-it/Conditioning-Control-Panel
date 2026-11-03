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

export function raceCanPlay({ paused, hidden, gentle, visible, dead, failed }) {
  return !paused && !hidden && !gentle && visible && !dead && !failed;
}
