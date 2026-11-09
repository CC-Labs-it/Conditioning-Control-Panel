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
export const ACTIVITIES = ['levels', 'toy_box', 'wheel', 'd_scroll', 'v_frame', 'b_frame', 'quiz', 'mini_breakout', 'dispenser', 'mini_race', 'mini_deep', 'mini_slots'];
/* Late game wave (owner 2026-11-10, handoff-1110/TC-LATE-GAME-BRIEF.md). The on-screen games: their rank 1 and 2 are named
 * upgrades that change how the game plays (the game reads run.store.journey.ranks[id] live each round); rank 3 and 4 keep
 * the bigger collect bonus. A game's first rank is never offered sooner than SAVOUR_BUYS purchases after it arrived. */
export const GAMES = ['wheel', 'mini_slots', 'toy_box', 'mini_race', 'mini_breakout', 'mini_deep', 'dispenser'];
export const GAME_RANK_MAX = 4;
export const RANK_MAX = 6;
export const SAVOUR_BUYS = 15;
export const rankCap = (id) => (GAMES.includes(id) ? GAME_RANK_MAX : RANK_MAX);
export const GAME_UPGRADES = Object.freeze({
  wheel: [{ name: 'Respin', what: 'Landing Nothing gives one free respin at once.' }, { name: 'Big Wedge', what: 'The 10x wedge doubles in width. Its odds double with it.' }],
  mini_slots: [{ name: 'Hold', what: 'After a spin, tap a reel to hold it for the next spin. Once per coin.' }, { name: 'Fourth Coin', what: 'Room for 4 coins, and a coin every 35 seconds.' }],
  toy_box: [{ name: 'Golden Piece', what: "One piece per box is gold. Placing it doubles that box's prize." }, { name: 'Big Box', what: 'The box stays open 30% longer and pays 1.5x.' }],
  mini_race: [{ name: 'Pick the Course', what: 'Choose which of the five courses plays next.' }, { name: 'Nitro', what: 'The reward timer fills 30% faster.' }],
  mini_breakout: [{ name: 'Multiball', what: 'Each board starts with three balls.' }, { name: 'Wide Paddle', what: 'The paddle is 50% wider.' }],
  mini_deep: [{ name: 'Bigger Board', what: 'The board grows to 5x5.' }, { name: 'Chains', what: 'Each extra match in one move pays 1.5x the one before.' }],
  dispenser: [{ name: 'Wide Catch', what: '4 seconds to catch instead of 2.5.' }, { name: 'Double Drop', what: 'Each crank drops two capsules.' }]
});
/** The named upgrade a game rank brings ({name, what}), or null (rank 3+, or not a game). */
export const gameUpgrade = (id, rank) => GAME_UPGRADES[id]?.[rank - 1] || null;
/* Every non-game rank is felt (owner 2026-11-10: "most are weak"; each was +0-0.3% of income). Sized so one rank lifts a
 * staying player's total income about 3-6% at the time it is bought (node dev/journey-timing.mjs --ranks prints it):
 *   stray: each wall hit of a bouncing button carries RANK_BOUNCE more of a second of base per-second pay, per rank;
 *   rounded: each manual tap carries RANK_TAP more of it, per rank;
 *   ticker / e_spiral / a_hum: per-second pay rises RANK_PASSIVE of base, per rank;
 *   levels / picture frames / quiz: the collect bank fills RANK_BANK of base per second, per rank (games keep GAME_BANK). */
export const RANK_BOUNCE = .038;
export const RANK_TAP = .03;
export const RANK_PASSIVE = .12;
export const RANK_BANK = .27;
export const GAME_BANK = .01;
export const BANK_CAP_S = 120;
/** Seconds of base per-second pay a collect bank fills each second at this rank. */
export const bankRate = (id, rank) => (GAMES.includes(id) ? GAME_BANK : RANK_BANK) * (rank || 0);
/* The four late trunk shows (Spiral Overlay, Page Tilt, Wallpaper, Warp Tunnel) each multiply ALL Journey income by
 * TRUNK_BOOST (owner 2026-11-10), so the counter visibly jumps. Folded after the knees, like a `post` mod. The Journey
 * plan's shadow run sees it too, so later prices rise with it and the run keeps its length. */
export const TRUNK_SHOWS = ['overlay', 'takeover', 'wallpaper', 'tunnel'];
export const TRUNK_BOOST = 1.5;
export const trunkBoost = (s) => (s.store?.journey ? TRUNK_BOOST ** TRUNK_SHOWS.filter((id) => (s.owned[id] || 0) > 0).length : 1);
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
/* 2026-11-10 (late game wave): 1.47 -> 1.51. Felt ranks bought cheapest-first ran the staying player about 3 minutes ahead
 * of the plan (87 min); 1.51 lands it at 89-90 again. */
export const JOURNEY_STRETCH = 1.51;
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
/** The shop name of rank row `rank` (1 = the second level) of `name`: a game's first two are its named upgrades. */
export function rankName(id, rank, name) {
  const up = gameUpgrade(id, rank);
  return up ? name + ': ' + up.name : name + ' · Rank ' + (rank + 1);
}
export function rankDescription(id, rank = 0) {
  const up = gameUpgrade(id, rank);
  if (up) return up.what;
  if (id === 'stray') return 'Each bounce pays more.';
  if (id === 'rounded') return 'Each tap pays more.';
  if (passiveRank(id)) return 'Pays more every second.';
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
    mini_deep: 'Make a match in The Deep End',
    mini_slots: 'Win on the Mini Slots'
  };
  return collection[id] + ' to collect a bonus. Each rank makes it bigger.';
}
export function setupJourney(s) {
  const old = s.store.journey || {};
  const j = {
    ranks: {},
    specs: {},
    banks: {},
    arrive: {}  // game id -> s.buys when it was bought (the savour rule); a save from before it has none
  };
  s.store.journey = j;  // trunkBoost below reads it
  for (const id of ['stray', 'rounded', 'ticker', 'e_spiral', 'a_hum', ...ACTIVITIES]) {
    const n = Math.min(rankCap(id), Math.floor(Number(old.ranks?.[id])));
    if (!(n > 0) || !s.owned[id]) continue;
    j.ranks[id] = n;
    if (ACTIVITIES.includes(id)) {
      const bank = Number(old.banks?.[id]);
      j.banks[id] = Number.isFinite(bank) ? Math.max(0, Math.min(bank, s.base.fps * bankRate(id, n) * BANK_CAP_S * trunkBoost(s))) : 0;
    }
  }
  for (const id of GAMES) {
    const at = Number(old.arrive?.[id]);
    if (s.owned[id] && Number.isFinite(at)) j.arrive[id] = at;
  }
  for (const [id, spec] of Object.entries(SPECIALTIES)) {
    if (s.owned[spec.parent] && spec.options.some(([key]) => key === old.specs?.[id])) j.specs[id] = old.specs[id];
  }
  return s.store.journey = j;
}
export function accrueActivities(s, seconds, factor = 1) {
  const j = s.store.journey;
  const boost = trunkBoost(s) * (factor > 0 ? factor : 1);
  for (const id of Object.keys(j.banks)) {
    const rate = s.base.fps * bankRate(id, j.ranks[id]) * boost;
    j.banks[id] = Math.min(rate * BANK_CAP_S, j.banks[id] + rate * seconds);
  }
}
export function takeActivity(s, id) {
  const banks = s.store.journey?.banks;
  if (!banks?.[id]) return 0;
  const amount = banks[id];
  banks[id] = 0;
  return amount * s.pace;
}
