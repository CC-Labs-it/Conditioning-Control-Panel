/* Slideshow: pictures that keep turning up, and you only have to look. The frames fill the page, and the last few rows ask
 * for your taste and then your time. Every picture comes through ctx.pictures: null draws an empty frame. */

import { FEED_LINES } from '../../../data/content/index.js';
import { panel, deck, burst, ring, onDark, claim, release } from './kit.js';

export const WOOD = 'box-sizing:border-box;border:4px solid #6b4a2b;background:#f3efe4;box-shadow:0 2px 5px rgba(0,0,0,.4);overflow:hidden;';

const NICHES = { base: 'pretty', second: 'close', third: 'soft', fav: 'favourite' };
const shared = { favs: 0 };

/** One image slot that crossfades. Answers null pictures with an empty frame, and shows a credit when there is one. */
export function pictureSlot(ctx, parent, css = '') {
  const box = ctx.el('div', `position:relative;width:100%;height:100%;overflow:hidden;${css}`, null, parent);
  const creditEl = ctx.el('div', 'position:absolute;right:2px;bottom:1px;font:10px Arial,Helvetica,sans-serif;color:#fff;text-shadow:0 1px 2px #000;z-index:2;', '', box);
  let top = null, ask = 0;
  return {
    box,
    async next(niche) {
      const mine = ++ask;
      const pic = await ctx.pictures.next({ niche });
      if (mine !== ask || !box.isConnected) return;
      const e = ctx.el('div', `position:absolute;inset:0;background:${pic ? `center/cover no-repeat url("${pic.url}")` : 'repeating-linear-gradient(45deg,rgba(0,0,0,.04) 0 6px,transparent 6px 12px)'};`, null, box);
      box.insertBefore(e, creditEl);
      creditEl.textContent = pic?.credit || '';
      ctx.anim(e, [{ opacity: 0 }, { opacity: 1 }], { duration: 450 });
      const old = top;
      top = e;
      if (old) ctx.after(470, () => old.remove());
    },
  };
}

function frame(ctx, { side = '', height = 190, niche, every = 3400, first = 0 }) {
  const el = panel(ctx, { side, height, css: `${WOOD}pointer-events:auto;` });
  const slot = pictureSlot(ctx, el);
  const pick = () => (shared.favs > 0 && ctx.rng() < Math.min(0.75, shared.favs * 0.25) ? NICHES.fav : niche);
  ctx.after(first, () => slot.next(pick()));
  ctx.every(every, () => slot.next(pick()));
  if (!ctx.gentle) ctx.anim(el, [{ transform: 'translateY(-16px) scale(.9)', opacity: 0 }, { transform: 'translateY(0) scale(1)', opacity: 1 }], { duration: 350, easing: 'cubic-bezier(.2,1.4,.5,1)' });
  ctx.on('pictures', () => slot.next(pick()));
  // A cornered button gets the free space back while it is out here.
  ctx.on('routes:corner', ({ on }) => { el.style.opacity = on ? '.12' : ''; el.style.pointerEvents = on ? 'none' : 'auto'; });
  return el;
}

export default {
  /* Picture Frame: one framed picture on the wall, changing by itself. */
  v_frame: { mount(ctx) { frame(ctx, { side: 'left', height: 230, niche: NICHES.base }); return {}; } },
  /* Wider Picture Mix: a second frame, from one more general category. */
  v_frame2: { mount(ctx) { frame(ctx, { side: 'right', height: 230, niche: NICHES.second, every: 4100, first: 300 }); return {}; } },
  /* Extra Frame: a third joins, smaller. */
  v_frame3: { mount(ctx) { frame(ctx, { side: 'left', height: 170, niche: NICHES.third, every: 4700, first: 600 }); return {}; } },

  /* Corner GIF: a short loop in a corner. Until real clips arrive, the picture itself pans in a loop. */
  v_gif: {
    mount(ctx) {
      const el = panel(ctx, { side: 'right', height: 150, css: `${WOOD}` });
      const slot = pictureSlot(ctx, el);
      const tag = ctx.el('div', 'position:absolute;left:3px;bottom:3px;font:700 10px Arial,Helvetica,sans-serif;background:rgba(0,0,0,.65);color:#fff;padding:1px 3px;z-index:3;', 'GIF', el);
      void tag;
      el.style.position = el.style.position || 'relative';
      slot.next('loop');
      ctx.every(9000, () => slot.next('loop'));
      if (!ctx.gentle) ctx.anim(slot.box, [{ transform: 'scale(1.12) translate(-3%,-2%)' }, { transform: 'scale(1.26) translate(3%,2%)' }], { duration: 1700, iterations: Infinity, direction: 'alternate', easing: 'ease-in-out' });
      return {};
    },
  },

  /* Wall of Frames: frames tile across the whole page behind the button, one by one, and keep turning over. */
  v_wall: {
    mount(ctx) {
      const { w, h } = ctx.bounds();
      const cell = Math.ceil(Math.sqrt((w * h) / 34)), cols = Math.ceil(w / cell), rows = Math.ceil(h / cell);
      const host = ctx.el('div', 'position:absolute;inset:0;', null, 'back');
      const cells = [];
      let n = 0;
      const add = () => {
        if (n >= cols * rows) return;
        const c = ctx.el('div', `position:absolute;left:${(n % cols) * cell}px;top:${Math.floor(n / cols) * cell}px;width:${cell - 3}px;height:${cell - 3}px;${WOOD}border-width:3px;`, null, host);
        if (!ctx.gentle) ctx.anim(c, [{ transform: 'scale(.3)', opacity: 0 }, { transform: 'scale(1)', opacity: 1 }], { duration: 220 });
        const slot = pictureSlot(ctx, c);
        slot.next(NICHES.base);
        cells.push(slot);
        n += 1;
      };
      const stop = ctx.every(190, () => { add(); if (n >= cols * rows) stop(); });
      ctx.every(1600, () => { if (cells.length) cells[Math.floor(ctx.rng() * cells.length)].next(ctx.rng() < 0.5 ? NICHES.base : NICHES.second); });
      ctx.css(`#counter{background:${onDark(ctx) ? 'rgba(20,16,30,.86)' : 'rgba(255,255,255,.88)'};padding:0 14px;border-radius:6px}#l-meters{background:${onDark(ctx) ? 'rgba(20,16,30,.8)' : 'rgba(255,255,255,.8)'};padding:0 8px;border-radius:4px}`);
      return {};
    },
  },

  /* Favourite Pictures: a heart on each frame. Press it and the frames start showing more like that. */
  v_fav: {
    mount(ctx) {
      const line = deck(FEED_LINES.fav, ctx);
      shared.favs = ctx.store.get('n') || 0;
      const hearts = [];
      const addTo = (host) => {
        if (host.querySelector('.routes-heart')) return;
        const px = host.clientHeight < 110 ? 30 : 44;
        const h = ctx.el('button', `position:absolute;left:3px;top:3px;width:${px}px;height:${px}px;padding:0;background:rgba(0,0,0,.35);border:0;border-radius:50%;color:#fff;font-size:${px / 2}px;line-height:${px}px;z-index:4;pointer-events:auto;touch-action:manipulation;`, '♥', host);
        h.type = 'button';
        h.className = 'routes-heart';
        h.addEventListener('pointerdown', (e) => {
          e.preventDefault();
          e.stopPropagation();
          h.style.color = '#ff3b7a';
          shared.favs += 1;
          ctx.store.set('n', shared.favs);
          const r = h.getBoundingClientRect(), x = r.left + r.width / 2, y = r.top + r.height / 2;
          burst(ctx, x, y, '#ff3b7a', 10, 60);
          ring(ctx, x, y, 90, '#ff3b7a', 500, 'over');
          ctx.pay(ctx.rates().fps * 2, 'fav', { x, y });
          ctx.toast(line());
          ctx.after(1500, () => { h.style.color = '#fff'; });
        });
        hearts.push(h);
      };
      const scan = () => {
        for (const layer of [ctx.layer('side', 'left'), ctx.layer('side', 'right'), ctx.layer('floaters')]) {
          for (const el of layer.children) if (el.style.borderColor === 'rgb(107, 74, 43)' || el.style.border.includes('rgb(107, 74, 43)')) addTo(el);
        }
      };
      scan();
      ctx.every(1500, scan);
      return { off() { shared.favs = 0; for (const h of hearts) h.remove(); } };
    },
  },

  /* Mandatory Video: a muted panel with no close button. Wake Up is the way out, and it also ends by itself. Staying to the end pays. */
  v_video: {
    mount(ctx) {
      let live = null;
      const finish = (paid) => {
        if (!live) return;
        const p = live; live = null;
        release('video', 'low');
        if (paid) {
          const r = p.getBoundingClientRect();
          ctx.pay(ctx.rates().fps * 12, 'video', { x: r.left + r.width / 2, y: r.top + r.height / 2 });
        }
        ctx.anim(p, [{ opacity: 1 }, { opacity: 0 }], { duration: 300, fill: 'both' });
        ctx.after(320, () => p.remove());
      };
      const play = () => {
        if (live) return;
        if (!claim(ctx, 'video', 15000, 'low')) return;
        const { w, h } = ctx.bounds(), pw = Math.min(340, w - 24), ph = Math.round(pw * 0.56);
        const p = ctx.el('div', `position:absolute;left:${(w - pw) / 2}px;top:${h - ph - 8}px;width:${pw}px;height:${ph}px;background:#111;border:2px solid #000;box-shadow:0 4px 18px rgba(0,0,0,.55);overflow:hidden;`, null, 'over');
        live = p;
        const slot = pictureSlot(ctx, p);
        slot.next('clip');
        if (!ctx.gentle) ctx.anim(slot.box, [{ transform: 'scale(1)' }, { transform: 'scale(1.18)' }], { duration: 14000, easing: 'linear', fill: 'forwards' });
        ctx.el('div', 'position:absolute;left:50%;top:50%;margin:-20px 0 0 -14px;width:0;height:0;border-left:30px solid rgba(255,255,255,.8);border-top:20px solid transparent;border-bottom:20px solid transparent;z-index:3;', null, p);
        const prog = ctx.el('div', 'position:absolute;left:0;right:0;bottom:0;height:5px;background:#ff69b4;transform-origin:0 50%;z-index:3;', null, p);
        ctx.anim(prog, [{ transform: 'scaleX(0)' }, { transform: 'scaleX(1)' }], { duration: 14000, easing: 'linear', fill: 'forwards' });
        const hole = ctx.el('button', 'position:absolute;right:4px;top:4px;width:40px;height:40px;background:transparent;border:2px dashed #fff;padding:0;pointer-events:auto;z-index:4;touch-action:manipulation;', null, p);
        hole.type = 'button';
        hole.setAttribute('aria-label', FEED_LINES.video);
        const note = ctx.el('div', 'position:absolute;right:48px;top:14px;font:700 13px Arial,Helvetica,sans-serif;color:#fff;opacity:0;z-index:4;text-shadow:0 1px 2px #000;', FEED_LINES.video, p);
        hole.addEventListener('pointerdown', (e) => { e.preventDefault(); ctx.anim(note, [{ opacity: 1 }, { opacity: 1, offset: 0.6 }, { opacity: 0 }], { duration: 1100 }); ctx.toast('Wake Up still works, top right.'); });
        ctx.after(14000, () => { if (live === p) finish(true); });
      };
      ctx.after(700, play);
      ctx.every(90000, play);
      ctx.on('pause', () => finish(false));
      return {};
    },
  },
};
