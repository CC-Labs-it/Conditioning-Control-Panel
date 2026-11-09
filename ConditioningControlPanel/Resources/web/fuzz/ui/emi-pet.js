/* ui/emi-pet.js - the EMI button's poke ladder (owner 2026-11-08: "pet her when we tap and she can react").
 * Pure: a tap and the clock go in, a mood comes out. Same ladder as the desktop EMI desk: the 3rd quick pet is glee,
 * the 4th annoyed, the 5th and on shocked. A mood climbs at most one rung per STEP_MS, so a fast clicker sees every
 * rung for a beat instead of a flicker, and each rung waits out its own calm before she settles back to idle. */

/* Rung 0 is idle. pose = body picture, face = the screen text, tag = the little paper label under her. */
export const PET_MOODS = Object.freeze([
  Object.freeze({ id: 'idle', pose: 'body-idle', face: '0_0', tag: 'Pet me', calm: 0 }),
  Object.freeze({ id: 'pet', pose: 'body-pet', face: '^_^', tag: 'Hehe', calm: 900 }),
  Object.freeze({ id: 'glee', pose: 'body-pet', face: '(≧◡≦)', tag: 'More', calm: 1100 }),
  Object.freeze({ id: 'annoyed', pose: 'body-smug', face: '¬_¬', tag: 'Okay, okay', calm: 1400 }),
  Object.freeze({ id: 'rage', pose: 'body-shock', face: '>_<', tag: 'HEY', calm: 1900 }),
]);
/** Taps closer together than this keep the streak going. */
export const PET_GAP_MS = 700;
/** The fastest the mood may climb one rung. */
export const STEP_MS = 420;

/** The rung a streak of quick pets asks for: 1-2 pet, 3 glee, 4 annoyed, 5+ rage. */
export const rungFor = (streak) => (streak <= 0 ? 0 : streak <= 2 ? 1 : Math.min(4, streak - 1));

export const petStart = () => ({ streak: 0, rung: 0, last: -Infinity, stepAt: -Infinity });

/** The highest rung while the player taps in EMI's sweet spot (sim/tap-rate.js emiBell): glee, never annoyed or rage
 * (owner 2026-11-09: "happy in the zone"); only a pace clearly past the zone climbs on to annoyed and HEY. */
export const SWEET_RUNG = 2;

/** One manual pet at time now. `sweet`: this press landed in the sweet spot. Answers the next state. */
export function petTap(s, now, sweet = false) {
  const streak = now - s.last <= PET_GAP_MS ? s.streak + 1 : 1;
  const want = sweet ? Math.min(SWEET_RUNG, rungFor(streak)) : rungFor(streak);
  let rung = s.rung, stepAt = s.stepAt;
  if (want < rung) { rung = want; stepAt = now; }
  else if (want > rung && (!rung || now - s.stepAt >= STEP_MS)) { rung += 1; stepAt = now; }
  return { streak, rung, last: now, stepAt };
}

/** Time passing with no tap: once the current rung's calm is out, she is idle again. */
export function petSettle(s, now) {
  if (s.rung && now - s.last >= PET_MOODS[s.rung].calm) return { ...s, streak: 0, rung: 0, stepAt: now };
  return s;
}
