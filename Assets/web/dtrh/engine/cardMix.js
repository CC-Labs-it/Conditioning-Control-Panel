/* ============================================================================
 * cardMix.js - pictures first, clips sparse later (owner, 2026-10-12).
 *
 * The tube's drifting cards used to draw a picture or a clip at random, so a
 * library with more clips than pictures opened a run on a wall of video. The
 * rule here: the first IMAGES_FIRST cards of a run are pictures, and after
 * that a clip may come at most once every VIDEO_EVERY cards. A library with
 * no pictures at all is not held back (the tube would be empty otherwise).
 * Pure, node tested; engine/spawner.js keeps the two counters.
 * ==========================================================================*/

export const IMAGES_FIRST = 10;   // cards at the start of a run that are always pictures
export const VIDEO_EVERY = 6;     // after that, at least this many cards between two clips

/** May the next card be a clip? cards = cards spawned this run, sinceVideo = cards since the last clip. */
export function videoAllowed(cards, sinceVideo, hasImages) {
  if (!hasImages) return true;
  if (cards < IMAGES_FIRST) return false;
  return sinceVideo >= VIDEO_EVERY;
}

/** A fresh counter pair for a run. */
export function newCardMix() { return { cards: 0, sinceVideo: VIDEO_EVERY }; }

/** Count one spawned card. Mutates and returns the pair. */
export function noteCard(mix, kind) {
  mix.cards += 1;
  mix.sinceVideo = kind === 'video' ? 0 : mix.sinceVideo + 1;
  return mix;
}
