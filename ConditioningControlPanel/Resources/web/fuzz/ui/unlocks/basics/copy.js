/* ui/unlocks/basics/copy.js - lines the basics lane needs that data/content does not have. Same rules: plain, dry, no
 * em-dashes, no exclamation marks, tokens {game} and {cur}. */

/* remote Friend Remote: there is no friend wire. The card says so before anyone goes looking for one. */
export const REMOTE_SOLO = Object.freeze({
  title: 'Friend Remote',
  note: 'The buttons a friend would get. No friend is on the other end, so these press you, and you are the one pressing them. Pause is always yours.',
  chip: 'Remote',
  toasts: Object.freeze({
    poke: 'You poked yourself.',
    spin: 'You spun it.',
    pink: 'You made it pinker.',
    tap: 'You tapped for you. Say thanks.',
    hush: 'You told you to be quiet.',
  }),
});

/* taste2 Second Theme */
export const SECOND_THEME = Object.freeze({
  title: 'Second Theme',
  ask: 'Pick one more. It brings its button and its pictures.',
  chosen: '{theme} too. For research.',
  chip: 'Theme two',
  empty: 'No pictures yet. Add your files from the Wake Up card.',
});

/* listen Listening Test */
export const LISTEN_NAME = 'EMI';
