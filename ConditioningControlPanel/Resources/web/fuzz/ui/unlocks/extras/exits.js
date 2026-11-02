/* ui/unlocks/extras/exits.js - the two ways out that are not Drop. Go Outside leaves: the window shrinks away and it is
 * nice out. Endless Mode is the end that does not happen: The End shows up, gets pushed aside, and everything goes on. */

import { ENDINGS } from '../../../data/content/endings.js';
import { AFTER_COPY as C } from './copy.js';
import { FONT, h, pick } from './kit.js';

const EMOJI_FONT = '"Segoe UI Emoji","Apple Color Emoji","Noto Color Emoji",sans-serif';

/** The fade sits above `over`, so a show goes in the ending layer once it lands, and on the cards host until then. */
function showHost(ctx) {
  try { const l = ctx.layer('ending'); if (l) return l; } catch { /* not there yet */ }
  return document.getElementById('cards') || ctx.layer('over');
}

export const EXIT_UNLOCKS = {
  outside: {
    /* Nothing to draw during the run; the row is the door. */
    mount() { return {}; },
    /* The show between the fade and the credits. Any press skips it. */
    ending(ctx) {
      return new Promise((done) => {
        const E = ENDINGS.outside;
        const sky = ctx.el('div', `position:fixed;inset:0;z-index:91;overflow:hidden;background:linear-gradient(#bfe6ff,#e8f6ff 68%);color:#124;font:600 17px/1.3 ${FONT};pointer-events:auto;`, null, showHost(ctx));
        h('div', 'position:absolute;left:0;right:0;bottom:0;height:26%;background:#7cc66b;', null, sky);
        h('div', `position:absolute;right:10%;top:9%;font:64px/1 ${EMOJI_FONT};`, '☀️', sky);
        h('div', `position:absolute;left:10%;bottom:16%;font:120px/1 ${EMOJI_FONT};`, '🌳', sky);
        const win = h('div', `position:absolute;left:50%;top:50%;width:min(300px,80vw);height:190px;margin:-95px 0 0 calc(min(300px,80vw)/-2);background:#d4d0c8;border:2px outset #fff;color:#000;font:18px "Times New Roman",serif;text-align:center;box-sizing:border-box;padding-top:70px;`, ctx.brand.money(Math.floor(ctx.state.fuzz)), sky);
        const words = h('div', 'position:absolute;left:0;right:0;top:12%;padding:0 20px;text-align:center;', null, sky);
        const line = (text, css = '') => {
          const e = h('div', `margin-top:10px;${css}`, ctx.brand.text(text), words);
          ctx.anim(e, [{ opacity: 0 }, { opacity: 1 }], { duration: 500, easing: 'ease-out' });
        };
        let over = false;
        const finish = () => { if (over) return; over = true; done(); };
        sky.addEventListener('click', finish);
        const shrink = ctx.gentle
          ? [{ opacity: 1 }, { opacity: 0 }]
          : [{ transform: 'none', opacity: 1 }, { transform: 'scale(.05)', opacity: 0 }];
        ctx.after(700, () => { ctx.anim(win, shrink, { duration: 900, easing: 'ease-in', fill: 'forwards' }); });
        ctx.after(1700, () => line(C.bye, 'font-size:34px;'));
        ctx.after(2500, () => line(E.result));
        E.credits.forEach((c, i) => ctx.after(3600 + i * 900, () => line(c, 'margin-top:4px;font-weight:500;font-size:15px;')));
        ctx.after(3600 + E.credits.length * 900 + 300, () => line(E.closing));
        ctx.after(3600 + E.credits.length * 900 + 5000, finish);
      });
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
