/* ui/unlocks/themes-b/kit.js - what the four themes of this lane share: the page dress, the arrival moment, button words,
 * ticker lines, flavour on shop rows, picture tiles and galleries, the theme ad strip, cards, and the ending stage. */

import { THEMES } from '../../../data/content/themes.js';
import { CCP_THEME_ADS } from '../../../data/content/ads.js';

export const pick = (ctx, list) => list[Math.floor(ctx.rng() * list.length)];
export const docOf = (ctx) => ctx.button.el.ownerDocument;
export const rowsOf = (ctx) => [...docOf(ctx).querySelectorAll('#shop .row')];
export const sfx = (ctx, name, gain = 0.8) => ctx.audio.play(`assets/audio/sfx/${name}.mp3`, { bus: 'sfx', gain });
export const gutter = (ctx) => Math.max(0, (ctx.bounds().w - 760) / 2);
export const wide = (ctx) => ctx.bounds().w >= 1100;

const FONT_SANS = '"Barlow", "Segoe UI", system-ui, sans-serif';
const FONT_TYPE = '"Courier New", Courier, monospace';

export const DRESS = {
  goon: {
    hot: '#f2a33c', ready: '#3a2c1e',
    vars: { '--page': '#16110d', '--ink': '#f7e6cf', '--ink-soft': '#d9c4a8', '--ink-faint': '#a8947c', '--btn': '#2a1f15', '--panel': '#241c15', '--panel-short': '#1b1510', '--line': '#6b5238', '--fill': '#4d3b2a', '--stamp': '#f2a33c', '--font': FONT_SANS },
    css: '#btn{color:#f7e6cf;border:2px solid #f2a33c;border-radius:6px;box-shadow:0 0 18px rgba(242,163,60,.28);}#btn.down{border-style:solid;}#count{color:#f2a33c;}',
  },
  locked: {
    hot: '#e3b341', ready: '#34495e',
    vars: { '--page': '#1d2935', '--ink': '#e4ecf3', '--ink-soft': '#c3d0dc', '--ink-faint': '#93a6b8', '--btn': '#2c3c4c', '--panel': '#263544', '--panel-short': '#1f2c39', '--line': '#5a7086', '--fill': '#3a4a5a', '--stamp': '#e3b341', '--font': FONT_SANS },
    css: '#btn{color:#e4ecf3;border:2px solid #9fb3c6;border-radius:4px;}#btn.down{border-style:solid;}#count{color:#e3b341;}',
  },
  censored: {
    hot: '#c2261f', ready: '#fffdf6',
    vars: { '--page': '#efece4', '--ink': '#141414', '--ink-soft': '#3d3a34', '--ink-faint': '#6b675d', '--btn': '#fffdf6', '--panel': '#fffdf6', '--panel-short': '#e2ded3', '--line': '#141414', '--fill': '#c9c4b6', '--stamp': '#c2261f', '--font': FONT_TYPE },
    css: '#btn{color:#141414;border:2px solid #141414;border-radius:0;}#btn.down{border-style:solid;}',
  },
  infection: {
    hot: '#ff2f92', ready: '#f7c9e0',
    vars: { '--page': '#e6f1e8', '--ink': '#18342a', '--ink-soft': '#2f5245', '--ink-faint': '#567a6b', '--btn': '#f4faf5', '--panel': '#f4faf5', '--panel-short': '#c9d8cc', '--line': '#8fa797', '--fill': '#a9bdaf', '--stamp': '#ff2f92', '--font': FONT_SANS },
    css: '#btn{color:#18342a;border:2px solid #18342a;border-radius:12px;}#btn.down{border-style:solid;}',
  },
};

/** The whole page changes character. Called once on the pick; theme variables win over later look buys. */
export function dress(ctx, lane) {
  const d = DRESS[lane];
  ctx.vars(d.vars);
  ctx.css(`${d.css}
    body{transition:background-color .35s ease-out,color .35s ease-out;}
    #goal .row.ready{animation-name:tb-ready;}
    @keyframes tb-ready{50%{background:${d.ready};}}
    .tb-card{box-sizing:border-box;padding:8px 10px;background:var(--panel);color:var(--ink);border:1px solid var(--line);border-radius:4px;font:13px/1.25 var(--font);}
    .tb-chip{min-height:30px;padding:2px 12px;background:var(--panel);color:var(--ink);border:1px solid var(--line);border-radius:4px;font:600 13px var(--font);touch-action:manipulation;pointer-events:auto;}
    .tb-tile{position:absolute;overflow:hidden;background:rgba(127,127,127,.14);border:1px dashed var(--line);border-radius:3px;}
    .tb-tile img{position:absolute;inset:0;width:100%;height:100%;object-fit:cover;display:block;}
    .tb-credit{position:absolute;right:2px;bottom:1px;font:10px var(--font);color:#fff;text-shadow:0 0 2px #000;}
    @media (max-width:759px){.tb-wide{display:none;}}`);
  const root = docOf(ctx).documentElement.style;
  const assert_ = () => { for (const [k, v] of Object.entries(d.vars)) root.setProperty(k, v); };
  ctx.on('buy', assert_);
  ctx.after(0, assert_);
  const body = docOf(ctx).body;
  body.classList.add(`tb-${lane}`);
  return () => body.classList.remove(`tb-${lane}`);
}

/** True once per run per key: the big payoff plays on buy and never again on a reload. */
export function fresh(ctx, key = 'seen') {
  if (ctx.store.get(key)) return false;
  ctx.store.set(key, 1);
  return true;
}

/** A short full-page moment in the over layer: never takes a press, always removes itself. */
export function moment(ctx, css, frames, ms, text = null) {
  const el = ctx.el('div', `position:absolute;inset:0;pointer-events:none;${css}`, text, 'over');
  const f = ctx.gentle ? [{ opacity: 0 }, { opacity: 1 }, { opacity: 0 }] : frames;
  ctx.anim(el, f, { duration: ctx.gentle ? Math.max(ms, 600) : ms, easing: 'ease-out', fill: 'forwards' });
  ctx.after(Math.max(ms, 600) + 60, () => el.remove());
  return el;
}

/** Theme words on the button, only while nothing else has written on it. */
export function buttonWords(ctx, lane, ms = 7000) {
  const words = THEMES[lane].buttonWords, btn = ctx.button.el;
  const original = btn.textContent;
  let mine = original;
  ctx.every(ms, () => {
    if (btn.textContent !== mine && btn.textContent !== original) return;
    mine = pick(ctx, words);
    btn.textContent = mine;
  });
  return () => { if (btn.textContent === mine) btn.textContent = original; };
}

export function tickerLines(ctx, lane, ms = 24000) {
  const lines = THEMES[lane].ticker;
  ctx.after(1500, () => ctx.say(pick(ctx, lines)));
  ctx.every(ms, () => ctx.say(pick(ctx, lines)));
}

/** A shop row reads a theme line for a few seconds, then goes back. The name and price stay. */
export function shopFlavour(ctx, lane, ms = 9000) {
  const lines = THEMES[lane].shopLines;
  ctx.every(ms, () => {
    const rows = rowsOf(ctx).filter((r) => r.parentElement?.id === 'rows' && !r.dataset.tbFlavour);
    if (!rows.length) return;
    const row = pick(ctx, rows), small = row.querySelector('small');
    if (!small) return;
    const old = small.textContent;
    row.dataset.tbFlavour = '1';
    small.textContent = ctx.brand.text(pick(ctx, lines));
    delete small.dataset.tbx;
    ctx.after(5000, () => { delete row.dataset.tbFlavour; if (small.isConnected) { small.textContent = old; delete small.dataset.tbx; } });
  });
}

/* ---- pictures ---- */

/** A picture tile. Null from the source, or the No Pictures route, leaves an empty frame. */
export function tile(ctx, parent, css, niche) {
  const el = ctx.el('div', css, null, parent);
  el.className = 'tb-tile';
  const img = ctx.el('img', 'opacity:0;transition:opacity .4s ease-out;', null, el);
  img.alt = '';
  const credit = ctx.el('span', '', null, el);
  credit.className = 'tb-credit';
  const t = {
    el,
    async fill() {
      const r = ctx.pick('f3') === 'blind' ? null : await ctx.pictures.next({ niche });
      if (!el.isConnected) return;
      if (!r) { img.removeAttribute('src'); img.style.opacity = 0; credit.textContent = ''; return; }
      img.onload = () => { img.style.opacity = 1; };
      img.src = r.url;
      credit.textContent = r.credit || '';
    },
  };
  t.fill();
  return t;
}

/** Columns of tiles in the side gutters; on a phone, one dim strip behind the counter. mode swap or scroll. */
export function gallery(ctx, { niche, mode = 'swap', decorate = null, parent = 'back' }) {
  const b = ctx.bounds(), g = gutter(ctx), tiles = [];
  const roomy = g >= 110;
  const w = roomy ? Math.min(g - 24, 200) : 92, h = Math.round(w * (roomy ? 1.25 : 1.3));
  const gap = 10;
  const opacity = roomy ? 1 : 0.34;
  if (roomy) {
    const rows = Math.min(4, Math.ceil(b.h / (h + gap)) + (mode === 'scroll' ? 1 : 0));
    for (const side of [12, b.w - w - 12]) for (let i = 0; i < rows; i++) tiles.push({ x: side, y: 12 + i * (h + gap), span: rows * (h + gap) });
  } else {
    const cols = Math.max(3, Math.min(5, Math.ceil(b.w / (w + gap))));
    for (let i = 0; i < cols; i++) tiles.push({ x: 4 + i * (w + gap), y: 36, span: 0 });
  }
  const live = tiles.map((p) => {
    const t = tile(ctx, parent, `left:0;top:0;width:${w}px;height:${h}px;opacity:${opacity};transform:translate3d(${p.x}px,${p.y}px,0);`, niche);
    decorate?.(t, w, h);
    return { ...t, ...p };
  });
  const scroll = mode === 'scroll' && roomy && !ctx.gentle;
  if (scroll) {
    ctx.frame((dt) => {
      for (const o of live) {
        o.y -= 20 * dt;
        if (o.y < -h) { o.y += o.span; o.fill(); }
        o.el.style.transform = `translate3d(${o.x}px,${o.y}px,0)`;
      }
    });
  } else {
    let n = 0;
    ctx.every(mode === 'scroll' ? 3500 : 6000, () => { live[n++ % live.length].fill(); });
  }
  ctx.on('pictures', () => live.forEach((o) => o.fill()));
  return { tiles: live, hide() { for (const o of live) o.el.style.display = 'none'; } };
}

/* ---- theme ads ---- */

/** A slim strip in the ad slot with this theme's honest CCP cards. One payment per ad, as the config says. */
export function adStrip(ctx, lane) {
  const ads = (CCP_THEME_ADS[lane] || []).filter((a) => a.kind === 'banner');
  if (!ads.length) return;
  const a = ctx.el('a', 'display:flex;gap:8px;align-items:center;justify-content:center;min-height:30px;padding:3px 10px;margin:2px 0;background:var(--panel);color:var(--ink);border:1px solid var(--line);font:13px var(--font);text-decoration:none;pointer-events:auto;overflow:hidden;white-space:nowrap;', null, 'banner');
  a.rel = 'noopener';
  a.target = '_blank';
  const head = ctx.el('span', 'overflow:hidden;text-overflow:ellipsis;', null, a);
  const cta = ctx.el('b', 'flex:none;color:var(--stamp);', null, a);
  let i = 0, ad = null;
  const paid = new Set(ctx.store.get('paid') || []);
  const show = () => { ad = ads[i++ % ads.length]; a.href = ad.url; head.textContent = ctx.brand.text(ad.headline); cta.textContent = ctx.brand.text(ad.cta); };
  show();
  ctx.every(20000, show);
  a.addEventListener('click', (ev) => {
    if (!ad || paid.has(ad.id)) return;
    paid.add(ad.id);
    ctx.store.set('paid', [...paid]);
    ctx.pay(ctx.rates().fps * ctx.config.AD_CLICK_PAYS_SECONDS, 'ad', { x: ev.clientX, y: ev.clientY });
    ctx.emit('ad', { id: ad.id });
  });
}

/* ---- spending ---- */

/** The engine has no way to take currency off the counter yet. Until it does, a charge lands on the next buy: a price
 *  modifier adds what is owed, and the buy clears it. Forks and endings are never surcharged. */
const owed = new WeakMap();
export function spend(ctx, n) {
  if (ctx.spend) { ctx.spend(n); return; }
  let bill = owed.get(ctx);
  if (!bill) {
    bill = { n: 0 };
    owed.set(ctx, bill);
    ctx.mod({ price: (v, s, now, it) => (it.kind === 'item' ? v + bill.n : v) });
    ctx.on('buy', () => { bill.n = 0; });
  }
  bill.n += n;
}

/* ---- small surfaces ---- */

/** A card in a side rail when there is room, else one line in the meters. Answers the element to fill. */
export function slot(ctx, css = '') {
  if (wide(ctx)) {
    const el = ctx.el('div', css, null, ctx.layer('side'));
    el.className = 'tb-card';
    return { el, roomy: true };
  }
  return { el: ctx.el('div', 'font:13px/1.3 var(--font);max-width:100%;overflow:hidden;white-space:nowrap;text-overflow:ellipsis;', null, 'meters'), roomy: false };
}

/** A padded ring out of the button: the party for a good moment. */
export function ring(ctx, color) {
  const wrap = ctx.button.el.parentElement;
  const el = ctx.el('div', `position:absolute;left:50%;top:50%;width:150px;height:150px;margin:-75px 0 0 -75px;border:3px solid ${color};border-radius:50%;pointer-events:none;`, null, wrap);
  ctx.anim(el, ctx.gentle ? [{ opacity: 0.8 }, { opacity: 0 }] : [{ transform: 'scale(.4)', opacity: 1 }, { transform: 'scale(1.5)', opacity: 0 }], { duration: ctx.gentle ? 400 : 480, easing: 'ease-out', fill: 'forwards' });
  ctx.after(520, () => el.remove());
}

export function buttonCentre(ctx) {
  const r = ctx.button.el.getBoundingClientRect();
  return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
}

/** The ending stage: above the end fade, below the credits. A press after two seconds moves on; never held past ms. */
export function endingStage(ctx, css, ms, build) {
  return new Promise((done) => {
    const root = ctx.el('div', `position:absolute;inset:0;z-index:85;overflow:hidden;pointer-events:auto;${css}`, null, ctx.world);
    let can = false, over = false;
    const finish = () => { if (over) return; over = true; root.remove(); done(); };
    ctx.after(2000, () => { can = true; });
    ctx.after(ms, finish);
    root.addEventListener('click', (ev) => { if (ev.target.closest?.('a')) return; if (can) finish(); });
    build(root, finish);
  });
}

/** The ending's closing words and its one honest link. */
export function endingWords(ctx, root, data, css = '') {
  const box = ctx.el('div', `position:absolute;left:16px;right:16px;bottom:12%;text-align:center;font:18px/1.35 var(--font);${css}`, null, root);
  ctx.el('p', 'margin:0 0 10px;', data.result, box);
  if (data.link) {
    const a = ctx.el('a', 'display:inline-block;padding:8px 14px;color:inherit;border:1px solid currentColor;text-decoration:none;font-size:15px;', data.link.label, box);
    a.href = data.link.url;
    a.rel = 'noopener';
    a.target = '_blank';
  }
  ctx.anim(box, [{ opacity: 0 }, { opacity: 1 }], { duration: 900, fill: 'both', delay: 1200 });
  return box;
}
