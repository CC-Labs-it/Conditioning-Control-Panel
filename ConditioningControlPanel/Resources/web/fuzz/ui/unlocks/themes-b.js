/* Lanes: goon locked censored infection. See ENGINE.md. One file per theme in ./themes-b/.
 *
 * f5 (Pick a Theme) is registered here because the page has to change on the pick, not on the later buy. It only acts on
 * this lane's four themes. start is registered only to give a friend who opened a share link their one spot. */

import goon, { goonPick } from './themes-b/goon.js';
import locked, { lockedPick } from './themes-b/locked.js';
import censored, { censoredPick } from './themes-b/censored.js';
import infection, { infectionPick, sharedSpot } from './themes-b/infection.js';
import { buttonWords, dress, fresh, shopFlavour, tickerLines } from './themes-b/kit.js';

const PICKS = { goon: goonPick, locked: lockedPick, censored: censoredPick, infection: infectionPick };

export default {
  start: { mount(ctx) { sharedSpot(ctx); return {}; } },

  f5: {
    mount(ctx) {
      const lane = ctx.pick('f5');
      if (!PICKS[lane]) return {};
      const undress = dress(ctx, lane);
      const unword = buttonWords(ctx, lane);
      tickerLines(ctx, lane);
      shopFlavour(ctx, lane);
      if (fresh(ctx)) PICKS[lane].arrive(ctx);
      return { off() { undress(); unword(); } };
    },
  },

  ...goon,
  ...locked,
  ...censored,
  ...infection,
};
