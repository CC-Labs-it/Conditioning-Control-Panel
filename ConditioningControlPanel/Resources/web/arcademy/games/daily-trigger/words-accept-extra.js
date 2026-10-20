/* ============================================================================
 * games/daily-trigger/words-accept-extra.js - THE ADULT BUILD'S EXTRA GUESSES.
 *
 * Words a player may TYPE on a five-letter day that only the full build should
 * accept: the kink, bimbo and body vocabulary the niche answer bands invite a
 * player to try, plus the handful of explicit words the ordinary-English list
 * used to carry. words-accept.js unions this into ACCEPT, so on every host that
 * loads the full tree the acceptance list is exactly what it always was.
 *
 * The store tree replaces this file with words-accept-extra.store.js (an empty
 * list), which is the whole reason these words are not in words-accept.js.
 * Nothing here is ever SHOWN by the game; it only decides whether a guess the
 * player typed counts as a word. Same format: lowercase a-z, five letters.
 * ==========================================================================*/

export const ACCEPT_EXTRA = `
  bimbo boner goons grope horny hussy hypno kinks kinky latex leash lusty moans naked
  nudes obeys pubes pubic pubis semen sexed sexes sissy slave spank sperm thong booty
  butts boobs busty twerk dildo femme domme roped caged cages kissy licks panty circe
  bambi
`;

export default ACCEPT_EXTRA;
