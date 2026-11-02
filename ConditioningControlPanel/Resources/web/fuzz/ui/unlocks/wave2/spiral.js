/* ui/unlocks/wave2/spiral.js - Spiral Colours and Spiral Shapes. Both only flip a switch on the bus: every spiral on the
 * page already listens to it (fx/spiral-bus.js). Switched off, the colours settle back and the shape melts home. */

import { spiralBus } from '../../../fx/spiral-bus.js';

const newest = (ctx) => { const o = ctx.state.order; return o[o.length - 1] === ctx.item.id; };

export default {
  sp_colors: {
    mount(ctx) {
      spiralBus.use(ctx);
      spiralBus.setDrift(true, { arrive: newest(ctx) });
      return { off() { spiralBus.setDrift(false); } };
    },
  },

  sp_shapes: {
    mount(ctx) {
      spiralBus.use(ctx);
      spiralBus.setShapes(true, {
        rng: ctx.rng,
        start: ctx.store.get('at') || 0,
        arrive: newest(ctx),
        onSettle: (i) => ctx.store.set('at', i),
      });
      return { off() { spiralBus.setShapes(false); ctx.store.set('at', 0); } };
    },
  },
};
