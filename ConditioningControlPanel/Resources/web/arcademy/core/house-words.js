/* ============================================================================
 * core/house-words.js - the school's own word floor (core/vocab.js deals it).
 *
 * The house vocabulary: 24 words, deliberately niche-agnostic and mod-agnostic.
 * Every one of them is something the school could say to anybody - no name, no
 * pronoun, no persona, nothing a creator mod would have to disown. Uppercase
 * because that is how a sub_flash card renders every other word it is given.
 *
 * The spelling is LOCKED: `slug()` in core/vocab.js turns each entry into the
 * filename its whisper clip ships under (assets/sublim/<slug>.mp3), so renaming
 * a word here orphans a clip on disk.
 *
 * The store tree replaces this file with house-words.store.js.
 * ==========================================================================*/

export const HOUSE_WORDS = Object.freeze([
  'FOCUS', 'RELAX', 'BREATHE', 'LET GO', 'SINK', 'DEEPER', 'DRIFT', 'BLANK',
  'EMPTY', 'LISTEN', 'OBEY', 'GOOD', 'AGAIN', 'STAY', 'SMILE', 'SOFTER',
  'MELT', 'CALM', 'DROP', 'TRUST', 'QUIET', 'OPEN', 'GIVE IN', 'FLOAT',
]);

export default HOUSE_WORDS;
