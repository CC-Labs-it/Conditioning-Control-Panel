/* ============================================================================
 * core/melt.js - the rules of the CARD MELT (pure, no DOM, no clock).
 *
 * The card melt is a whimsy: a click on the card outside a control has a small
 * chance to melt the whole card away and move the run on. Owner rules
 * (2026-10-13):
 *
 *   1. It NEVER happens on a TIMED question. A timed beat is a test of speed,
 *      so a stray click must not take the question away.
 *   2. When it happens it SCORES NOTHING: no miss, no pass, no streak change,
 *      no reward, no entry in the run's record. The question simply was not
 *      asked.
 *
 * render/beats.js asks meltAllowed() before it rolls the chance and resolves a
 * melt with an AnswerEvent that carries `melted: true`; core/engine.js asks
 * isMeltSkip() and leaves every tally alone.
 * ========================================================================== */

/** Mechanics that never melt (their card is not a question card). Wire strings
 *  from contracts.js Mechanic, repeated here so this file imports nothing. */
const NEVER_MELTS = Object.freeze(['interlude', 'bubblepop']);

/** True when the beat runs against a clock. */
export function isTimedBeat(beat) {
  return !!(beat && typeof beat.timeoutMs === 'number' && beat.timeoutMs > 0);
}

/**
 * May a click on this beat's card melt it?
 * @param {object} beat   BeatSpec (mechanic, timeoutMs)
 * @param {object=} live  per-beat render state: committed, meltUsed, gateFrozen, corruptLive
 */
export function meltAllowed(beat, live) {
  if (!beat) return false;
  if (isTimedBeat(beat)) return false;                       // rule 1
  if (NEVER_MELTS.indexOf(beat.mechanic) >= 0) return false;
  const s = live || {};
  if (s.committed || s.meltUsed || s.gateFrozen || s.corruptLive) return false;
  return true;
}

/** True when an AnswerEvent is a melt: the engine must score nothing for it. */
export function isMeltSkip(ev) {
  return !!(ev && ev.melted === true);
}
