/* ============================================================================
 * data/content/small-funnel.js - the small edition's doors to the full game (owner 2026-11-24,
 * handoff-1124/tc-small-funnel/BRIEF.md). Pure data, the small edition only: the full game never shows a line of it.
 *
 * COPY: EVERY STRING HERE IS A DRAFT for the owner (handoff-1124/tc-small-funnel/COPY-DRAFT-B.md lists each one with
 * where it shows). Swap a line here and nowhere else. The approved nudge lines stay in ui/nudges.js, frozen.
 *
 * Counts never live here: the wall card reads them from data/editions.js SMALL_CUT and the catalog ({n} is filled in).
 * Ending names come from the catalog too. Tokens {game} and {cur} as in data/brand.js.
 * ==========================================================================*/

/** A fate seed the small edition keeps locked, on the seed shelf beside the open one. */
export const LOCKED_SEED_COPY = Object.freeze({
  /** The stamp across the card, where a set that was not picked says NOT THIS RUN. */
  stamp: 'FULL GAME',
  /** Read aloud after the ending's name: "EMI Takeover. In the full game, in CCP." */
  aria: 'In the full game, in CCP.',
});

/** The one card on the recap after the run (ui/recap/wall.js). */
export const SMALL_WALL_COPY = Object.freeze({
  /** The Courier tag on the panel's edge. */
  cap: 'The rest of it',
  /** The plain offer, always this plain. */
  offer: 'The full game is in CCP, for subscribers.',
  /** Under the offer: the joke may run a little longer than the chrome. */
  sub: 'You got the one ending that lets you leave. The others do not, and they are in the app.',
  /** The fan-out of what small leaves out, one pill each. {n} comes from the data. */
  parts: Object.freeze({
    arcade: '{n} arcade toys',
    quiz: '{n} more quiz tricks for EMI',
    popups: '{n} pop-ups',
    combos: '{n} crossovers',
    renewal: 'Renewal, {n} stars',
    expo: 'The Llama Expo',
  }),
  /** The main button: the plan door (nudge where 'ending', want 'upgrade'). */
  main: 'See the plans',
  /** The second button: the app itself (nudge where 'ending', want 'download'). Web only: inside CCP it is already there. */
  second: 'Get CCP',
});

/** The real CCP cards of the ad rotation, small edition only: each one points at the full game. The joke ads are the
 *  same in both editions. Same shape as data/content/ads.js CCP_ADS; `small: true` marks them for tests. */
const CCP_URL = 'https://cclabs.app';
const card = (id, kind, headline, sub, cta) => ({ id, kind, headline, sub, cta, url: CCP_URL, clip: null, small: true });

export const SMALL_CCP_ADS = Object.freeze([
  card('s_full', 'banner', 'This is the small edition.', 'The full game is in CCP, for subscribers. It has more endings and fewer excuses.', 'See the full game'),
  card('s_endings', 'banner', 'There are more ways out of this.', 'The other endings are in the full game, in CCP, for subscribers.', 'See the endings'),
  card('s_honest', 'banner', 'An honest ad, about this very game.', 'You are playing the free part. The rest of it is in CCP, for subscribers.', 'See the rest'),
  card('s_emi', 'popup', 'EMI knows more tricks than this.', 'She saves them for the full game, in CCP. She is not sorry about it.', 'Meet her properly'),
  card('s_socks', 'banner', 'The only product on this network that exists.', 'The full game of {game}. In CCP, for subscribers. Sorry about the socks.', 'See it exist'),
  card('s_llama', 'banner', 'The llamas have an Expo.', 'It opens in the full game, in CCP. They have been rehearsing.', 'Ask the llamas'),
]);
