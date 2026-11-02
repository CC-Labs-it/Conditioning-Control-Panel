/* ui/unlocks/themes-b/infection.js - lane infection. A clean clinic, and a pink that keeps coming. The mechanic: spots spread, coverage
 * pays, and scratching one pays now and makes two more. Spots never sit on the button. */

import { THEMES } from '../../../data/content/themes.js';
import { FAKE_ANTIVIRUS } from '../../../data/content/fakes.js';
import { ENDINGS } from '../../../data/content/endings.js';
import { createSpiral } from '../../../fx/spiral.js';
import { mountPodcast } from '../podcast-player.js';
import { COPY } from './copy.js';
import { buttonCentre, docOf, endingStage, endingWords, fresh, gallery, gutter, moment, motion, pick, rowsOf, sfx, slot, wide } from './kit.js';

const R = THEMES.infection.rules;
const PINK = '#ff2f92';
const SPOT_BG = 'radial-gradient(circle at 40% 38%,#ff86c6,#ff2f92 62%,#e01f7e)';
const MAX_SPOTS = 26;
const SHARE_URL = 'https://cclabs.app/?spot=1';

export const infectionPick = {
  /* The pick: the page goes clinically clean, then one pink spot lands in it and rings. */
  arrive(ctx) {
    moment(ctx, 'background:#fff;', [{ opacity: 1 }, { opacity: 1, offset: 0.2 }, { opacity: 0 }], 800);
    const b = ctx.bounds();
    const r = ctx.el('div', `position:absolute;left:${b.w / 2 - 60}px;top:${b.h * 0.3 - 60}px;width:120px;height:120px;border-radius:50%;background:${SPOT_BG};pointer-events:none;`, null, 'over');
    ctx.anim(r, ctx.gentle ? [{ opacity: 0 }, { opacity: 1 }, { opacity: 0 }] : [{ transform: 'scale(0)' }, { transform: 'scale(1.2)', offset: 0.3 }, { transform: 'scale(1)', offset: 0.45 }, { transform: 'scale(.05)', offset: 1 }], { duration: 1200, delay: 150, fill: 'both' });
    ctx.after(1400, () => r.remove());
    ctx.after(300, () => sfx(ctx, 'chip_pop'));
  },
};

/* The spots live in in_head; everything else asks through events. */
function spotField(ctx) {
  const spots = new Set();
  const safe = (x, y, r) => {
    const c = buttonCentre(ctx);
    if (Math.abs(x - c.x) <= 120 + r && Math.abs(y - c.y) <= 60 + r) return false;
    const m = ctx.layer('meters').getBoundingClientRect();
    if (Math.abs(x - c.x) <= 170 + r && y - r < m.bottom) return false;
    const s = ctx.parts.shop?.getBoundingClientRect();
    return !s?.width || x + r < s.left || x - r > s.right || y + r < s.top;
  };
  const spawn = (x0, y0) => {
    if (spots.size >= (ctx.bounds().w < 760 ? 12 : MAX_SPOTS)) return;
    const b = ctx.bounds();
    const shop = ctx.parts.shop?.getBoundingClientRect();
    const floor = shop?.width && b.w < 760 ? shop.top : b.h;
    const r = 9 + ctx.rng() * 15;
    let x = x0, y = y0, tries = 0;
    do {
      if (x0 === undefined || tries) { x = r + ctx.rng() * (b.w - 2 * r); y = 40 + r + ctx.rng() * Math.max(10, floor - 40 - 2 * r); }
      tries += 1;
    } while (!safe(x, y, r) && tries < 24);
    if (!safe(x, y, r)) return;
    const e = ctx.el('div', `position:absolute;left:${x - r}px;top:${y - r}px;width:${2 * r}px;height:${2 * r}px;border-radius:50%;background:${SPOT_BG};pointer-events:auto;touch-action:manipulation;`);
    e.dataset.tbSpot = '1';
    spots.add(e);
    ctx.anim(e, ctx.gentle ? [{ opacity: 0 }, { opacity: 1 }] : [{ transform: 'scale(.1)' }, { transform: 'scale(1.15)' }, { transform: 'scale(1)' }], 300);
    e.addEventListener('pointerdown', (ev) => {
      ev.preventDefault?.();
      spots.delete(e);
      e.remove();
      const rates = ctx.rates();
      ctx.pay(rates.fps * 0.1 + rates.click, 'scratch', { x: ev.clientX, y: ev.clientY });
      sfx(ctx, 'chip_pop', 0.5);
      ctx.emit('in:scratch', {});
      ctx.after(500, () => { spawn(); spawn(); });
    });
  };
  ctx.on('in:spawn', ({ n = 1 } = {}) => { for (let i = 0; i < n; i++) spawn(); });
  return { spawn, spots };
}

export default {
  in_head: {
    mount(ctx) {
      const f = spotField(ctx);
      f.spawn();
      ctx.every(14000, () => { if (f.spots.size < 3) f.spawn(); });
      return {};
    },
  },

  /* A blob behind the page grows with coverage, and coverage pays. */
  in_spot: {
    mount(ctx) {
      if (ctx.store.get('start') === undefined) ctx.store.set('start', ctx.state.clock);
      let scratched = ctx.store.get('sc') || 0;
      const cover = () => Math.min(1, (ctx.state.clock - ctx.store.get('start')) / 720000 + scratched * 0.004);
      const base = ctx.item.fps;
      ctx.mod({ fps: (v) => v + base * 6 * cover() });
      const b = ctx.bounds(), size = Math.hypot(b.w, b.h) * 1.15;
      const blob = ctx.el('div', `position:absolute;left:${b.w * 0.6 - size / 2}px;top:${b.h * 0.35 - size / 2}px;width:${size}px;height:${size}px;border-radius:47% 53% 44% 56%;background:radial-gradient(circle,rgba(255,47,146,.5) 0 55%,rgba(255,47,146,.28) 80%,transparent 100%);transform-origin:50% 50%;`, null, 'wash');
      const line = ctx.el('div', 'font:12px var(--font);color:var(--ink);', '', 'meters');
      const draw = () => {
        const c = cover();
        blob.style.transform = `scale(${(0.02 + c * 0.98).toFixed(3)})`;
        const stage = R.stages[Math.min(R.stages.length - 1, Math.floor(c * R.stages.length))];
        line.textContent = COPY.infection.coverage(Math.round(c * 100), stage, ctx.fmt(base * 6 * c));
      };
      draw();
      ctx.every(1000, draw);
      ctx.on('in:scratch', () => { scratched += 1; ctx.store.set('sc', scratched); });
      ctx.every(16000, () => ctx.emit('in:spawn', { n: 1 }));
      return {};
    },
  },

  /* Spots hop between shop rows. Each landing leaves a mark on the row. */
  in_spread: {
    mount(ctx) {
      ctx.css('.tb-dot{position:absolute !important;width:13px;height:13px;border-radius:50%;background:' + SPOT_BG + ';pointer-events:none;}');
      const doc = docOf(ctx);
      ctx.every(3200, () => {
        const rows = rowsOf(ctx);
        if (rows.length < 2) return;
        const a = pick(ctx, rows), b = pick(ctx, rows);
        if (a === b) return;
        const ra = a.getBoundingClientRect(), rb = b.getBoundingClientRect();
        const ax = ra.right - 20, ay = ra.top + 10, bx = rb.right - 20 - ctx.rng() * 60, by = rb.top + 8 + ctx.rng() * 24;
        const d = ctx.el('div', `position:absolute;left:${ax}px;top:${ay}px;width:13px;height:13px;border-radius:50%;background:${SPOT_BG};pointer-events:none;`, null, 'over');
        const mx = (bx - ax) / 2, my = (by - ay) / 2 - 34;
        ctx.anim(d, [{ transform: 'translate(0,0)' }, { transform: `translate(${mx}px,${my}px) scale(1.3)` }, { transform: `translate(${bx - ax}px,${by - ay}px)` }], { duration: ctx.gentle ? 300 : 640, easing: 'linear', fill: 'forwards' });
        ctx.after(ctx.gentle ? 300 : 640, () => {
          d.remove();
          if (!b.isConnected || b.querySelectorAll('.tb-dot').length >= 4) return;
          const m = doc.createElement('span');
          m.className = 'tb-dot';
          m.style.right = `${6 + b.querySelectorAll('.tb-dot').length * 15}px`;
          m.style.top = '4px';
          b.appendChild(m);
          ctx.float(bx, by - 6, COPY.infection.scratch, `color:${PINK};font-size:12px;`);
        });
      });
      return {};
    },
  },

  /* The pictures catch it: a pink tint rises and the frames warp, slowly. */
  in_feed: {
    mount(ctx) {
      gallery(ctx, {
        niche: 'infection', mode: 'swap', decorate: (t) => {
          const tint = ctx.el('div', `position:absolute;inset:0;background:${PINK};opacity:0;`, null, t.el);
          const o = { duration: 3600, iterations: Infinity, direction: 'alternate', delay: -ctx.rng() * 3600 };
          ctx.anim(tint, [{ opacity: 0 }, { opacity: 0.6 }], o);
          motion(ctx, t.el.querySelector('img'), [{ transform: 'none', borderRadius: '2px' }, { transform: 'skewX(4deg) scaleY(1.08)', borderRadius: '34% 14% 28% 20%' }], o);
        },
      });
      return {};
    },
  },

  /* Some rows are sick: pressing one pays, and raises its price, up to a point. A buy still goes through when you can afford it. */
  in_tile: {
    mount(ctx) {
      ctx.css(`.row.tb-ill{background:#ffb6d9 !important;border-color:${PINK} !important;color:#3b0a24 !important;}.row.tb-ill small,.row.tb-ill i{color:#6b1f47 !important;}`);
      const extra = ctx.store.get('extra') || {};
      const ill = (id) => { let h = 7; for (const c of id) h = (h * 31 + c.charCodeAt(0)) % 997; return h % 3 === 0; };
      const free = (it) => it.kind === 'item' && it.lane !== 'trunk';
      ctx.mod({ price: (v, s, now, it) => (free(it) && ill(it.id) ? v * (1.15 + (extra[it.id] || 0) * 0.05) : v) });
      let gone = false;
      /* A press on a row you cannot afford yet: each sick row answers for itself. A row you can afford just buys. */
      const hit = (row, ev) => {
        if (gone || !row.classList.contains('short')) return;
        const id = row.dataset.id, n = extra[id] || 0;
        if (n >= 10) return;
        extra[id] = n + 1;
        ctx.store.set('extra', extra);
        ctx.pay(ctx.rates().click * 6, 'infected', { x: ev.clientX, y: ev.clientY });
        ctx.emit('in:spawn', { n: 1 });
      };
      const mark = () => {
        for (const row of rowsOf(ctx)) {
          if (row.dataset.tbIll !== undefined || row.parentElement?.id !== 'rows') continue;
          const on = ill(row.dataset.id);
          row.dataset.tbIll = on ? '1' : '0';
          row.classList.toggle('tb-ill', on);
          if (on) row.addEventListener('click', (ev) => hit(row, ev));
        }
      };
      mark();
      ctx.every(500, mark);
      return { off() { gone = true; } };
    },
  },

  /* Antivirus, Allegedly. It turns up, scans, loses, and makes things worse. Plainly a joke, and it always closes. */
  in_cure: {
    mount(ctx) {
      const F = FAKE_ANTIVIRUS;
      let open = null, press = 0;
      const shut = (card) => { if (open !== card) return; open = null; card.remove(); };
      const show = () => {
        if (open) return;
        const roomy = wide(ctx);
        const css = roomy ? `left:12px;top:150px;width:${Math.min(gutter(ctx) - 24, 260)}px;` : 'left:10px;right:10px;bottom:10px;';
        const card = ctx.el('div', `position:absolute;${css}box-sizing:border-box;padding:10px;background:#fff0f7;color:#3b0a24;border:3px dashed ${PINK};border-radius:14px;font:13px/1.25 var(--font);pointer-events:auto;box-shadow:0 4px 14px rgba(0,0,0,.25);`, null, 'over');
        open = card;
        ctx.el('b', 'display:block;font-size:15px;', F.title, card);
        ctx.el('small', 'display:block;opacity:.8;', F.tagline, card);
        const threats = ctx.el('div', 'font:700 15px var(--font);margin-top:6px;', F.threats[0], card);
        const scan = ctx.el('div', 'min-height:17px;', F.scanning[0], card);
        const track = ctx.el('div', 'height:8px;margin:4px 0 8px;background:#fff;border:1px solid #3b0a24;overflow:hidden;', null, card);
        const fill = ctx.el('div', `height:100%;background:${PINK};transform-origin:0 50%;`, null, track);
        ctx.anim(fill, [{ transform: 'scaleX(0)' }, { transform: 'scaleX(1)' }], { duration: 2800, iterations: Infinity, easing: 'linear' });
        const row = ctx.el('div', 'display:flex;flex-wrap:wrap;gap:6px;', null, card);
        const stepBtn = ctx.el('button', 'min-height:34px;padding:2px 10px;font:700 13px var(--font);background:#fff;color:#3b0a24;border:1px solid #3b0a24;touch-action:manipulation;', F.buttons[0], row);
        const giveUp = ctx.el('button', 'min-height:34px;padding:2px 10px;font:13px var(--font);background:#fff;color:#3b0a24;border:1px solid #3b0a24;touch-action:manipulation;', F.close, row);
        stepBtn.type = giveUp.type = 'button';
        let n = 0;
        stepBtn.addEventListener('click', (ev) => {
          n += 1; press += 1;
          stepBtn.textContent = F.buttons[Math.min(n, F.buttons.length - 1)];
          threats.textContent = F.threats[Math.min(n, F.threats.length - 1)];
          scan.textContent = pick(ctx, F.results);
          ctx.emit('in:spawn', { n: 1 + Math.min(n, 2) });
          ctx.pay(ctx.rates().fps * 2, 'antivirus', { x: ev.clientX, y: ev.clientY });
          sfx(ctx, 'ui_denied', 0.5);
          if (n >= F.buttons.length) { ctx.after(900, () => shut(card)); }
        });
        giveUp.addEventListener('click', () => shut(card));
        const steps = ctx.every(1400, () => { if (!n) scan.textContent = F.scanning[Math.floor(ctx.rng() * F.scanning.length)]; });
        ctx.after(15000, () => { steps(); shut(card); });
        ctx.anim(card, [{ opacity: 0, transform: 'translateY(12px)' }, { opacity: 1, transform: 'none' }], 200);
      };
      if (fresh(ctx)) ctx.after(600, show);
      ctx.every(75000, show);
      return {};
    },
  },

  /* The stage wobbles and the page warms. A thermometer keeps score. Gentle keeps the warmth and drops the wobble. */
  in_fever: {
    mount(ctx) {
      const warm = ctx.el('div', 'position:absolute;inset:0;background:#ff8a5c;opacity:.12;', null, 'wash');
      ctx.anim(warm, [{ opacity: 0.1 }, { opacity: 0.3 }], { duration: 3200, iterations: Infinity, direction: 'alternate' });
      // Added on top, so a Swap or a lean from another unlock still shows through the wobble.
      for (const el of [ctx.parts.buttonWrap, ctx.parts.shop, ctx.layer('meters')]) motion(ctx, el, [{ transform: 'rotate(-.7deg) skewX(.6deg)' }, { transform: 'rotate(.7deg) skewX(-.6deg)' }], { duration: 1500, iterations: Infinity, direction: 'alternate', easing: 'ease-in-out', composite: 'add' });
      const s = slot(ctx);
      const line = ctx.el(s.roomy ? 'div' : 'span', '', '', s.el);
      let i = 0;
      const draw = () => {
        const t = (37.4 + Math.min(3.4, ctx.state.clock / 600000)).toFixed(1);
        line.textContent = COPY.infection.fever(t, R.fever[i++ % R.fever.length]);
      };
      draw();
      ctx.every(8000, draw);
      return {};
    },
  },

  /* A real share press, by the player only. Off the shelf: a line, a button, a toast. */
  in_share: {
    mount(ctx) {
      const S = R.share, s = slot(ctx, 'pointer-events:auto;');
      const b = ctx.el('button', 'margin-right:6px;', S.button, s.el);
      b.className = 'tb-chip';
      b.type = 'button';
      ctx.el(s.roomy ? 'div' : 'span', 'font-size:12px;', S.title, s.el);
      if (!s.roomy) s.el.style.pointerEvents = 'auto';
      const nav = docOf(ctx).defaultView.navigator;
      const done = () => { ctx.toast(S.toast); ctx.pay(ctx.rates().fps * 5, 'share'); };
      b.addEventListener('click', () => ctx.modal({
        title: S.title, body: S.body,
        buttons: [{
          label: S.button,
          onPress: () => {
            const sent = nav.share ? nav.share({ url: SHARE_URL }) : nav.clipboard?.writeText(SHARE_URL);
            (sent || Promise.reject()).then(done, () => {});
          },
        }, { label: 'Close' }],
      }));
      return {};
    },
  },

  in_pod: { mount(ctx) { return mountPodcast(ctx, { theme: 'infection', show: 'Symptoms' }); } },

  /* Every couple of minutes one shop rule changes, for a while. Nothing here can stop a run. */
  in_mut: {
    mount(ctx) {
      const M = R.mutations, shop = ctx.parts.shop;
      let cur = null, n = ctx.store.get('n') || 0;
      ctx.css(`#shop.tb-m-flip #rows{transform:scaleY(-1);}#shop.tb-m-flip #rows .row{transform:scaleY(-1);animation:none;}
        #shop.tb-m-pink .row{background:#ff9fcd;}
        #shop.tb-m-noNames .row b{visibility:hidden;}
        #shop.tb-m-mystery{filter:hue-rotate(70deg);}`);
      ctx.mod({
        price: (v, s, now, it) => (it.kind !== 'item' ? v : cur === 'prices2' ? v * 2 : cur === 'pricesHalf' ? v / 2 : v),
        click: (v) => (cur === 'tapsDouble' ? v * 2 : v),
        fps: (v) => (cur === 'rateHalf' ? v / 2 : v),
      });
      const bannerBox = ctx.el('div', 'position:absolute;left:0;right:0;top:40%;text-align:center;pointer-events:none;', null, 'over');
      const banner = ctx.el('span', `display:inline-block;max-width:92%;padding:4px 14px;background:#fff0f7;color:${PINK};border:2px solid ${PINK};border-radius:6px;font:800 22px/1.15 var(--font);opacity:0;`, '', bannerBox);
      const clear = () => {
        if (cur) shop.classList.remove(`tb-m-${cur}`);
        if (cur === 'shuffle') for (const r of rowsOf(ctx)) r.style.order = '';
        cur = null;
      };
      const turn = () => {
        clear();
        const m = M[n++ % M.length];
        ctx.store.set('n', n);
        cur = m.key;
        shop.classList.add(`tb-m-${cur}`);
        if (cur === 'shuffle') for (const r of rowsOf(ctx)) r.style.order = String(Math.floor(ctx.rng() * 8));
        banner.textContent = ctx.brand.text(m.label);
        ctx.anim(banner, [{ opacity: 0 }, { opacity: 1, offset: 0.1 }, { opacity: 1, offset: 0.8 }, { opacity: 0 }], { duration: 3000, fill: 'forwards' });
        sfx(ctx, 'rank_settle', 0.6);
        ctx.after(45000, clear);
      };
      ctx.after(1000, turn);
      ctx.every(110000, turn);
      return { off() { clear(); } };
    },
  },

  /* All pink, a flatline, and the line turns into a spiral. */
  in_end: {
    mount() { return {}; },
    ending(ctx) {
      return endingStage(ctx, 'background:#ff5aa8;color:#fff;--font:"Segoe UI",system-ui,sans-serif;', 15000, (root) => {
        const b = ctx.bounds(), w = Math.min(560, b.w - 32), h = w * 0.5;
        let d = `M0 ${h / 2} h${w * 0.1} l${w * 0.02} ${-h * 0.14} l${w * 0.03} ${h * 0.3} l${w * 0.02} ${-h * 0.16} h${w * 0.1} l${w * 0.02} -${h * 0.1} l${w * 0.02} ${h * 0.1} h${w * 0.14}`;
        const cx = w * 0.7, cy = h / 2, turns = 6 * Math.PI;
        for (let a = 0; a <= turns; a += 0.2) { const r = (h * 0.44) * (1 - a / turns); d += ` L${(cx + r * Math.cos(Math.PI + a)).toFixed(1)} ${(cy + r * Math.sin(Math.PI + a)).toFixed(1)}`; }
        const box = ctx.el('div', `position:absolute;left:50%;top:14%;width:${w}px;height:${h}px;margin-left:${-w / 2}px;`, null, root);
        box.innerHTML = `<svg width="${w}" height="${h}" viewBox="0 0 ${w} ${h}"><path d="${d}" fill="none" stroke="#fff" stroke-width="3" stroke-linecap="round" stroke-linejoin="round" pathLength="1" stroke-dasharray="1" stroke-dashoffset="1"/></svg>`;
        const path = box.querySelector('path');
        ctx.anim(path, [{ strokeDashoffset: 1 }, { strokeDashoffset: 0 }], { duration: ctx.gentle ? 400 : 6000, easing: 'linear', fill: 'forwards' });
        ctx.after(6200, () => {
          const c = ctx.el('canvas', `position:absolute;left:50%;top:50%;width:90vmin;height:90vmin;transform:translate(-50%,-50%);opacity:0;mix-blend-mode:screen;`, null, root);
          root.insertBefore(c, box);
          try {
            const sp = createSpiral(c, { reduced: ctx.gentle, manual: true, ctx });
            sp.show();
            ctx.frame((dt) => sp.step(dt));
            ctx.anim(c, [{ opacity: 0 }, { opacity: 0.8 }], { duration: 1500, fill: 'forwards' });
          } catch (e) { c.remove(); }
        });
        endingWords(ctx, root, ENDINGS.in_end);
      });
    },
  },
};

/* One pink spot for a friend who came in through a share link. Decoration only, and it leaves when pressed. */
export function sharedSpot(ctx) {
  const search = docOf(ctx).defaultView.location?.search || '';
  if (!/[?&]spot=1\b/.test(search)) return;
  const c = ctx.bounds();
  const s = ctx.el('div', `position:absolute;left:${c.w * 0.74}px;top:${c.h * 0.16}px;width:26px;height:26px;border-radius:50%;background:${SPOT_BG};pointer-events:auto;`);
  s.addEventListener('pointerdown', () => s.remove());
}
