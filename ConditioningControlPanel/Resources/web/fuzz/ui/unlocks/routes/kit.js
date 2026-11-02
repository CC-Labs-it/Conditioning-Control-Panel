/* ui/unlocks/routes/kit.js - helpers shared by the choice routes. Nothing touches the DOM at import time.
 * panel() is the one place that decides where a route's furniture sits: in a side rail when there is room for one, and
 * in the free space beside the button when there is not (phones, narrow windows). */

import { createSpiral } from '../../../fx/spiral.js';

export const docOf = (ctx) => ctx.layer('over').ownerDocument;
export const fill = (s, map) => String(s).replace(/\{(\w+)\}/g, (m, k) => (k in map ? map[k] : m));
export const nowOf = (ctx) => ctx.state.clock;

/** Draws without repeating until the list runs out. */
export function deck(list, ctx) {
  let bag = [];
  return () => {
    if (!bag.length) { bag = [...list]; for (let i = bag.length - 1; i > 0; i--) { const j = Math.floor(ctx.rng() * (i + 1)); [bag[i], bag[j]] = [bag[j], bag[i]]; } }
    return bag.pop();
  };
}

export const rectOf = (el) => el.getBoundingClientRect();
export function btnCentre(ctx) { const r = rectOf(ctx.button.el); return { x: r.left + r.width / 2, y: r.top + r.height / 2, w: r.width, h: r.height, top: r.top, bottom: r.bottom, left: r.left, right: r.right }; }
/** The zone the button sits in: where the free space beside it ends. */
export function zoneRect(ctx) { return rectOf(ctx.parts.buttonWrap.parentElement); }
export function counterRect(ctx) { return rectOf(ctx.parts.counter); }

/** A ring that opens and fades. Fades only, so it is fine under Gentle. */
export function ring(ctx, x, y, size, color = '#7b5cff', ms = 800, parent = 'floaters') {
  const e = ctx.el('div', `position:absolute;left:${x - size / 2}px;top:${y - size / 2}px;width:${size}px;height:${size}px;border:2px solid ${color};border-radius:50%;`, null, parent);
  ctx.anim(e, ctx.gentle ? [{ opacity: 0.6 }, { opacity: 0 }] : [{ transform: 'scale(.3)', opacity: 0.9 }, { transform: 'scale(1)', opacity: 0 }], { duration: Math.max(ms, 300), easing: 'ease-out' });
  ctx.after(Math.max(ms, 300) + 20, () => e.remove());
  return e;
}

/** A party sized to the event. Off under Gentle. */
export function burst(ctx, x, y, color = '#ffd24d', n = 12, dist = 70) {
  if (ctx.gentle) return;
  for (let i = 0; i < n; i++) {
    const a = (i / n) * 6.283, d = ctx.el('div', `position:absolute;left:${x}px;top:${y}px;width:6px;height:6px;border-radius:50%;background:${color};`, null, 'over');
    ctx.anim(d, [{ transform: 'translate(0,0)', opacity: 1 }, { transform: `translate(${Math.cos(a) * dist}px,${Math.sin(a) * dist}px)`, opacity: 0 }], { duration: 650, easing: 'ease-out' });
    ctx.after(670, () => d.remove());
  }
}

/** A word that fades in, holds and fades out, then removes itself. Never faster than 250 ms in or out. */
export function flash(ctx, parent, text, css, ms = 1500) {
  const e = ctx.el('div', `position:absolute;pointer-events:none;${css}`, text, parent);
  const edge = Math.max(0.18, Math.min(0.4, 280 / ms));
  ctx.anim(e, [{ opacity: 0 }, { opacity: 1, offset: edge }, { opacity: 1, offset: 1 - edge }, { opacity: 0 }], { duration: ms, fill: 'both' });
  ctx.after(ms + 30, () => e.remove());
  return e;
}

/** A thin progress bar. set(0..1, ms) */
export function bar(ctx, parent, css, color = '#ff69b4', bg = 'rgba(128,128,128,.35)') {
  const o = ctx.el('div', `height:8px;border-radius:4px;overflow:hidden;background:${bg};${css}`, null, parent);
  const i = ctx.el('div', `width:100%;height:100%;background:${color};transform-origin:0 50%;transform:scaleX(0);`, null, o);
  return {
    el: o,
    set(p, ms = 0) { i.style.transition = ms ? `transform ${ms}ms linear` : 'none'; if (ms) void i.offsetWidth; i.style.transform = `scaleX(${Math.max(0, Math.min(1, p))})`; },
  };
}

/* ---- one thing wants the player's attention at a time: a stare and a tap order must never ask at once ---- */
const attention = { main: { until: 0, who: null }, low: { until: 0, who: null } };
/** ns 'low' is the one low panel on the page, shared by the video and ad rows. */
export function claim(ctx, who, ms, ns = 'main') {
  const t = nowOf(ctx), a = attention[ns];
  if (a.until > t && a.until - t < 120000 && a.who !== who) return false;
  a.until = t + ms; a.who = who;
  return true;
}
export function release(who, ns = 'main') { const a = attention[ns]; if (a.who === who) { a.until = 0; a.who = null; } }

/* ---- furniture ---- */
const stacks = { left: [], right: [] };
const prune = () => { for (const s of ['left', 'right']) stacks[s] = stacks[s].filter((p) => p.el.isConnected); };
const RAIL_MIN = 170;

function layout(ctx) {
  prune();
  const { w } = ctx.bounds();
  const railW = (w - 800) / 2;
  if (railW >= RAIL_MIN) {
    docOf(ctx).documentElement.style.setProperty('--routes-mid', '100%');
    for (const s of ['left', 'right']) {
      for (const p of stacks[s]) {
        const rail = ctx.layer('side', s);
        if (p.el.parentElement !== rail) rail.appendChild(p.el);
        p.el.style.cssText = `${p.css};position:relative;left:auto;right:auto;top:auto;width:100%;height:${p.height}px;flex:none;`;
        p.place?.({ w: railW - 16, h: p.height, rail: true });
      }
    }
    return;
  }
  const zone = zoneRect(ctx), btn = rectOf(ctx.button.el);
  const flankW = Math.max(60, Math.min(240, (w - btn.width) / 2 - 16));
  const metersBottom = rectOf(ctx.layer('meters')).bottom;
  const top = Math.max(counterRect(ctx).bottom, Math.min(metersBottom, zone.bottom - 80)) + 2, bottom = zone.bottom - 4;
  docOf(ctx).documentElement.style.setProperty('--routes-mid', stacks.left.length || stacks.right.length ? `${w - 2 * (flankW + 10)}px` : '100%');
  for (const s of ['left', 'right']) {
    const list = stacks[s], gap = 4;
    const each = Math.max(40, (bottom - top - gap * (list.length - 1)) / Math.max(1, list.length));
    list.forEach((p, i) => {
      if (p.el.parentElement !== ctx.layer('floaters')) ctx.layer('floaters').appendChild(p.el);
      p.el.style.cssText = `${p.css};position:absolute;${s}:6px;top:${top + i * (each + gap)}px;width:${flankW}px;height:${each}px;`;
      p.place?.({ w: flankW, h: each, rail: false });
    });
  }
}

/** A free spot on the page: not on the button or the counter. */
export function freeSpot(ctx, pad = 60) {
  const { w, h } = ctx.bounds(), b = btnCentre(ctx), c = counterRect(ctx);
  for (let i = 0; i < 8; i++) {
    const x = 12 + ctx.rng() * (w - 24 - pad), y = 44 + ctx.rng() * (h - 70);
    const onBtn = x + pad > b.left - 8 && x < b.right + 8 && y + 20 > b.top - 8 && y < b.bottom + 8;
    const onCount = x + pad > c.left && x < c.right && y + 20 > c.top && y < c.bottom + 22;
    if (!onBtn && !onCount) return { x, y };
  }
  return { x: 12, y: h - 40 };
}

/** A box for one piece of a route's furniture. side 'left', 'right' or empty for the emptier one. place(size) hears each layout. */
export function panel(ctx, { side = '', height = 170, css = '', place = null } = {}) {
  prune();
  const s = side || (stacks.left.length <= stacks.right.length ? 'left' : 'right');
  const el = ctx.el('div', css);
  baseCss(ctx);
  stacks[s].push({ el, side: s, height, css, place });
  if (s === 'right') ctx.on('routes:corner', ({ on }) => el.classList.toggle('routes-cornered', on && !railsRoom(ctx)));
  layout(ctx);
  let seen = ctx.bounds(), zoneBottom = zoneRect(ctx).bottom;
  ctx.every(700, () => {
    const b = ctx.bounds(), z = zoneRect(ctx).bottom;   // a new meter row moves the button down
    if (b.w !== seen.w || b.h !== seen.h || Math.abs(z - zoneBottom) > 1) { seen = b; zoneBottom = z; layout(ctx); }
  });
  return el;
}
export const railsRoom = (ctx) => (ctx.bounds().w - 800) / 2 >= RAIL_MIN;
/** The one line of free page just above the main button. One speaker at a time: a new line takes it from the last.
 *  ms 0 holds it until dropLine. Toasts keep the line under the button. */
let lineNow = null;
export function setLine(ctx, text, css = '', ms = 2200) {
  lineNow?.remove();
  const b = btnCentre(ctx);
  const e = ctx.el('div', `position:absolute;left:0;right:0;top:${b.top - (ctx.bounds().w < 760 ? 21 : 25)}px;height:22px;line-height:20px;text-align:center;pointer-events:none;padding:0 10px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;font:600 ${ctx.bounds().w < 760 ? 15 : 17}px/20px Arial,Helvetica,sans-serif;${css}`, text);
  lineNow = e;
  ctx.anim(e, [{ opacity: 0 }, { opacity: 1 }], { duration: 250 });
  if (ms) {
    ctx.after(ms - 280, () => { if (lineNow === e) ctx.anim(e, [{ opacity: 1 }, { opacity: 0 }], { duration: 270, fill: 'both' }); });
    ctx.after(ms, () => { if (lineNow === e) { e.remove(); lineNow = null; } });
  }
  return e;
}
export function dropLine(e) { if (e) { if (lineNow === e) lineNow = null; e.remove(); } }

/** One shared row under the counter, so the routes' little meters sit side by side instead of pushing the shop down. */
let rowNow = null;
function baseCss(ctx) {
  const doc = docOf(ctx);
  if (doc.getElementById('routes-lbl-css')) return;
  const s = doc.createElement('style');
  s.id = 'routes-lbl-css';
  s.textContent = '@media(max-width:759px){.routes-lbl{display:none !important}}.routes-cornered{opacity:.12 !important}.routes-cornered,.routes-cornered *{pointer-events:none !important}';
  doc.head.appendChild(s);
}
export function meterRow(ctx) {
  baseCss(ctx);
  if (rowNow?.isConnected) return rowNow;
  rowNow = ctx.layer('over').ownerDocument.createElement('div');
  rowNow.style.cssText = 'display:flex;align-items:center;justify-content:center;gap:6px 10px;flex-wrap:wrap;max-width:var(--routes-mid,100%);';
  ctx.layer('meters').appendChild(rowNow);
  return rowNow;
}

/* ---- tones: a route may repaint the page, and the Watch route stays dark under all of them ---- */
export const DARK = { '--page': '#14101e', '--ink': '#f4eff8', '--ink-soft': '#cfc7dc', '--ink-faint': '#a79fb6', '--btn': '#3a3350', '--panel': '#2a2438', '--panel-short': '#201b2c', '--line': '#6a6180', '--fill': '#4a4162', '--stamp': '#ffb0d6', '--ready': '#4a4162' };
export function darkCss(ctx) {
  ctx.css('#btn{border-color:#8d84a5}#counter,#l-meters,#l-ticker{text-shadow:0 1px 4px #000,0 0 2px #000}');
}
export const onDark = (ctx) => ctx.has('e_spiral') || ctx.has('a_rain') || ctx.has('a_spiral');
/** A colour that reads on the page as it is now. */
export const ink = (ctx, onLight, onDarkPage) => (onDark(ctx) ? onDarkPage : onLight);
/** Sets the page colours unless a dark route holds them. */
export function tone(ctx, vars) { if (!onDark(ctx)) ctx.vars(vars); }

/* ---- the spiral: one canvas per run, shared by the rows that change it ---- */
let spiralNow = null;
export function mountSpiral(ctx, { opacity = 0.6, fadeMs = 1400 } = {}) {
  const c = ctx.el('canvas', `position:absolute;left:50%;top:50%;width:110vmax;height:110vmax;transform:translate(-50%,-50%);opacity:0;transition:opacity ${fadeMs}ms;`, null, 'back');
  const sp = createSpiral(c, { reduced: ctx.gentle, manual: true, ctx });
  sp.show();
  const handle = {
    el: c, base: opacity, speedMul: 1, kick: 0,
    setOpacity(v, ms = 800) { c.style.transition = `opacity ${ms}ms`; c.style.opacity = String(v); },
    restore(ms = 800) { handle.setOpacity(handle.base, ms); },
    step(dt) { handle.kick = Math.max(0, handle.kick - dt * 1.2); sp.faster(handle.speedMul + handle.kick); sp.setStill(ctx.gentle); sp.step(dt); },
    pulse() { if (!ctx.gentle) sp.beat(); },
    recolour(colors) { sp.recolour(colors); },
    dispose() { sp.dispose(); if (spiralNow === handle) spiralNow = null; },
  };
  ctx.frame((dt) => handle.step(dt));
  ctx.after(30, () => { c.style.opacity = String(opacity); });
  spiralNow = handle;
  return handle;
}
export const spiral = () => spiralNow;

/** Flip when ctx.pictures can answer {kind:'clip'} with a muted video: Corner GIF, Video Clips and Mandatory Video stop panning a still. */
export const CLIP_PICTURES = false;

/* ---- ads: one look, one rule. Only a real card links out, on the player's own press, paying once per ad ---- */
export const nextAd = (ctx, kind, opts = {}) => ctx.ads.next({ kind, ...opts });
/** A press on a real ad: pays once for this ad and opens it. Call it directly in the click handler, or ctx.openLink refuses. */
export function pressAd(ctx, rec, at) {
  if (!rec?.ccp || !rec.url) return false;
  let paid = ctx.store.get('paidAds') || [];
  if (!paid.includes(rec.id)) {
    paid = [...paid, rec.id].slice(-60);
    ctx.store.set('paidAds', paid);
    const r = ctx.rates();
    ctx.pay(Math.max(r.fps, r.click) * ctx.config.AD_CLICK_PAYS_SECONDS, 'ad', at);
  }
  ctx.emit('ad', { id: rec.id });
  ctx.openLink(rec.url);
  return true;
}
