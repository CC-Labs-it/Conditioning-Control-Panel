/* ============================================================================
 * games/daily-trigger/words-answers.store.js - THE ANSWER POOL, store build.
 *
 * In the store tree this file IS words-answers.js: the vendoring script copies
 * it over its sibling (core/storesafe.js has the whole mechanism), so bank.js
 * imports it under the usual name and the full pool is not in that download at
 * all. Every other host loads the full tree, where nothing imports this file.
 *
 * Same exports, same shape: THEME_GROUPS / THEME / COMMON / PHRASES /
 * CAT_NAMES. The day's word is still niche, as the owner ruled on 2026-08-25:
 * a store build is a narrower version of the same idea, not random English.
 * It keeps the school, arcade, drift, practice and happy bands, and grows one
 * band of ordinary calm so the pool stays big enough to be a pool.
 *
 * SELF-CONTAINED ON PURPOSE. It cannot import its sibling, because in the store
 * tree its sibling is this file. The kept bands are COPIED, so a word added to
 * words-answers.js does not reach a store build until somebody adds it here.
 * That is the safe direction for the two to drift in.
 *
 * SIZE IS LOAD-BEARING, the same way it is next door: bank.js walks one shuffled
 * pass of the whole pool before any word repeats, so the pool length IS the
 * repeat-free horizon in days. Keep the union above ~400 (it is 431).
 *
 * FORMAT: whitespace-separated lowercase a-z, exactly five letters. bank.js
 * filters, dedupes and sorts on load.
 * ==========================================================================*/

/**
 * The bands. `cat` is the stable key behind EMI's stuck-hint (`dt_cat_<cat>`),
 * so the keys the full pool shares keep their names.
 */
export const THEME_GROUPS = Object.freeze([
  /* drift / dreamy (83) */
  Object.freeze({ cat: 'trance', words: `
  abyss blank blink dazed dazes depth dozed dozes dream drift drips drone drown empty faint fades
  faded float focus foggy fuzzy gazed gazes glaze hazed hazes heavy limbo lower lulls melts misty
  murky sinks sleep slept spell stare still sways swung swirl swoon tides tones trust under whirl
  woozy yield yearn blurs dulls numbs inert vapor glass gleam glint glows watch clock ticks timed
  count chime chant verse lines words voice dizzy giddy heady tipsy dopey mushy melty waken woken
  awake wakes dives
  ` }),
  /* practice / habits (43) */
  Object.freeze({ cat: 'training', words: `
  train learn drill habit reset prime loops cycle daily rites study teach tutor coach guide shape
  molds mould wired wires brand stamp carve tuned tunes tweak nudge minds brain patch fixed edits
  wipes wiped erase clean slate fresh again creed dogma faith rerun
  ` }),
  /* app features / arcade (102) */
  Object.freeze({ cat: 'arcade', words: `
  flash video timer level badge quest drain popup panic chaos pause plays press click audio sound
  sonic noise texts fonts pixel frame blast combo bonus gifts prize medal ranks coins token chips
  punch cards decks wheel reels spins slots lucky bingo lotto dealt deals games gamer retry round
  extra lives heart bells dings alarm beats tempo loopy noisy mists smoke cloud twist twirl whorl
  coils orbit droid robot cyber codes coded input bytes error crash froze stuck stiff stone tiles
  score cheat mazes arena laser block dodge jumps snaps boost blitz clone ghost halos super shift
  tempt worth prism ocean waves rocks
  ` }),
  /* school framing (27) */
  Object.freeze({ cat: 'school', words: `
  class grade exams pupil chalk board desks notes books pages paper marks tests essay honor merit
  award tiers years halls plaid pleat tasks chore tardy later dorms
  ` }),
  /* happy (15) */
  Object.freeze({ cat: 'melt', words: `
  vapid inane batty dotty kooky wacky happy laugh merry jolly enjoy bliss elate highs light
  ` }),
  /* ordinary calm (115): attention, weather, growing things, small comforts and
   * a little music. None of it collides with the bands above. */
  Object.freeze({ cat: 'focus', words: `
  alert aware acute clear crisp brisk lucid sober plain think recap tally recur
  peace relax calms comfy cushy downy couch quilt duvet sheet linen towel robes
  socks shawl scarf hands palms wrist ankle thumb lungs yawns snore
  beach shore sands dunes hills vales woods trees ferns grass field river brook
  creek lakes ponds marsh reeds mossy pines cedar birch maple aspen elder olive
  lemon melon plums pears grape toast bread scone juice water
  robin finch doves swans geese otter sheep lambs birds herds
  piano chord hymns lyric organ flute cello harps
  draft index shelf plans query facts logic proof chart graph
  sunny rainy windy balmy chill frost snows gusts storm
  hours weeks month today dates
  ` }),
]);

/** The flat pool bank.js parses: one space-joined string, same contract. */
export const THEME = THEME_GROUPS.map((g) => g.words).join(' ');

/** The campus / sweet shop / arcade band, the same as the full pool's. */
export const COMMON = `
  bench chair early night smirk sleek sharp taste smell sense scale share stage staff stash steam
  sauna spare ready quote print paint piece point music movie media radio jelly fudge syrup cocoa
  mango berry apple funny nifty handy hello yours drama fable theme topic truly valid
`;

/** Phrase days: two groups, a free gap, ten letters or fewer. */
export const PHRASES = Object.freeze([
  ['dont', 'think'], ['sink', 'deep'], ['drop', 'down'], ['pink', 'mist'],
  ['soft', 'focus'], ['deep', 'sleep'], ['blank', 'slate'], ['stay', 'down'],
  ['melt', 'away'], ['drift', 'off'], ['thank', 'you'], ['slow', 'blink'],
  ['heavy', 'eyes'], ['let', 'go'], ['count', 'down'], ['press', 'play'],
  ['high', 'score'], ['game', 'over'], ['bonus', 'round'], ['free', 'play'],
  ['quiet', 'mind'], ['sweet', 'spot'], ['keep', 'still'], ['one', 'more'],
]);

/** The bands' display names, the stuck-hint's fallback. The shared keys keep
 *  the host lexicon's wording, so whichever of the two a build reads agrees. */
export const CAT_NAMES = Object.freeze({
  trance: 'spirally', training: 'training arc', arcade: 'hometown',
  school: 'classroom', melt: 'melty', focus: 'fresh air', common: 'civilian',
});

export default { THEME, THEME_GROUPS, COMMON, PHRASES, CAT_NAMES };
