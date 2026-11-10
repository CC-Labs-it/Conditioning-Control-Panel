/* ui/unlocks/extras/exits.js - the two ways out that are not Drop. Go Outside goes outside, and it was only
 * a monitor (./outside-show.js). Endless Mode is the end that does not happen: The End shows up, gets pushed aside, and everything goes on. */

import { ENDINGS } from '../../../data/content/endings.js';
import { AFTER_COPY as C } from './copy.js';
import { FONT, h, pick } from './kit.js';
import { outsideShow } from './outside-show.js';
import { livingAdEnding, LIVING_AD_SHOW_MAX_MS } from '../../adcard/ending.js';

export const EXIT_UNLOCKS = {
  /* Go Outside (2026-11-09, handoff-1109/TC-GO-OUTSIDE-ENDING-BRIEF.md): a white fade onto a playable blocky meadow, then
   * the pull back shows it was only a monitor in a dim room (./outside-show.js). No way out, and the tab stays open. */
  outside: {
    /* Nothing to draw during the run; the row is the door. */
    mount() { return {}; },
    fadeColour: '#fff',
    showMaxMs: LIVING_AD_SHOW_MAX_MS + 60000,
    async ending(ctx) {
      if (!await outsideShow(ctx, ENDINGS.outside)) return;
      const cancelled = new Promise((resolve) => {
        ctx.on('suspend', (s) => { if (s?.on && s.reason === 'panic') resolve(); });
        ctx.cleanup(resolve);
      });
      await Promise.race([livingAdEnding(ctx), cancelled]);
    },
  },

  onemore: {
    mount(ctx) {
      const E = ENDINGS.onemore;
      if (!ctx.store.get('shown')) {
        ctx.store.set('shown', 1);
        const panel = ctx.el('div', `position:absolute;inset:0;z-index:15;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:10px;padding:24px;background:#14101c;color:#fff;font:600 16px/1.3 ${FONT};text-align:center;pointer-events:auto;`, null, 'over');
        h('div', 'font:700 42px Georgia,serif;', C.theEnd, panel);
        const rest = h('div', 'min-height:120px;', null, panel);
        const add = (t, css = '') => { const e = h('div', `margin-top:6px;${css}`, ctx.brand.text(t), rest); ctx.anim(e, [{ opacity: 0 }, { opacity: 1 }], { duration: 300 }); };
        let gone = false;
        const leave = () => {
          if (gone) return;
          gone = true;
          panel.style.pointerEvents = 'none';
          const frames = ctx.gentle ? [{ opacity: 1 }, { opacity: 0 }] : [{ transform: 'none', opacity: 1 }, { transform: 'translateY(-100%)', opacity: 1 }];
          ctx.anim(panel, frames, { duration: 420, easing: 'ease-in', fill: 'forwards' }).onfinish = () => panel.remove();
          ctx.audio.play('assets/audio/sfx/rank_up.mp3', { bus: 'sfx', gain: 0.7 });
          ctx.toast(E.closing);
        };
        panel.addEventListener('click', leave);
        ctx.after(800, () => add(E.result));
        E.credits.forEach((c, i) => ctx.after(1500 + i * 500, () => add(c, 'font-weight:500;font-size:14px;opacity:.8;')));
        ctx.after(1500 + E.credits.length * 500 + 1000, leave);
      }
      ctx.every(47000, () => ctx.say(pick(ctx, E.credits)));
      return {};
    },
  },
};
