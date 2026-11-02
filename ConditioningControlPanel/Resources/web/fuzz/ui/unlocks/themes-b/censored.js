/* ui/unlocks/themes-b/censored.js - lane censored. Paper, black bars, a red stamp. The mechanic: earn more the less you see, and pay to
 * look. Bars never take a press, and every price is one held Peek away. */

import { THEMES } from '../../../data/content/themes.js';
import { FAKE_PAYWALL } from '../../../data/content/fakes.js';
import { ENDINGS } from '../../../data/content/endings.js';
import { mountPodcast } from '../podcast-player.js';
import { COPY } from './copy.js';
import { buttonCentre, docOf, endingStage, endingWords, fresh, gallery, pick, ring, rowsOf, sfx, spend } from './kit.js';

const R = THEMES.censored.rules;
const RED = '#c2261f';

export const censoredPick = {
  /* The pick: bars slam across the page, a red stamp lands, then they lift off what is left. */
  arrive(ctx) {
    const b = ctx.bounds(), n = 7, h = b.h / n;
    for (let i = 0; i < n; i++) {
      const bar = ctx.el('div', `position:absolute;left:0;right:0;top:${i * h}px;height:${h + 1}px;background:#000;pointer-events:none;transform-origin:${i % 2 ? '100%' : '0'} 50%;`, null, 'over');
      ctx.anim(bar, ctx.gentle ? [{ opacity: 0 }, { opacity: 1 }, { opacity: 0 }] : [{ transform: 'scaleX(0)' }, { transform: 'scaleX(1)', offset: 0.25 }, { transform: 'scaleX(1)', offset: 0.65 }, { transform: 'scaleX(0)' }], { duration: 1200, delay: i * 30, easing: 'ease-in-out', fill: 'both' });
      ctx.after(1500, () => bar.remove());
    }
    const stamp = ctx.el('div', `position:absolute;left:50%;top:38%;width:300px;margin-left:-150px;padding:6px 0;text-align:center;border:5px solid ${RED};color:${RED};font:800 34px "Courier New",monospace;transform:rotate(-8deg);pointer-events:none;`, COPY.censored.stamp, 'over');
    ctx.anim(stamp, ctx.gentle ? [{ opacity: 0 }, { opacity: 1 }, { opacity: 0 }] : [{ opacity: 0, transform: 'rotate(-8deg) scale(1.8)' }, { opacity: 1, transform: 'rotate(-8deg) scale(1)', offset: 0.2 }, { opacity: 1, transform: 'rotate(-8deg) scale(1)', offset: 0.75 }, { opacity: 0, transform: 'rotate(-8deg) scale(1)' }], { duration: 1500, delay: 200, fill: 'both' });
    ctx.after(450, () => sfx(ctx, 'snap'));
    ctx.after(1750, () => stamp.remove());
  },
};

/** Black bars across a tile. They grow in, and Peek fades them. */
function barTile(ctx) {
  const pats = [[[34, 16]], [[10, 12], [52, 14]], [[30, 26]], [[6, 12], [36, 10], [68, 10]], [[46, 18]]];
  let n = 0;
  return (t) => {
    for (const [top, size] of pats[n++ % pats.length]) {
      const bar = ctx.el('div', `position:absolute;left:0;right:0;top:${top}%;height:${size}%;background:#000;transform-origin:0 50%;`, null, t.el);
      bar.className = 'tb-bar-on';
      ctx.anim(bar, [{ transform: 'scaleX(0)' }, { transform: 'scaleX(1)' }], { duration: 300, delay: 200 + n * 120, fill: 'both' });
    }
  };
}

/** Bars and blur answer one body class, so Peek has a single switch. */
const PEEK_CSS = `body.tb-peek .tb-bar-on{opacity:.08;}
  .tb-bar-on{transition:opacity .25s ease-out;}
  body.tb-peek #count{background-size:0 100% !important;color:var(--ink) !important;}
  body.tb-peek .tb-word{background:transparent !important;color:inherit !important;}
  body.tb-peek .tb-tile img{filter:none !important;}`;

export default {
  ce_head: {
    mount(ctx) {
      ctx.css(PEEK_CSS);
      const g = gallery(ctx, { niche: 'censored', mode: 'swap', decorate: barTile(ctx) });
      if (ctx.has('ce_feed')) g.hide();
      ctx.on('buy', (e) => { if (e.item.id === 'ce_feed') g.hide(); });
      return {};
    },
  },

  /* The button is a bar. A tap shows a sliver of what is under it. */
  ce_bar: {
    mount(ctx) {
      ctx.css(`#btn[data-skin=censorbar]{width:230px;height:40px;background:#000;color:#000;border:0;border-radius:0;font:700 15px var(--font);transition:color .08s ease-out;}
        #btn[data-skin=censorbar].down{color:#fffdf6;transform:scale(.98);}
        body.gentle #btn[data-skin=censorbar].down{color:#000;}`);
      ctx.button.skin('censorbar');
      if (fresh(ctx)) ring(ctx, RED);
      return {};
    },
  },

  /* Every censored collection: columns of pictures behind bars. */
  ce_feed: { mount(ctx) { gallery(ctx, { niche: 'censored', mode: 'scroll', decorate: barTile(ctx) }); return {}; } },

  /* Blur breathes in and out, and it pays by how little you can see. */
  ce_blur: {
    mount(ctx) {
      const base = ctx.item.fps;
      let lvl = 0.5, t = 0, held = false;
      ctx.mod({ fps: (v) => v + base * 2 * (held ? 0 : lvl) });
      const box = ctx.el('div', 'display:flex;gap:8px;align-items:center;white-space:nowrap;font:12px var(--font);', null, 'meters');
      const label = ctx.el('span', '', pick(ctx, R.blur), box);
      const track = ctx.el('span', 'display:inline-block;flex:0 0 56px;height:6px;background:var(--fill);overflow:hidden;', null, box);
      const fill = ctx.el('span', 'display:block;height:100%;background:#000;transform-origin:0 50%;', null, track);
      const root = docOf(ctx).documentElement.style;
      ctx.css('.tb-tile img{filter:blur(var(--tb-blur,0));}#btn{filter:none;}');
      const edge = ctx.el('div', 'position:absolute;inset:0;background:radial-gradient(ellipse at 50% 32%,transparent 42%,rgba(239,236,228,.85) 92%);opacity:0;', null, 'wash');
      ctx.every(9000, () => { label.textContent = ctx.brand.text(pick(ctx, R.blur)); });
      ctx.on('peek', ({ on }) => { held = on; });
      const paint = () => {
        lvl = ctx.gentle ? 0.6 : (1 - Math.cos(t * Math.PI / 4)) / 2;
        root.setProperty('--tb-blur', `${(lvl * 7).toFixed(1)}px`);
        fill.style.transform = `scaleX(${lvl.toFixed(2)})`;
        edge.style.opacity = lvl.toFixed(2);
      };
      paint();
      ctx.every(250, () => { t += 0.25; paint(); });
      return { off() { root.removeProperty('--tb-blur'); } };
    },
  },

  /* The counter goes behind a bar. Prices still go dim when you are short, so you can always shop. */
  ce_nums: {
    mount(ctx) {
      ctx.css(`#count{display:inline-block;padding:0 6px;background:linear-gradient(#000,#000) no-repeat 0 50%/0 100%;transition:background-size .5s ease-out,color .5s step-end;}
        body.tb-hidecount #count{background-size:100% 100%;color:#000;}`);
      const body = docOf(ctx).body;
      ctx.after(30, () => body.classList.add('tb-hidecount'));
      ctx.every(16000, () => ctx.say(pick(ctx, R.hidden)));
      return { off() { body.classList.remove('tb-hidecount'); } };
    },
  },

  /* Hold to look. Every bar lifts, and the rate pays for it for a while afterwards. */
  ce_peek: {
    mount(ctx) {
      const doc = docOf(ctx), win = doc.defaultView, wrap = ctx.button.el.parentElement;
      const peek = ctx.el('button', `position:absolute;right:calc(100% + 8px);top:50%;width:70px;height:44px;margin-top:-22px;background:var(--panel);color:var(--ink);border:2px solid ${RED};font:700 14px var(--font);touch-action:none;pointer-events:auto;user-select:none;-webkit-user-select:none;`, COPY.censored.peek, wrap);
      peek.type = 'button';
      let held = false, cost = 0;
      ctx.mod({ fps: (v) => v * (1 - cost) });
      const set = (on) => {
        if (held === on) return;
        held = on;
        doc.body.classList.toggle('tb-peek', on);
        ctx.emit('peek', { on });
        if (!on) { const r = peek.getBoundingClientRect(); ctx.float(r.left + r.width / 2, r.top - 8, pick(ctx, R.peek), `color:${RED};font-weight:700;`); }
      };
      const down = (ev) => { ev.preventDefault?.(); set(true); };
      const up = () => set(false);
      peek.addEventListener('pointerdown', down);
      win.addEventListener('pointerup', up);
      win.addEventListener('pointercancel', up);
      win.addEventListener('blur', up);
      peek.addEventListener('contextmenu', (ev) => ev.preventDefault());
      ctx.on('pause', up);
      return {
        tick(dt) { cost = held ? Math.min(0.65, cost + dt * 0.4) : Math.max(0, cost - dt * 0.09); },
        off() {
          doc.body.classList.remove('tb-peek');
          peek.removeEventListener('pointerdown', down);
          win.removeEventListener('pointerup', up);
          win.removeEventListener('pointercancel', up);
          win.removeEventListener('blur', up);
        },
      };
    },
  },

  /* A paywall slides over the next goal. Pay and the rate jumps for a bit; say no and it goes away. Nothing is lost either way. */
  ce_wall: {
    mount(ctx) {
      const F = FAKE_PAYWALL;
      let open = null, boostUntil = 0, n = 0;
      const base = ctx.item.fps;
      ctx.mod({ fps: (v, s) => (s.clock < boostUntil ? v + base * 3 : v) });
      const leave = (card) => {
        open = null;
        ctx.anim(card, [{ transform: 'none', opacity: 1 }, { transform: 'translateY(30px)', opacity: 0 }], { duration: 220, fill: 'forwards' });
        ctx.after(240, () => card.remove());
      };
      const show = () => {
        if (open) return;
        const goal = docOf(ctx).querySelector('#goal .row');
        if (!goal || !goal.offsetParent) return;
        const r = goal.getBoundingClientRect();
        const price = Math.max(100, Math.round(ctx.rates().fps * 4));
        const fill = (s) => ctx.brand.text(s.replace('{price}', ctx.fmt(price)));
        const card = ctx.el('div', `position:absolute;left:${r.left}px;top:${Math.max(4, r.top - 2)}px;width:${r.width}px;box-sizing:border-box;padding:8px 10px;background:#fff;color:#141414;border-top:4px solid ${RED};box-shadow:0 2px 10px rgba(0,0,0,.35);font:13px/1.25 var(--font);pointer-events:auto;`, null, 'over');
        open = card;
        ctx.el('b', 'display:block;font-size:14px;', F.title, card);
        ctx.el('div', 'margin:2px 0 6px;', fill(F.bodies[n++ % F.bodies.length]), card);
        const row = ctx.el('div', 'display:flex;gap:8px;', null, card);
        const pay = ctx.el('button', `min-height:36px;padding:2px 12px;background:${RED};color:#fff;border:0;font:700 13px var(--font);touch-action:manipulation;`, fill(F.pay), row);
        const no = ctx.el('button', 'min-height:36px;padding:2px 12px;background:#efece4;color:#141414;border:1px solid #141414;font:13px var(--font);touch-action:manipulation;', F.decline, row);
        pay.type = no.type = 'button';
        pay.addEventListener('click', (ev) => {
          if (ctx.state.fuzz < price) { ctx.toast(fill(F.poor)); return; }
          spend(ctx, price);
          boostUntil = ctx.state.clock + 30000;
          ctx.float(ev.clientX, ev.clientY - 20, `-${ctx.fmt(price)}`, `color:${RED};font-weight:700;font-size:16px;`);
          ctx.toast(pick(ctx, F.paid));
          sfx(ctx, 'dling');
          leave(card);
        });
        no.addEventListener('click', () => { ctx.toast(pick(ctx, F.declined)); leave(card); });
        ctx.after(14000, () => { if (open === card) leave(card); });
        ctx.anim(card, [{ transform: 'translateY(30px)', opacity: 0 }, { transform: 'none', opacity: 1 }], 240);
      };
      ctx.after(1200, show);
      ctx.every(65000, show);
      return {};
    },
  },

  /* Words in the shop lose to black bars. Prices go too, on some rows. Peek reads them all. */
  ce_text: {
    mount(ctx) {
      ctx.css(`.tb-word{background:#000;color:#000;}.row.short .tb-word{background:#000;color:#000;}
        .row.tb-noprice .cost>span:first-child{background:#000;color:#000;padding:0 4px;}
        body.tb-peek .row.tb-noprice .cost>span:first-child{background:transparent;color:inherit;}`);
      const words = R.bars.map((w) => ctx.brand.text(w).toLowerCase());
      const doc = docOf(ctx);
      const mask = (el) => {
        const text = el.textContent;
        el.textContent = '';
        text.split(/(\s+)/).forEach((w, i) => {
          if (!w.trim()) { el.append(w); return; }
          const low = w.toLowerCase().replace(/[^a-z]/g, '');
          const hit = words.includes(low) || (w.length > 3 && (i * 7 + text.length) % 4 === 0);
          if (!hit) { el.append(w); return; }
          const s = doc.createElement('span');
          s.className = 'tb-word';
          s.textContent = w;
          el.appendChild(s);
        });
      };
      const scan = () => {
        for (const row of rowsOf(ctx)) {
          const small = row.querySelector('small');
          if (small && !small.dataset.tbx) { small.dataset.tbx = '1'; mask(small); }
          if (row.dataset.tbPriced === undefined) {
            row.dataset.tbPriced = '1';
            if (row.parentElement?.id === 'rows' && ctx.rng() < 0.3) row.classList.add('tb-noprice');
          }
        }
      };
      scan();
      ctx.every(600, scan);
      return {};
    },
  },

  ce_pod: { mount(ctx) { return mountPodcast(ctx, { theme: 'censored', show: '[REDACTED] FM' }); } },

  /* Most of the screen is black bars. Nothing under them takes less of a press, and the button stays clear. */
  ce_all: {
    mount(ctx) {
      const b = ctx.bounds();
      const bars = [];
      const place = () => {
        const c = buttonCentre(ctx), hole = { top: c.y - 62, bottom: c.y + 62 };
        const rowsTop = docOf(ctx).getElementById('shop')?.getBoundingClientRect().top ?? b.h * 0.5;
        const slots = [];
        for (let y = 0; y < rowsTop; y += 26 + ctx.rng() * 34) {
          const h = 12 + ctx.rng() * 18;
          if (y + h > hole.top && y < hole.bottom) continue;
          slots.push({ y, h, x: ctx.rng() * b.w * 0.25, w: b.w * (0.45 + ctx.rng() * 0.5) });
        }
        for (const side of [0, 1]) {
          const g = Math.max(0, (b.w - 760) / 2);
          if (g > 40) for (let y = rowsTop; y < b.h; y += 44 + ctx.rng() * 50) slots.push({ y, h: 16 + ctx.rng() * 24, x: side ? b.w - g : 0, w: g });
        }
        bars.splice(0).forEach((e) => e.remove());
        slots.slice(0, 16).forEach((s, i) => {
          const e = ctx.el('div', `position:absolute;left:${s.x}px;top:${s.y}px;width:${s.w}px;height:${s.h}px;background:#000;transform-origin:0 50%;pointer-events:none;`);
          e.className = 'tb-bar-on';
          ctx.anim(e, [{ transform: 'scaleX(0)' }, { transform: 'scaleX(1)' }], { duration: 260, delay: i * 50, fill: 'both' });
          bars.push(e);
        });
      };
      ctx.after(300, place);
      ctx.every(11000, place);
      ctx.every(19000, () => ctx.say(pick(ctx, R.all)));
      return {};
    },
  },

  /* One black bar, then a roll of credits with every name removed. */
  ce_end: {
    mount() { return {}; },
    ending(ctx) {
      const data = ENDINGS.ce_end;
      return endingStage(ctx, 'background:#efece4;color:#141414;--font:"Courier New",monospace;', 14000, (root) => {
        const b = ctx.bounds();
        const clip = ctx.el('div', 'position:absolute;left:0;right:0;top:0;bottom:34%;overflow:hidden;', null, root);
        const roll = ctx.el('div', 'position:absolute;left:0;right:0;top:0;text-align:center;font:600 18px/2 var(--font);', null, clip);
        const bar = ctx.el('div', `position:absolute;left:0;right:0;top:50%;height:${b.h}px;margin-top:${-b.h / 2}px;background:#000;`, null, root);
        ctx.anim(bar, [{ transform: 'scaleY(.02) scaleX(0)' }, { transform: 'scaleY(.02) scaleX(1)', offset: 0.3 }, { transform: 'scaleY(.02) scaleX(1)', offset: 0.6 }, { transform: 'scaleY(.06) scaleX(1)' }], { duration: 3000, fill: 'forwards' });
        data.credits.forEach((line) => ctx.el('div', '', line, roll));
        ctx.anim(roll, [{ transform: `translateY(${b.h * 0.66}px)` }, { transform: `translateY(${-roll.offsetHeight - 40}px)` }], { duration: 11000, delay: 2600, easing: 'linear', fill: 'both' });
        endingWords(ctx, root, data, `bottom:6%;background:#efece4;padding:8px;`);
        sfx(ctx, 'snap');
      });
    },
  },
};
