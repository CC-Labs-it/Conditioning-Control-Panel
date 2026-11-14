/* kart-bars.js - what a kart body does, for the shelf and the confirm card (owner 2026-10-31).
 * A copy of Glassroad's `statBars` (glassroad/sim/kartStats.js over feel.json `karts`): a page never fetches from another
 * web tree. tests/kart-bars.test.mjs fails when this drifts and prints the rows to paste. Words stay in the web tree, as
 * the garage's do: no loc keys. */

export const PIPS = 7;
export const STAT_WORDS = [['speed', 'Speed'], ['accel', 'Accel'], ['handling', 'Handling'], ['stability', 'Stability']];
export const TYPE_WORDS = Object.freeze({ allround: 'All-rounder', easy: 'Easy', drifter: 'Drifter', heavy: 'Heavy', fast: 'Fast' });

export const KART_BARS = Object.freeze({
  lovebomb: { speed: 4, accel: 4, handling: 4, stability: 4, type: 'allround' },
  pendulum: { speed: 5, accel: 3, handling: 4, stability: 4, type: 'allround' },
  petal: { speed: 1, accel: 7, handling: 7, stability: 6, type: 'easy' },
  trinket: { speed: 2, accel: 6, handling: 7, stability: 5, type: 'easy' },
  feedback: { speed: 3, accel: 4, handling: 6, stability: 3, type: 'drifter' },
  reverie: { speed: 3, accel: 5, handling: 5, stability: 3, type: 'drifter' },
  checkmate: { speed: 5, accel: 1, handling: 3, stability: 7, type: 'heavy' },
  'house-edge': { speed: 5, accel: 2, handling: 4, stability: 6, type: 'heavy' },
  undertow: { speed: 7, accel: 1, handling: 1, stability: 1, type: 'fast' },
  'velvet-lock': { speed: 6, accel: 2, handling: 2, stability: 2, type: 'fast' },
});

/** A counter row id -> that body's bars and type word; null for anything that is not a kart body. */
export function kartBarsOf(itemId) {
  const m = /^kart\.body\.([a-z0-9-]+)$/.exec(String(itemId));
  const row = m && Object.prototype.hasOwnProperty.call(KART_BARS, m[1]) ? KART_BARS[m[1]] : null;
  return row ? { ...row, word: TYPE_WORDS[row.type] || '' } : null;
}

/** The same bars in words, for a screen reader. */
export const barsLine = (bars) => `${bars.word}. ${STAT_WORDS.map(([key, word]) => `${word} ${bars[key]} of ${PIPS}`).join(', ')}`;
