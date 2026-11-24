/* data/editions.js - the two editions of this one game tree (owner 2026-11-22). Plain data, no imports.
 *
 *   small  the website, phones, and free accounts everywhere. No Renewal, no Llama Expo, 56 of the 316 rows never
 *          offered, hard caps on a phone. Runs of about 45 minutes after its own balance pass.
 *   full   inside the CCP desktop app and the standalone, for any subscriber. Everything.
 *
 * A host picks the edition in init caps (CONTRACT.md section 6). ui/edition.js is the ONE reader of that choice and of
 * this file: no other module reads the host's choice or these lists.
 * Plan and lanes: handoff-1122/TC-EDITIONS-PLAN.md. Rules: handoff-1114/TC-STANDING.md "T&C EDITIONS". */

export const EDITIONS = Object.freeze(['small', 'full']);

/** Rows the small edition never offers, by the reason they go. A save that already owns one keeps it asleep
 *  (CONTRACT.md 6.4): nothing here ever deletes. */
export const SMALL_CUT = Object.freeze({
  /* The arcade keeps Mini Slots, the wheel, the boxes and the golden button. */
  arcade: Object.freeze(['toy_box', 'mini_breakout', 'mini_race', 'mini_deep', 'dispenser', 'dispenser_fast', 'dispenser_faster']),
  /* EMI's quiz keeps quiz, melt, nag, crowd, pbubble and onlyyes. */
  quiz: Object.freeze(['swap', 'flee', 'slider', 'shrink', 'fadeno', 'magnet', 'late', 'holdno', 'dragno', 'stackno', 'robot', 'grid', 'ttype', 'regen', 'hijack']),
  /* EMI keeps her head, petting, chat, the hat and the nap. */
  emi: Object.freeze(['em_jeal', 'em_mini', 'em_call', 'em_play', 'em_boss']),
  popups: Object.freeze(['inbox', 'fakechat', 'flash', 'drain', 'triggers', 'opinions', 'listen']),
  /* Every crossover (lane: combo). The test pins this list to the catalog. */
  combos: Object.freeze(['x_dream', 'x_front', 'x_story', 'x_drill', 'x_audio', 'x_gallery', 'x_infinite', 'x_nothing', 'x_later', 'x_listener', 'x_compl', 'x_fourhr', 'x_manners', 'x_induct', 'x_viral', 'x_what']),
  /* Rows that are a desktop thing by nature: toys, real Sparkles from boxes, the right mouse button. */
  desktop: Object.freeze(['buzz', 'toy', 'sparkle', 'dualwield']),
  /* The Llama Expo: its fate card and its ending. The pen and the hunt stay. */
  expo: Object.freeze(['ft_panel', 'll_end']),
});

export const SMALL_CUT_IDS = Object.freeze(Object.values(SMALL_CUT).flat());

/** Hand-made requirement lists for kept rows, small edition only: { id: { req: [...], any?: [...] } }.
 *  Empty means the general rule in ui/edition.js applies: a cut parent hands down its own parents (today that links
 *  Are You Sure to Melting Answers, Only Yes to Bubble Answers and EMI Naps to the hat). The balance pass may pin a
 *  row here instead. */
export const SMALL_REQ = Object.freeze({});

/** The most a phone shows at once in the small edition. Caps, not options: the late game stays readable. */
export const PHONE_CAPS = Object.freeze({ bouncingButtons: 4, bouncingWords: 1, pictureFrames: 1, adBanners: 1 });

/** The small edition's balance (lane S8): every small-only number, in one place. minutes: the Journey plan's planned
 *  length (sim/journey.js timeline), the small run's only length knob. No row price or pay changes: the full game
 *  plays T_RUN[0] and never reads this. */
export const SMALL_BALANCE = Object.freeze({ minutes: 55 });

/** The funnel (owner 2026-11-24): the small edition holds ONE ending, Go Outside. Only this fate set opens in small;
 *  the other sets a full run would be offered are shown locked, by name, and lead to the full game. A fate set id
 *  from data/fate.js. */
export const SMALL_ENDING_SET = 'outside';

/** Nudges: the only two things a `nudge` frame may say (CONTRACT.md 6.2). No free text, no url from the page. */
export const NUDGE_WHERE = Object.freeze(['ending', 'llama8', 'transmission', 'locked-tile', 'pause', 'save-sleep']);
export const NUDGE_WANT = Object.freeze(['account', 'download', 'upgrade']);
/** Where a nudge leads when no host is there to open its own door. Every one passes ui/links.js; `download` is its
 *  DESKTOP_URL (the test pins the two together). */
export const NUDGE_LINKS = Object.freeze({
  account: 'https://app.cclabs.app/tc',
  download: 'https://cclabs.app/ccp.html',
  upgrade: 'https://app.cclabs.app/subscribe',
});

/** Save carry (CONTRACT.md 6.3): the page never offers more than this, and a host never sends more. */
export const SAVE_CARRY_MAX_BYTES = 512000;
/** The save parser reads at most this many characters (sim/save.js parse; the test pins the two together). A real
 *  late-game save is about 27,000. */
export const SAVE_CARRY_MAX_CHARS = 400000;
export const SAVE_FROM = Object.freeze(['web', 'desktop']);
export const SAVE_RESULT_REASONS = Object.freeze(['sign-in', 'too-big', 'offline', 'refused', 'none']);

/** The Expo arrives as a download the player accepts in fiction: the transmission card shows at this many llamas. */
export const TRANSMISSION_AT_LLAMAS = 4;
/** The self-opening cards (transmission, llama 8) wait until the run clock (ms played: pause and suspend do not count)
 *  passes this; the owner asked for the card mid-run. The pen sign and the wormhole never wait. */
export const TRANSMISSION_MIN_RUN_MS = 180000;
/** At most one of the two opens by itself per run; the llama-8 card waits a later run or this much more play. */
export const LLAMA8_GAP_MS = 300000;
export const EXPO_FAIL_REASONS = Object.freeze(['offline', 'disk', 'refused']);
/** Where the Expo's files load from when they sit in the tree (a local preview). A desktop host names its own base. */
export const EXPO_DEFAULT_BASE = 'assets/expo/';
