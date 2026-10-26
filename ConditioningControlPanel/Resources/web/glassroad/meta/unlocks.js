/* meta/unlocks.js - placeholder, the META lane replaces this file.
 *
 * The three unlock ids of CONTRACT-LOGBOOK.md section 6, shaped { label, garage?, by }: `garage` is the
 * kart id the garage stops darkening, `by` the achievement id that grants it.
 */
export const UNLOCKS = Object.freeze({
  'kart:velvet-lock': Object.freeze({ label: 'Velvet Lock', garage: 'velvet-lock', by: 'long-hold' }),
  'kart:undertow': Object.freeze({ label: 'Undertow', garage: 'undertow', by: 'deep-end' }),
  'hat:umbrella': Object.freeze({ label: 'Umbrella hat', by: 'first-finish' }),
});

/** @returns {Set<string>} the known unlock ids the record holds */
export function unlockedSet(record) {
  const list = record && Array.isArray(record.unlocks) ? record.unlocks : [];
  return new Set(list.filter((id) => Object.prototype.hasOwnProperty.call(UNLOCKS, id)));
}
