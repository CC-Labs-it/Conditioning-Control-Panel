/* meta/medals.js - Time Trial medal times per course (CONTRACT-LOGBOOK.md sections 1 and 7).
 *
 * Seconds for the whole trial (every lap). Glass is our own best time, Gold a clean fast run, Silver a
 * steady run, Bronze any finish without long rescues. A course whose times are not set yet (0) awards
 * no medal. The TRIAL lane fills the numbers from measured solo runs and says how in its PR.
 */
export const MEDAL_ORDER = Object.freeze([null, 'bronze', 'silver', 'gold', 'glass']);

export const MEDALS = Object.freeze({
  threshold: Object.freeze({ bronze: 0, silver: 0, gold: 0, glass: 0 }),
  mainframe: Object.freeze({ bronze: 0, silver: 0, gold: 0, glass: 0 }),
  keep: Object.freeze({ bronze: 0, silver: 0, gold: 0, glass: 0 }),
  boudoir: Object.freeze({ bronze: 0, silver: 0, gold: 0, glass: 0 }),
  goonworks: Object.freeze({ bronze: 0, silver: 0, gold: 0, glass: 0 }),
});

/** @returns {null|'bronze'|'silver'|'gold'|'glass'} the best medal the time earns */
export function medalFor(trackId, timeS) {
  const m = MEDALS[trackId];
  if (!m || !(timeS > 0)) return null;
  for (const name of ['glass', 'gold', 'silver', 'bronze']) if (m[name] > 0 && timeS <= m[name]) return name;
  return null;
}

/** 0 for none up to 4 for glass. */
export const medalRank = (name) => Math.max(0, MEDAL_ORDER.indexOf(name ?? null));
