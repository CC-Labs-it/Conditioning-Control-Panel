/* The existing llama fate card now opens the Expo. The in-run build-up is a separate lane. */

import { expoEnding, PORTAL_DARK } from './llama/ending.js';

export default {
  /* Section 17: The Panel. The row is the door; nothing on the page. */
  ft_panel: { mount() { return {}; } },

  ll_end: {
    /* Nothing to draw during the run; the row is the door. */
    mount() { return {}; },
    /* The visit owns its lifetime; the gift shop hands back to the ordinary recap. */
    fadeColour: PORTAL_DARK,
    fadeMs: 180,
    take: expoEnding,
    takeMaxMs: null,
    showMaxMs: null,
  },
};
