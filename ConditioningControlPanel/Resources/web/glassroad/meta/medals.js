/* meta/medals.js - Time Trial medal times per course (CONTRACT-LOGBOOK.md sections 1 and 7).
 *
 * Seconds for the whole trial (every lap). Glass is our own best time, Gold a clean fast run, Silver a
 * steady run, Bronze any finish without long rescues. A course whose times are not set yet (0) awards
 * no medal. The TRIAL lane fills the numbers from measured solo runs and says how in its PR.
 * Measured 2026-10-26: the AI drove a lone player (no bots, no items; hazard steer, moving traps and the
 * Mainframe industry on, as boot.js buildRun) at 60 Hz, seeds 1 to 6. Glass = AI best minus 2% (floor),
 * Gold = AI average, Silver = average plus 8%, Bronze = average plus 20% (ceil). AI best / average, s:
 * threshold 585.97 / 588.50, mainframe 787.52 / 789.89, keep 524.91 / 530.72, boudoir 559.09 / 563.56,
 * goonworks 397.72 / 401.83.
 * Cross-check: the same harness and seeds with 8 karts puts the player within about 1% of the solo times
 * (threshold seed 3: 577.6 s in traffic, 578.8 s alone), so a lone kart is not slowed. The courses are long:
 * Threshold grew from about 1.7 km to 5.4 km a lap on 2026-10-24 (d6db721c0), so a 3 lap race is about
 * 10 minutes at the 30 m/s top speed; "about 3 min" race notes predate that.
 */
export const MEDAL_ORDER = Object.freeze([null, 'bronze', 'silver', 'gold', 'glass']);

export const MEDALS = Object.freeze({
  threshold: Object.freeze({ bronze: 707, silver: 636, gold: 589, glass: 574 }),
  mainframe: Object.freeze({ bronze: 948, silver: 854, gold: 790, glass: 771 }),
  keep: Object.freeze({ bronze: 637, silver: 574, gold: 531, glass: 514 }),
  boudoir: Object.freeze({ bronze: 677, silver: 609, gold: 564, glass: 547 }),
  goonworks: Object.freeze({ bronze: 483, silver: 434, gold: 402, glass: 389 }),
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
