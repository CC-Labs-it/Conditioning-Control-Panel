/* meta/achievements.js - placeholder, the META lane replaces this file.
 *
 * Three rows in the contract shape { id, name, line, kind, hidden, reward } so the RECORD lane can
 * draw the achievement wall and the garage locks before the real table lands.
 */
export const ACHIEVEMENTS = Object.freeze([
  Object.freeze({ id: 'first-finish', name: 'First Lap of Many', line: 'Finish a race.', kind: 'race', hidden: false, reward: null }),
  Object.freeze({ id: 'long-hold', name: 'Long Hold', line: 'Win on every course.', kind: 'lifetime', hidden: false, reward: 'kart:velvet-lock' }),
  Object.freeze({ id: 'deep-end', name: 'Deep End', line: 'Fall off and still win.', kind: 'race', hidden: true, reward: 'kart:undertow' }),
]);

/** @returns {string[]} ids newly earned by this run (placeholder: none) */
export function evaluate() { return []; }
