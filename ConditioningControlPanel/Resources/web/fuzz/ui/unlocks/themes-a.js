/* Lanes: trance bimbo drone sissy. One file per theme under themes-a/, the shared bits in themes-a/kit.js. See ENGINE.md. */
import trance, { START as TRANCE } from './themes-a/trance.js';
import bimbo, { START as BIMBO } from './themes-a/bimbo.js';
import drone, { START as DRONE } from './themes-a/drone.js';
import sissy, { START as SISSY } from './themes-a/sissy.js';
import { startTheme } from './themes-a/kit.js';

const STARTS = { trance: TRANCE, bimbo: BIMBO, drone: DRONE, sissy: SISSY };

export default {
  /* Picking the theme is the big moment: the page turns at once. The head row then brings its pictures. */
  f5: {
    mount(ctx) {
      const spec = STARTS[ctx.pick('f5')];
      return spec ? startTheme(ctx, spec) : {};
    },
  },
  ...trance, ...bimbo, ...drone, ...sissy,
};
