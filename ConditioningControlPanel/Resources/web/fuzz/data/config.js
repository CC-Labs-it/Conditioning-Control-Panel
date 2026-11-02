/* data/config.js - switches and tunables that are not one item's numbers. Open owner calls live here so flipping one
 * is a one-line change. */

export const CONFIG = Object.freeze({
  /** Owner call open. One payment per ad, worth this many seconds of rate. */
  AD_CLICK_PAYS_SECONDS: 10,
  /** Owner call open. Bambi clips never play on the web build while this is false. */
  BAMBI_VOICE_ON_WEB: false,

  /** Wall hits per second per bouncing button, and what a tap rate is measured against in the route test. */
  WALLS_PER_STRAY: 0.35,
  /** The route test's greedy player stops stacking strays once one takes longer than this to pay back. */
  STRAY_PAYBACK_S: 45,

  /** Where pictures come from until the player chooses: online, own or none (ui/pictures.js). */
  PICTURES_DEFAULT: 'none',

  /** Rows in the shop window. The goal row is extra. */
  SHOP_MIN: 3,
  SHOP_MAX: 8,

  /** Pace is a dev door: it multiplies income, not the clock. */
  MAX_PACE: 500,

  /** ctx.speed is held inside these, however many unlocks multiply it. */
  SPEED_MIN: 0.1,
  SPEED_MAX: 4,

  /** A guarded row goes through after this many refusals, whatever the guard says. */
  GUARD_MAX_REFUSALS: 3,

  /** An unlock's card nobody answers lets go after this long of game time, counted from the last press on it. */
  CARD_GIVE_UP_MS: 15000,

  /** A button lock is always temporary. */
  LOCK_MAX_MS: 30000,
  /** How long an ending's own show may hold the credits back. */
  ENDING_SHOW_MAX_MS: 90000,
  /** Live flying numbers and floats, across every unlock. */
  MAX_PARTICLES: 40,
});
