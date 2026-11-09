import { T_RUN } from '../data/prestige.js';
// Only runs opened in the long preview carry this bag. The save already persists run.store.
export const SPECIALTIES = {
  spec_rebounds: {
    parent: 'stray',
    name: 'Bouncing Button: pick one',
    options: [['steady', 'Every Bounce', 'each bounce pays a little more.'], ['corner', 'Corners', 'a corner hit pays big.']]
  },
  spec_breakout: {
    parent: 'mini_breakout',
    name: 'Breakout: pick one',
    options: [['rally', 'Long Shield', 'shields last 35 seconds.'], ['heavy', 'Heavy Ball', 'one hit breaks armored bricks.']]
  },
  spec_dispenser: {
    parent: 'dispenser',
    name: 'Dispenser: pick one',
    options: [['frequent', 'Faster Refills', 'capsules come sooner.'], ['jackpot', 'Big Jackpots', 'gold is rarer but pays triple.']]
  },
  spec_mix: {
    parent: 'taste2',
    name: 'Second Theme: pick one',
    options: [['bounce', 'Bounces', 'each bounce pays a little more.'], ['tap', 'Taps', 'each tap pays a little more.']]
  }
};
export const ACTIVITIES = ['levels', 'toy_box', 'wheel', 'd_scroll', 'v_frame', 'b_frame', 'quiz', 'mini_breakout', 'dispenser', 'mini_race', 'mini_deep'];
/* [minute the chapter ends, purchase it ends on], on the clock of a player who stays and clicks (owner 2026-11-07: about
 * 80 minutes to Drop for that player, about 2 hours for one who idles, about 1 hour with good multiplier combos).
 * Was [[12,14],[35,28],[70,49],[120,77],[165,102],[205,124],[240,144]]: a 4-hour run. Plan: handoff-1107/tc-pacing/. */
/* Owner 2026-11-07, later: everything 35% faster (x0.65): staying about 52 min, casual about 85-105, combos about 37. */
/* Owner 2026-11-08 ("wait a bit too much in the 1k-3k zone, buy the 10k+ power-ups too fast"): the seconds a purchase takes
 * now only ever rise, chapter by chapter, about 11 / 14 / 19 / 23 / 28 / 32 s for the staying player. Was
 * [[2.34,14],[5.72,28],[11.38,49],[18.2,77],[26,102],[36.4,124],[48.43,144]] = 14.5 / 16.2 / 14.6 / 18.7 / 28.4 / 36.1 s, so
 * the 10k-30k stretch (chapter 4) paced faster than the 1k-3k one (chapter 2). Record: handoff-1108/tc-pacing-midlate.md. */
/* Owner 2026-11-09 ("pan them out to 1.30 hrs on average, atm it's about 1 hr"): ONE stretch knob on the whole timeline.
 * dev/journey-timing.mjs read 61-62 min (4 taps) before it; 1.47 lands the staying player near 90. The chapter shape above
 * is kept as the base; the first chapter's targetSeconds in sim/journey.js take the same factor. */
export const JOURNEY_STRETCH = 1.47;
const BASE_CHAPTERS = [[2.34, 14], [4.91, 28], [9.81, 49], [18.67, 77], [28.26, 102], [38.52, 124], [49.19, 144]];
export const JOURNEY_CHAPTERS = BASE_CHAPTERS.map(([min, end]) => [min * JOURNEY_STRETCH, end]);
/* Renewal Sky (2026-11-09): a renewed run is planned to last T_RUN[depth] minutes (data/prestige.js); 90 = the stretch above.
 * The purchase ends stay; only the minutes scale. */
export const journeyStretch = (minutes = T_RUN[0]) => (minutes === T_RUN[0] ? JOURNEY_STRETCH : JOURNEY_STRETCH * (minutes / T_RUN[0]));
export const journeyChapters = (minutes = T_RUN[0]) => {
  const k = journeyStretch(minutes);
  return k === JOURNEY_STRETCH ? JOURNEY_CHAPTERS : BASE_CHAPTERS.map(([min, end]) => [min * k, end]);
};
export function journeyChapter(s) {
  const purchases = s.buys - Object.keys(s.picks).length;
  const next = JOURNEY_CHAPTERS.findIndex(([, end]) => purchases < end);
  return next < 0 ? JOURNEY_CHAPTERS.length - 1 : next;
}
export const passiveRank = id => ['ticker', 'e_spiral', 'a_hum'].includes(id);
export function rankDescription(id) {
  if (id === 'stray') return 'Each bounce pays a little more.';
  if (id === 'rounded') return 'Each tap pays a little more.';
  if (passiveRank(id)) return 'Pays a little more every second.';
  const collection = {
    levels: 'Collect a level',
    toy_box: 'Finish a Toy Box',
    wheel: 'Spin the Prize Wheel',
    d_scroll: 'Swipe the feed yourself',
    v_frame: 'Press Collect on the picture frame',
    b_frame: 'Press Collect on the empty frame',
    quiz: 'Answer a question',
    mini_breakout: 'Clear a Breakout board',
    dispenser: 'Catch a capsule',
    mini_race: 'Collect the race reward',
    mini_deep: 'Make a match in The Deep End'
  };
  return collection[id] + ' to collect a bonus. Each rank makes it bigger.';
}
export function setupJourney(s) {
  const old = s.store.journey || {};
  const j = {
    ranks: {},
    specs: {},
    banks: {}
  };
  for (const id of ['stray', 'rounded', 'ticker', 'e_spiral', 'a_hum', ...ACTIVITIES]) {
    const n = Math.min(6, Math.floor(Number(old.ranks?.[id])));
    if (!(n > 0) || !s.owned[id]) continue;
    j.ranks[id] = n;
    if (ACTIVITIES.includes(id)) {
      const bank = Number(old.banks?.[id]);
      j.banks[id] = Number.isFinite(bank) ? Math.max(0, Math.min(bank, s.base.fps * n * 1.2)) : 0;
    }
  }
  for (const [id, spec] of Object.entries(SPECIALTIES)) {
    if (s.owned[spec.parent] && spec.options.some(([key]) => key === old.specs?.[id])) j.specs[id] = old.specs[id];
  }
  return s.store.journey = j;
}
export function accrueActivities(s, seconds) {
  const j = s.store.journey;
  for (const id of Object.keys(j.banks)) {
    const rate = s.base.fps * .01 * j.ranks[id];
    j.banks[id] = Math.min(rate * 120, j.banks[id] + rate * seconds);
  }
}
export function takeActivity(s, id) {
  const banks = s.store.journey?.banks;
  if (!banks?.[id]) return 0;
  const amount = banks[id];
  banks[id] = 0;
  return amount * s.pace;
}
