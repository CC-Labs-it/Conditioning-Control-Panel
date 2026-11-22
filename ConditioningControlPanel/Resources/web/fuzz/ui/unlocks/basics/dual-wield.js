/* ui/unlocks/basics/dual-wield.js - Double Wielding (owner 2026-11-08): the right mouse button presses the game button
 * and its bouncing copies too. The press rule lives in ui/hands.js and the press path in ui/app.js (ctx.button.bothHands);
 * this row only switches it on while mounted, so the Control Panel can switch it off. A right-hand press leaves a small
 * ring in the second accent, so the two hands read apart. Gentle: one soft ring (fx.rings). */

import { rings } from './fx.js';

export default {
  dualwield: {
    mount(ctx) {
      ctx.button.bothHands((ev) => rings(ctx, ev.clientX, ev.clientY, { color: 'var(--accent-2, #6fd6ff)', n: 2, size: 64 }));
      return {};
    },
  },
};
