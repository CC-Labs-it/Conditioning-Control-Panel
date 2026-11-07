/* ui/unlocks/wave2/edges.js - The ASMR player: the same player as the podcasts, one show per theme.
 * Reads assets/audio/asmr/<theme>/manifest.json, the podcast manifest's shape. Missing is silence, and the player says Off air. */

import { mountLofi } from '../lofi-player.js';
import { mountPodcast } from '../podcast-player.js';

export default {
  lofi_radio: { mount: mountLofi },
  asmr: {
    mount(ctx) {
      const theme = ctx.pick('f5') || 'general';
      return mountPodcast(ctx, { show: 'Closer', label: 'ASMR', base: `assets/audio/asmr/${theme}/` });
    },
  },
};
