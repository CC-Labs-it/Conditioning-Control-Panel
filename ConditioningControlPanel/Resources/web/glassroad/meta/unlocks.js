/* meta/unlocks.js - cosmetic unlocks (CONTRACT-LOGBOOK.md section 6). Never a stat, a feature or Sparkles.
 * `garage` is the garage key the RECORD lane unlocks; `by` is the achievement that grants it.
 */
export const UNLOCKS = Object.freeze({
  'kart:velvet-lock': Object.freeze({ label: 'Velvet Lock', garage: 'velvet-lock', by: 'last-to-first' }),
  'kart:undertow': Object.freeze({ label: 'Undertow', garage: 'undertow', by: 'gold-leaf' }),
  'hat:umbrella': Object.freeze({ label: 'Umbrella', garage: 'umbrella', by: 'five-rooms' }),
});

/** @returns {Set<string>} the unlock ids the record holds (only known ids) */
export function unlockedSet(record) {
  const out = new Set();
  for (const id of (record && Array.isArray(record.unlocks) ? record.unlocks : [])) if (UNLOCKS[id]) out.add(id);
  return out;
}
