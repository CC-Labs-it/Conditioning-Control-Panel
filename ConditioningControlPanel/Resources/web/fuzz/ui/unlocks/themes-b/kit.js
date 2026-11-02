/* ui/unlocks/themes-b/kit.js - what the four themes of this lane share: the page dress, the arrival moment, button words,
 * ticker lines, flavour on shop rows, picture tiles and galleries, cards, and the ending stage. */

import { THEMES } from '../../../data/content/themes.js';
import { THEME_PAGES } from '../../palette.js';

export const pick = (ctx, list) => list[Math.floor(ctx.rng() * list.length)];
export const docOf = (ctx) => ctx.button.el.ownerDocument;
export const rowsOf = (ctx) => [...ctx.parts.shop.querySelectorAll('.row')];
export const sfx = (ctx, name, gain = 0.8) => ctx.audio.play(`assets/audio/sfx/${name}.mp3`, { bus: 'sfx', gain });
export const gutter = (ctx) => Math.max(0, (ctx.bounds().w - 760) / 2);
export const wide = (ctx) => ctx.bounds().w >= 1100;

const FONT_SANS = '"Barlow", "Segoe UI", system-ui, sans-serif';
const FONT_TYPE = '"Courier New", Courier, monospace';

export const DRESS = {
  goon: {
    hot: 'var(--niche)',
    vars: { ...THEME_PAGES.goon, '--font': FONT_SANS },
    css: '#btn{border-radius:6px;box-shadow:0 0 18px color-mix(in srgb,var(--mix) 35%,transparent);}#btn.down{border-style:solid;}',
  },
  locked: {
    hot: 'var(--niche)',
    vars: { ...THEME_PAGES.locked, '--font': FONT_SANS },
    css: '#btn{border-radius:4px;}#btn.down{border-style:solid;}',
  },
  censored: {
    hot: 'var(--niche)',
    vars: { ...THEME_PAGES.censored, '--font': FONT_TYPE },
    css: '#btn{border-color:#141414;border-radius:0;}#btn.down{border-style:solid;}',
  },
  infection: {
    hot: 'var(--niche)',
    vars: { ...THEME_PAGES.infection, '--font': FONT_SANS },
    css: '#btn{border-radius:12px;}#btn.down{border-style:solid;}',
  },
};

/** The whole page changes character. Called once on the pick; theme variables win over later look buys. */
export function dress(ctx, lane) {
  const d = DRESS[lane];
  const vars = d.vars;
  ctx.vars(vars);
  ctx.css(`${d.css}
    body{transition:background-color .35s ease-out,color .35s ease-out;}
    .tb-card{box-sizing:border-box;padding:8px 10px;background:var(--panel);color:var(--ink);border:1px solid var(--line);border-radius:4px;font:13px/1.25 var(--font);}
    .tb-chip{min-height:30px;padding:2px 12px;background:var(--panel);color:var(--ink);border:1px solid var(--line);border-radius:4px;font:600 13px var(--font);touch-action:manipulation;pointer-events:auto;}
    .tb-tile{position:absolute;overflow:hidden;background:rgba(127,127,127,.14);border:1px dashed var(--line);border-radius:3px;}
    .tb-tile img{position:absolute;inset:0;width:100%;height:100%;object-fit:cover;display:block;}
    .tb-credit{position:absolute;right:2px;bottom:1px;font:10px var(--font);color:#fff;text-shadow:0 0 2px #000;}
    @media (max-width:759px){.tb-wide{display:none;}}`);
  const root = docOf(ctx).documentElement.style;
  /* The theme holds its page against later look buys, until the player has pressed a page colour of their own. */
  const assert_ = () => { if (ctx.has('paint')) return; for (const [k, v] of Object.entries(vars)) root.setProperty(k, v); };
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

/** A looping motion that stops the moment Gentle goes on and comes back when it goes off. */
export function motion(ctx, el, frames, opts) {
  let a = null;
  const sync = () => {
    if (ctx.gentle) { if (a) { a.cancel(); a = null; } } else if (!a) a = ctx.anim(el, frames, opts);
  };
  sync();
  ctx.every(500, sync);
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
  if (mode === 'scroll' && roomy) {
    ctx.frame((dt) => {
      if (ctx.gentle) return;
      for (const o of live) {
        o.y -= 20 * dt;
        if (o.y < -h) { o.y += o.span; o.fill(); }
        o.el.style.transform = `translate3d(${o.x}px,${o.y}px,0)`;
      }
    });
  }
  let n = 0;
  ctx.every(mode === 'scroll' ? 3500 : 6000, () => { if (mode !== 'scroll' || !roomy || ctx.gentle) live[n++ % live.length].fill(); });
  ctx.on('pictures', () => live.forEach((o) => o.fill()));
  return { tiles: live, hide() { for (const o of live) o.el.style.display = 'none'; } };
}

/* ---- small surfaces ---- */

/** A card in the dock. Answers the element to fill. */
export function slot(ctx, css = '') {
  const el = ctx.el('div', css, null, ctx.dock());
  el.className = 'tb-card';
  return { el, roomy: true };
}

/** A padded ring out of the button: the party for a good moment. */
export function ring(ctx, color) {
  const wrap = ctx.parts.buttonWrap;
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
    const root = ctx.el('div', `position:absolute;inset:0;overflow:hidden;pointer-events:auto;${css}`, null, 'ending');
    let can = false, over = false;
    const finish = () => { if (over) return; over = true; root.remove(); done(); };
    ctx.after(2000, () => { can = true; });
    ctx.after(ms, finish);
    root.addEventListener('click', (ev) => { if (ev.target.closest?.('button')) return; if (can) finish(); });
    build(root, finish);
  });
}

/** The ending's closing words and its one honest link. */
export function endingWords(ctx, root, data, css = '') {
  const box = ctx.el('div', `position:absolute;left:16px;right:16px;bottom:12%;text-align:center;font:18px/1.35 var(--font);${css}`, null, root);
  ctx.el('p', 'margin:0 0 10px;', data.result, box);
  if (data.link) {
    const go = ctx.el('button', 'display:inline-block;min-height:40px;padding:8px 14px;background:transparent;color:inherit;border:1px solid currentColor;font:15px var(--font);touch-action:manipulation;', data.link.label, box);
    go.type = 'button';
    go.addEventListener('click', () => ctx.openLink(data.link.url));
  }
  ctx.anim(box, [{ opacity: 0 }, { opacity: 1 }], { duration: 900, fill: 'both', delay: 1200 });
  return box;
}
