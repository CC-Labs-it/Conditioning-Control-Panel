/* ui/unlocks/themes-a/kit.js - what the four theme files share: page geometry, the head moment, picture frames, the
 * shop text rewriter and the small CCP chip. Nothing runs at import time. */

import { THEMES } from '../../../data/content/index.js';

/** One bag per run for what a theme's unlocks tell each other (ctx.store is per unlock). */
const bags = new WeakMap();
export function bag(ctx) {
  const k = ctx.state;
  if (!bags.has(k)) bags.set(k, {});
  return bags.get(k);
}

export const pick = (ctx, list) => list[Math.floor(ctx.rng() * list.length) % list.length];

/* ---- geometry ---- */
/** The counter has no ctx.parts entry yet (named in the hand-back); this is the one place that finds it. */
export const counterEl = (ctx) => ctx.parts.counter ?? ctx.parts.buttonWrap?.closest?.('#stage')?.querySelector?.('#counter') ?? null;

/** Page numbers, measured on purpose: call it from slow timers, never each frame. */
export function geo(ctx) {
  const { w, h } = ctx.bounds();
  const wrap = ctx.parts.buttonWrap?.getBoundingClientRect?.();
  const shop = ctx.parts.shop?.getBoundingClientRect?.();
  return {
    w, h,
    wide: w >= 1100,                                  // rails are wide enough for a panel
    cx: wrap ? wrap.left + wrap.width / 2 : w / 2,
    cy: wrap ? wrap.top + wrap.height / 2 : h / 3,
    bw: wrap?.width || 150,
    bh: wrap?.height || 54,
    shop,
    stageBottom: shop?.width ? shop.top : (wrap ? wrap.bottom + 24 : 220),
  };
}

/* ---- the head: page variables, button words, headlines, the big moment ---- */

/** A short-lived rule so the page crosses to its new colours instead of jumping. */
function crossfade(ctx) {
  const s = ctx.css('body,#world,.row,#btn,.toast,#counter{transition:background-color .6s,color .6s,border-color .6s,border-radius .6s;}');
  ctx.after(1400, () => s.remove());
}

/**
 * The theme switches on the moment it is picked: variables and css at once, the show only the first time, not on a
 * reload. Runs from the shared f5 handler, so its ctx.store keys stay prefixed.
 * spec: { theme, vars, css, moment(ctx), words }
 */
export function startTheme(ctx, spec) {
  const first = !ctx.store.get('taSeen');
  ctx.store.set('taSeen', 1);
  if (first) crossfade(ctx);
  ctx.vars(spec.vars);
  if (spec.css) ctx.css(spec.css);
  ctx.css(`#cards .card{background:var(--panel);color:var(--ink);border:1px solid var(--line);font-family:var(--font);}
#cards .card .card-sub,#cards .opt small{color:var(--ink-soft);}
#cards .card button{background:var(--panel-short);color:var(--ink);border:1px solid var(--line);}`);
  if (first && spec.moment) spec.moment(ctx);

  /* The button says the theme's words, one every few seconds. A skin that hides the label simply hides them. */
  const t = THEMES[spec.theme];
  const btn = ctx.button.el, plain = btn.textContent;
  let w = 0;
  const word = () => { btn.textContent = t.buttonWords[w++ % t.buttonWords.length]; };
  word();
  ctx.every(5200, word);

  /* Headlines and shop flavour cross the ticker. */
  const lines = [...t.ticker, ...t.shopLines];
  let l = Math.floor(ctx.rng() * lines.length);
  ctx.after(first ? 4000 : 9000, () => ctx.say(lines[l++ % lines.length]));
  ctx.every(24000, () => ctx.say(lines[l++ % lines.length]));
  return { off() { btn.textContent = plain; } };
}

/** A full-page sheet for the moment. It never takes the pointer. Removed by the caller's animation end. */
export function sheet(ctx, css, text) {
  const e = ctx.el('div', `position:absolute;inset:0;pointer-events:none;${css}`, text ?? null, 'over');
  return e;
}

export function done(a, e) {
  a.finished?.then(() => e.remove(), () => e.remove());
  return a;
}

/**
 * Gentle can change mid-run. Runs motion() or still(), each returning its own stop, and swaps when the switch moves.
 */
export function gentleSwitch(ctx, motion, still = () => () => {}) {
  let on = ctx.gentle, stop = (on ? still : motion)();
  ctx.every(500, () => {
    if (ctx.gentle === on) return;
    on = ctx.gentle;
    stop?.();
    stop = (on ? still : motion)();
  });
}

/* ---- pictures ---- */

/** A frame that holds one picture. Null from the source draws the empty frame. Refill with .next(). */
export function frame(ctx, css, niche, parent = 'back') {
  const f = ctx.el('div', `overflow:hidden;background:var(--panel-short);${css}`, null, parent);
  const img = ctx.el('img', 'display:block;width:100%;height:100%;object-fit:cover;opacity:0;', null, f);
  img.alt = '';
  img.decoding = 'async';
  const cred = ctx.el('span', 'position:absolute;left:4px;bottom:2px;font-size:10px;color:#fff;text-shadow:0 0 3px #000;', null, f);
  let ask = 0;
  const api = {
    el: f,
    async next(n = niche) {
      const mine = ++ask;
      const pic = await ctx.pictures.next({ niche: n });
      if (mine !== ask || !f.isConnected) return;
      if (!pic) { img.removeAttribute('src'); img.style.opacity = 0; cred.textContent = ''; return; }
      img.onload = () => { if (mine === ask) ctx.anim(img, [{ opacity: 0 }, { opacity: 1 }], { duration: 400, fill: 'forwards' }); };
      img.src = pic.url;
      cred.textContent = pic.credit || '';
    },
  };
  api.next();
  return api;
}

/** Refill a list of frames whenever the player changes the source. */
export function refillOnChange(ctx, frames) {
  ctx.on('pictures', () => { for (const f of frames) f.next(); });
}

/** Two to four frames in the gutters on a wide page, two beside the counter on a phone. */
export function sideFrames(ctx, niches, css = '') {
  ctx.css(`.ta-side{position:absolute;width:clamp(74px,calc((100vw - 800px)/2 - 24px),210px);aspect-ratio:3/4;${css}}
.ta-side.l{left:12px}.ta-side.r{right:12px}
.ta-side.b0{bottom:20px}.ta-side.b1{bottom:calc(20px + clamp(74px,calc((100vw - 800px)/2 - 24px),210px)*1.333 + 14px)}
@media(max-height:900px){.ta-side.b1{display:none}}
@media(max-width:1000px){.ta-side{bottom:auto !important;top:50px;width:70px;opacity:.5}.ta-side.b1{display:none}}`);
  const out = niches.map((n, i) => {
    const f = frame(ctx, '', n);
    f.el.className = `ta-side ${i % 2 ? 'r' : 'l'} b${Math.floor(i / 2)}`;
    return f;
  });
  refillOnChange(ctx, out);
  let k = 0;
  ctx.every(9000, () => { out[k++ % out.length].next(); });
  return out;
}

/* ---- the shop's words ---- */

/**
 * One observer per run. Each rewriter is (text, node) => text and folds in mount order over the row's ORIGINAL words,
 * so turning one off or moving a threshold just reapplies. Only row arrivals are watched, never the prices.
 */
export function shopWords(ctx, rewrite) {
  const b = bag(ctx);
  if (!b.words) {
    const orig = new WeakMap();
    const list = [];
    const applyRow = (row) => {
      for (const n of row.querySelectorAll('b,small')) {
        if (!orig.has(n)) orig.set(n, n.textContent);
        let t = orig.get(n);
        for (const r of list) t = r(t, n);
        if (n.textContent !== t) n.textContent = t;
      }
    };
    const boxes = ['#rows', '#goal'].map((sel) => ctx.parts.shop?.querySelector?.(sel)).filter(Boolean);
    const all = () => { for (const box of boxes) for (const row of box.children) applyRow(row); };
    const mo = new MutationObserver((recs) => { for (const r of recs) for (const a of r.addedNodes) if (a.nodeType === 1) applyRow(a); });
    for (const box of boxes) mo.observe(box, { childList: true });
    b.words = { list, all, mo };
  }
  const w = b.words;
  w.list.push(rewrite);
  w.all();
  return {
    refresh: w.all,
    off() {
      const i = w.list.indexOf(rewrite);
      if (i >= 0) w.list.splice(i, 1);
      w.all();
      if (!w.list.length) { w.mo.disconnect(); b.words = null; }
    },
  };
}

/** A small panel in the side rail, for a page wide enough to have one. */
export function railPanel(ctx, side, css = '') {
  return ctx.el('div', `pointer-events:auto;padding:8px;background:var(--panel);color:var(--ink);border:1px solid var(--line);font-size:14px;${css}`, null, ctx.layer('side', side));
}

/** A self-closing pop-up in `over` that is not a modal: the game keeps running behind it and it costs nothing to ignore. */
export function pop(ctx, css, ms, onGone) {
  const e = ctx.el('div', `position:absolute;left:50%;top:var(--ta-pop-top,16%);transform:translateX(-50%);pointer-events:auto;${css}`, null, 'over');
  const inA = ctx.anim(e, ctx.gentle ? [{ opacity: 0 }, { opacity: 1 }] : [{ opacity: 0, transform: 'translateX(-50%) scale(.6)' }, { opacity: 1, transform: 'translateX(-50%) scale(1)' }], { duration: ctx.gentle ? 300 : 240, easing: 'cubic-bezier(.2,1.4,.5,1)' });
  let open = true;
  const stop = ctx.after(ms, () => close());
  function close(fast) {
    if (!open) return;
    open = false;
    stop();
    inA.cancel?.();
    done(ctx.anim(e, [{ opacity: 1 }, { opacity: 0 }], { duration: fast ? 180 : 300, fill: 'forwards' }), e);
    onGone?.();
  }
  return { el: e, close, get open() { return open; } };
}
