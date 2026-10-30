/* input/holdCue.js - is a stretch ahead that the hat flies only while jump is held? Pure, read by boot for the
 * touch Jump ring. Solo the phrase rides catch the aerial gaps by themselves (sim/airWords.js), so only the authored
 * launch counts there; online nothing catches, and letting go over a gap is a fall. The sim is never asked: the cue
 * only says when to hold, the hands stay the player's.
 */
const LEAD_M = 60;
const inside = (s, z, endS) => s >= z.rampS0 - LEAD_M && s < endS;

/** @param {object} track @param {object|null} launch the course's authored launch @param {number} s @param {boolean} online */
export function holdCueAt(track, launch, s, online) {
  if (launch && inside(s, launch, launch.gapS1)) return true;
  if (!online) return false;
  const gaps = track.aerials || [];
  for (let i = 0; i < gaps.length; i++) if (inside(s, gaps[i], gaps[i].gapS1)) return true;
  return false;
}
