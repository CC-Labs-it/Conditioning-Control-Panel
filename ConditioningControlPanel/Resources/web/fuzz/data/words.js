/* ============================================================================
 * data/words.js - the spoken-word banks. Recorded clips only, never synthesis
 * (AGENTS.md hard rule 5). A word with no clip stays silent.
 *
 * CCP Default and every web/phone build use the Circe clips copied from
 * Resources/Audio/backroom/words (whispered, mono, -25 LUFS). The Bambi mod
 * may hand the page another bank through init.caps.words (CONTRACT.md 3.1);
 * this file never names those clips.
 * ==========================================================================*/

const W = (slug) => `assets/audio/words/${slug}.mp3`;

/** Every Circe clip the tree ships, by slug. */
export const CIRCE = Object.freeze([
  'blank', 'blank-and-happy', 'breathe', 'deeper', 'denied', 'don-t-touch', 'drift', 'drop', 'drop-deeper',
  'edge', 'empty', 'empty-and-ready', 'focus', 'freeze', 'let-go', 'listen', 'mindless', 'obedient', 'obey',
  'quiet-mind', 'relax', 'release', 'reset', 'sink', 'sink-deeper', 'so-calm', 'softer', 'submit', 'trance',
]);

/** Mantra: one quiet word every few seconds while the item is owned. */
export const MANTRA = Object.freeze(['relax', 'sink', 'deeper', 'let-go', 'blank', 'so-calm', 'drift', 'breathe', 'softer', 'quiet-mind', 'empty', 'trance', 'listen', 'release']);

/** The Voice: what she says on each event. */
export const VOICE = Object.freeze({
  crit: ['deeper', 'drop', 'sink-deeper'],
  buy: ['obey', 'obedient', 'submit'],
  corner: ['drop-deeper', 'focus'],
  denied: ['denied', 'don-t-touch'],
  golden: ['listen', 'edge'],
});

/** The ending: one word every 10 s, in this order, then silence. */
export const ENDING = Object.freeze(['relax', 'sink', 'deeper', 'let-go', 'blank', 'so-calm']);

/** Trigger Words on the bubble: shown as text only (250 ms fade), never spoken. */
export const TRIGGER_TEXT = Object.freeze(['sink', 'drift', 'deeper', 'blank', 'soft', 'pink', 'obey', 'empty', 'float', 'down', 'yes', 'good']);

export function clipUrl(slug) { return CIRCE.includes(slug) ? W(slug) : null; }
