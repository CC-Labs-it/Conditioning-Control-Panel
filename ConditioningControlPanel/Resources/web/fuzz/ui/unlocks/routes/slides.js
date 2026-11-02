/* Slideshow: pictures that keep turning up, and you only have to look. Every frame is a screen (ui/screens.js): it pays, and
 * it goes where you drag it. The last few rows ask for your taste and then your time. Every picture comes through
 * ctx.pictures: with nothing chosen a screen shows its test card and still pays. */

import { FEED_LINES } from '../../../data/content/index.js';
import { deck, burst, ring, onDark, claim, release } from './kit.js';
import { ROUTE_COPY } from './copy.js';
import { screen, liveScreens, themeLists, themeNiche } from '../../screens.js';

const NICHES = { base: 'pretty', second: 'close', third: 'soft', fav: 'favourite' };
const shared = { favs: 0 };

/** One background tile that crossfades, for the Wall: too many to be real screens. Null draws a hatch in the theme's colour. */
function wallSlot(ctx, parent) {
  const box = ctx.el('div', 'position:relative;width:100%;height:100%;overflow:hidden;', null, parent);
  const creditEl = ctx.el('div', 'position:absolute;right:2px;bottom:1px;font:10px Arial,Helvetica,sans-serif;color:#fff;text-shadow:0 1px 2px #000;z-index:2;', '', box);
  let top = null, ask = 0;
  return {
    async next(niche) {
      const mine = ++ask;
      const pic = await ctx.pictures.next({ niche });
      if (mine !== ask || !box.isConnected) return;
      const e = ctx.el('div', `position:absolute;inset:0;background:${pic ? `center/cover no-repeat url("${pic.url}")` : 'repeating-linear-gradient(45deg,var(--niche-soft) 0 6px,var(--niche) 6px 8px,var(--niche-soft) 8px 12px)'};`, null, box);
      box.insertBefore(e, creditEl);
      creditEl.textContent = pic?.credit || '';
      ctx.anim(e, [{ opacity: 0 }, { opacity: 1 }], { duration: 450 });
      const old = top;
      top = e;
      if (old) ctx.after(470, () => old.remove());
    },
  };
}

const pickFav = (ctx, niche) => () => (shared.favs > 0 && ctx.rng() < Math.min(0.75, shared.favs * 0.25) ? NICHES.fav : niche);

function frame(ctx, { niche, every = 3400, first = 0, ratio = 1.15, scale = 1, hint = false }) {
  return screen(ctx, { kind: 'picture', ratio, scale, every, first, hint, niche: pickFav(ctx, niche) });
}

export default {
  /* Picture Frame: one framed picture, changing by itself. It pays, and you can put it anywhere. */
  v_frame: { mount(ctx) { frame(ctx, { niche: NICHES.base, hint: !ctx.store.get('spot') }); return {}; } },
  /* Wider Picture Mix: a second frame, from one more general category. */
  v_frame2: { mount(ctx) { frame(ctx, { niche: NICHES.second, every: 4100, first: 300 }); return {}; } },
  /* Extra Frame: a third joins, smaller. */
  v_frame3: { mount(ctx) { frame(ctx, { niche: NICHES.third, every: 4700, first: 600, ratio: 1, scale: 0.8 }); return {}; } },

  /* Corner GIF: a real gif, looping. With none to show, the test card asks for one. */
  v_gif: {
    mount(ctx) {
      const s = screen(ctx, { kind: 'gif', ratio: 0.72, scale: 0.8, every: 9000, niche: 'loop' });
      ctx.el('div', 'position:absolute;right:3px;top:3px;font:700 10px Arial,Helvetica,sans-serif;background:rgba(0,0,0,.65);color:#fff;padding:1px 3px;z-index:3;pointer-events:none;', ROUTE_COPY.gif, s.body);
      return {};
    },
  },

  /* Wall of Frames: frames tile across the whole page behind everything, one by one, and keep turning over. Not draggable:
   * it is the wallpaper the screens hang on. */
  v_wall: {
    mount(ctx) {
      const { w, h } = ctx.bounds();
      const cell = Math.ceil(Math.sqrt((w * h) / 34)), cols = Math.ceil(w / cell), rows = Math.ceil(h / cell);
      const host = ctx.el('div', `position:absolute;inset:0;${onDark(ctx) ? 'opacity:.7;' : ''}`, null, 'back');
      const cells = [];
      let n = 0;
      const niche = (base) => themeNiche(themeLists(ctx), base, ctx.rng);
      const add = () => {
        if (n >= cols * rows) return;
        const c = ctx.el('div', `position:absolute;left:${(n % cols) * cell}px;top:${Math.floor(n / cols) * cell}px;width:${cell - 3}px;height:${cell - 3}px;box-sizing:border-box;border:3px solid var(--niche);background:var(--niche-soft);box-shadow:0 2px 5px rgba(0,0,0,.4);overflow:hidden;`, null, host);
        if (!ctx.gentle) ctx.anim(c, [{ transform: 'scale(.3)', opacity: 0 }, { transform: 'scale(1)', opacity: 1 }], { duration: 220 });
        const slot = wallSlot(ctx, c);
        slot.next(niche(NICHES.base));
        cells.push(slot);
        n += 1;
      };
      const stop = ctx.every(190, () => { add(); if (n >= cols * rows) stop(); });
      ctx.every(1600, () => { if (cells.length) cells[Math.floor(ctx.rng() * cells.length)].next(niche(ctx.rng() < 0.5 ? NICHES.base : NICHES.second)); });
      ctx.on('pictures', () => { for (const c of cells) c.next(niche(NICHES.base)); });
      /* A plate in the page's own colour, so the page's ink always reads on it, whatever the theme did to both. */
      ctx.css('#counter{background:color-mix(in srgb,var(--page) 88%,transparent);padding:0 14px;border-radius:6px}#l-meters{background:color-mix(in srgb,var(--page) 80%,transparent);padding:0 8px;border-radius:4px}#more{background:color-mix(in srgb,var(--page) 80%,transparent);width:max-content;margin-left:auto;margin-right:auto;padding:0 8px;border-radius:4px}');
      return {};
    },
  },

  /* Favourite Pictures: a heart on each frame. Press it and the frames start showing more like that. */
  v_fav: {
    mount(ctx) {
      const line = deck(FEED_LINES.fav, ctx);
      shared.favs = ctx.store.get('n') || 0;
      const hearts = [];
      const addTo = (s) => {
        const host = s.body;
        if (!s.slot || host.querySelector('.routes-heart')) return;
        const px = host.clientHeight < 110 ? 30 : 44;
        const h = ctx.el('button', `position:absolute;left:3px;top:3px;width:${px}px;height:${px}px;padding:0;background:rgba(0,0,0,.35);border:0;border-radius:50%;color:#fff;font-size:${px / 2}px;line-height:${px}px;z-index:4;pointer-events:auto;touch-action:manipulation;`, '♥', host);
        h.type = 'button';
        h.className = 'routes-heart';
        h.addEventListener('pointerdown', (e) => {
          e.preventDefault();
          e.stopPropagation();
          if (h.dataset.cool) return;
          h.dataset.cool = '1';
          h.style.color = '#ff3b7a';
          shared.favs += 1;
          ctx.store.set('n', shared.favs);
          const r = h.getBoundingClientRect(), x = r.left + r.width / 2, y = r.top + r.height / 2;
          burst(ctx, x, y, '#ff3b7a', 10, 60);
          ring(ctx, x, y, 90, '#ff3b7a', 500, 'over');
          ctx.pay(ctx.rates().fps * 2, 'fav', { x, y });
          ctx.toast(line());
          ctx.after(1500, () => { h.style.color = '#fff'; delete h.dataset.cool; });
        });
        hearts.push(h);
      };
      const scan = () => { for (const s of liveScreens()) addTo(s); };
      scan();
      ctx.every(1500, scan);
      return { off() { shared.favs = 0; for (const h of hearts) h.remove(); } };
    },
  },

  /* Mandatory Video: a muted screen with no close button. It drags like the others, and it does not leave. Wake Up is the way
   * out, and it also ends by itself. Staying to the end pays. The row's own per-second pay runs all the time. */
  v_video: {
    mount(ctx) {
      const s = screen(ctx, { kind: 'clip', ratio: 0.56, wide: true, first: -1, niche: 'clip', css: 'background:#111;' });
      s.show(false);
      const prog = ctx.el('div', 'position:absolute;left:0;right:0;bottom:0;height:5px;background:var(--niche);transform-origin:0 50%;transform:scaleX(0);z-index:3;pointer-events:none;', null, s.body);
      const hole = ctx.el('button', 'position:absolute;right:4px;top:4px;width:40px;height:40px;background:transparent;border:2px dashed #fff;padding:0;pointer-events:auto;z-index:4;touch-action:manipulation;', null, s.body);
      hole.type = 'button';
      hole.setAttribute('aria-label', FEED_LINES.video);
      const note = ctx.el('div', 'position:absolute;right:48px;top:14px;font:700 13px Arial,Helvetica,sans-serif;color:#fff;opacity:0;z-index:4;text-shadow:0 1px 2px #000;pointer-events:none;', FEED_LINES.video, s.body);
      hole.addEventListener('pointerdown', (e) => {
        e.preventDefault();
        e.stopPropagation();
        ctx.anim(note, [{ opacity: 1 }, { opacity: 1, offset: 0.6 }, { opacity: 0 }], { duration: 1100 });
        ctx.toast(ROUTE_COPY.wakeStill);
      });
      let on = false, run = null;
      const finish = (paid) => {
        if (!on) return;
        on = false;
        release('video', 'low');
        run?.cancel();
        if (paid) {
          const f = s.frame();
          ctx.pay(ctx.rates().fps * 12, 'video', { x: f.x + f.w / 2, y: f.y + f.h / 2 });
        }
        s.slot.clear();
        s.show(false);   // Wake Up: timers and animations are frozen now, so it goes at once
      };
      const play = () => {
        if (on || !claim(ctx, 'video', 15000, 'low')) return;
        on = true;
        s.show(true);
        s.next();
        run = ctx.anim(prog, [{ transform: 'scaleX(0)' }, { transform: 'scaleX(1)' }], { duration: 14000, easing: 'linear', fill: 'forwards' });
        if (!ctx.gentle) ctx.anim(s.inner, [{ opacity: 0 }, { opacity: 1 }], { duration: 300 });
        ctx.after(14000, () => { if (on) finish(true); });
      };
      if (!ctx.restored) ctx.after(700, play);
      ctx.every(90000, play);
      ctx.on('pause', () => finish(false));
      ctx.on('suspend', ({ on: stop }) => { if (stop) finish(false); });
      return { off() { release('video', 'low'); } };
    },
  },
};
