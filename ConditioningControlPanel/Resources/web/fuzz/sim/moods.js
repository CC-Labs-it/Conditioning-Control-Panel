/* sim/moods.js - the Run Moods (lane `meta`), rolled at run start, first run excepted. Pure numbers only. A mood's
 * drawing (the first card, EMI early) belongs to its registry entry; `state.mood` is what everything reads. */

const AD_LANES = new Set(['sell']);

export const MOODS = Object.freeze({
  m_static: { walls: 2 },
  m_pink: { price: (v, item) => (item.id === 'colour' ? 0 : item.lane === 'look' ? v * 0.5 : v * 1.1) },
  m_quiet: { pay: (v, item) => (item.lane === 'sweet' ? v * 1.25 : v), silentUntil: ['a_phones', 'a_rain'] },
  m_rush: { price: (v) => v * 0.75, window: 3 },
  m_lucky: { goldenEvery: 0.5, critChance: 0.5 },
  m_broke: { pay: (v, item) => (item.id === 'banner' || AD_LANES.has(item.lane) ? v * 2 : v * 0.9), adClick: 2 },
  m_watch: { emiAt: 1000 },
});

export const MOOD_IDS = Object.freeze(Object.keys(MOODS));

export const moodOf = (s) => (s.mood && MOODS[s.mood]) || null;

/** What a press on an ad is worth against the plain price, in this run. */
export const adClickFactor = (s) => moodOf(s)?.adClick ?? 1;

/** The first run stays a plain button: moods start with the second. */
export function rollMood(runsDone, rng = Math.random) {
  if (runsDone < 1) return null;
  return MOOD_IDS[Math.floor(rng() * MOOD_IDS.length)];
}
