/* ============================================================================
 * data/content/small-funnel.js - the small edition's doors to the full game (owner 2026-11-24,
 * handoff-1124/tc-small-funnel/BRIEF.md). Pure data, the small edition only: the full game never shows a line of it.
 *
 * COPY: the wall card, its comparison list, the buttons, the DLC stamp, the title tag and the six ad texts are
 * APPROVED by the owner and FROZEN (handoff-1124/tc-small-funnel/COPY-APPROVED.md; tests/small-funnel-copy.test.mjs
 * pins every one letter for letter). Never change a character without the owner's yes on old against new. Still drafts
 * (COPY-DRAFT-B.md): the locked card's screen-reader line and the ad cards' button labels. The approved nudge lines
 * stay in ui/nudges.js, frozen.
 *
 * Counts never live here: the wall card reads them from data/editions.js SMALL_CUT, the catalog and the fan of locked
 * endings ({n} is filled in). Ending names come from the catalog too. Tokens {game} and {cur} as in data/brand.js.
 * ==========================================================================*/

/** The tag beside the game's title wherever the small edition shows it as a title (the main menu). Courier, never in full. */
export const EDITION_TAG_COPY = Object.freeze({
  tag: 'FREE EDITION',
});

/** A fate seed the small edition keeps locked, on the seed shelf beside the open one. */
export const LOCKED_SEED_COPY = Object.freeze({
  /** The stamp across the card, where a set that was not picked says NOT THIS RUN. */
  stamp: 'DLC',
  /** Read aloud after the ending's name: "EMI Takeover. In the full game, in CCP." (draft) */
  aria: 'In the full game, in CCP.',
});

/** One row of the wall's comparison list: the perk, then its side tag. */
const row = (perk, tag) => Object.freeze({ perk, tag });

/** The one card on the recap after the run (ui/recap/wall.js). */
export const SMALL_WALL_COPY = Object.freeze({
  /** The Courier tag on the panel's edge. */
  cap: 'The upsell, as promised',
  /** The plain offer, always this plain. */
  offer: 'The full game is in CCP, for subscribers.',
  /** Under the offer. */
  sub: 'This is the part where a free game discovers it has a paid version. We held out for forty-five minutes, which by the standards of this industry makes us saints.',
  /** The comparison list, one row per perk, in this order. {n} comes from the data (endings = the locked fan). */
  parts: Object.freeze({
    endings: row('{n} more endings', "though, let's be honest, you will probably live without five more endings"),
    arcade: row('{n} arcade toys', 'cut from the free edition for space, and by space we mean money'),
    quiz: row('{n} more quiz tricks for EMI', 'she does them for subscribers, and wants you to know it is not personal'),
    popups: row('{n} pop-ups', 'yes, we are charging for pop-ups, and somehow it is working'),
    combos: row('{n} crossovers', 'nobody has ever needed sixteen of anything, and yet here we are'),
    renewal: row('Renewal, {n} stars', 'you finish the game and then pay to start it again, which is our best trick'),
    expo: row('The Llama Expo', "The best part of the game, according to the dev's mom."),
  }),
  /** The main button: the plan door (nudge where 'ending', want 'upgrade'). */
  main: 'Fall into the DLC trap',
  /** The second button: the app itself (nudge where 'ending', want 'download'). Web only: inside CCP it is already there. */
  second: 'Just get CCP',
});

/** The real CCP cards of the ad rotation, small edition only: each one points at the full game. The joke ads are the
 *  same in both editions. Same shape as data/content/ads.js CCP_ADS; `small: true` marks them for tests.
 *  Each approved text is ONE line split over headline and sub without changing a word: headline + ' ' + sub is the
 *  approved text exactly. The cta labels are still drafts. */
const CCP_URL = 'https://cclabs.app';
const card = (id, kind, headline, sub, cta) => ({ id, kind, headline, sub, cta, url: CCP_URL, clip: null, small: true });

export const SMALL_CCP_ADS = Object.freeze([
  card('s_full', 'banner', 'This is the free edition, which is the same game with the good parts taken out and sold back to you.', 'We did not invent that business model, but we have come to love it.', 'See the full game'),
  card('s_endings', 'banner', 'Do not fall into the DLC trap.', 'The dev probably left the best endings out because the dev wants your money. They are in CCP, for subscribers.', 'See the endings'),
  card('s_honest', 'banner', 'An honest ad, for once:', 'you are playing the sample, the rest is in CCP, and we both know how this ends.', 'See the rest'),
  card('s_emi', 'popup', 'EMI knows more tricks than this', 'and is saving them for people who pay her, a policy she describes as nothing personal.', 'Meet her properly'),
  card('s_socks', 'banner', 'The only product on this network that actually exists is the rest of this game.', 'Sorry about the socks.', 'See it exist'),
  card('s_llama', 'banner', 'The llamas are holding an Expo in the full game.', 'You could live a full and happy life without seeing it. They would notice, though.', 'Ask the llamas'),
]);
