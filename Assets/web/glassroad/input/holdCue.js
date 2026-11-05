/* input/holdCue.js - is a stretch ahead that the hat flies only while jump is held? Pure, read by boot for the
 * touch Jump ring. The phrase rides catch the aerial gaps by themselves (sim/airWords.js), solo and online, so only
 * the course's authored launch counts. The sim is never asked: the cue only says when to hold, the hands stay the
 * player's.
 */
const LEAD_M = 60;

/** @param {object|null} launch the course's authored launch @param {number} s */
export function holdCueAt(launch, s) {
  return !!launch && s >= launch.rampS0 - LEAD_M && s < launch.gapS1;
}
