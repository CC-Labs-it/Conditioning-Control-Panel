/* Media objects share the dock's movement, visibility, tabs and saved layouts.
 * Legacy position helpers remain exported for old save conversion and layout tooling.
 * Media and gameplay use the same pause/suspend lifecycle as other features. */

import { THEME_TASTES } from './unlocks/theme-skins.js';
import { createPlayer, mediaReady, releasePlayer } from './media-cache.js';
import { ROUTE_COPY as C } from './unlocks/routes/copy.js';

const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));
/** How much of a screen must stand clear of the shop, so there is always something to grab. */
export const GRIP = 44;
const PAD = 8, TOP = 44;

/** Spot <-> share of the free room. */
export const toShare = ({ x, y, w, h }, { W, H }) => ({ fx: W > w ? x / (W - w) : 0, fy: H > h ? y / (H - h) : 0 });
export const fromShare = ({ fx, fy }, { w, h }, { W, H }) => ({ x: clamp(fx, 0, 1) * Math.max(0, W - w), y: clamp(fy, 0, 1) * Math.max(0, H - h) });

/** Inside the window, and never all the way behind the shop: at least GRIP stands clear above or beside it. */
export function clampSpot({ x, y, w, h }, { W, H, shop = null }) {
  const inWindow = (p) => ({ x: clamp(p.x, 0, Math.max(0, W - w)), y: clamp(p.y, 0, Math.max(0, H - h)) });
  const p = inWindow({ x, y });
  if (!shop || !shop.width) return p;
  const clear = (q) => Math.max(shop.top - q.y, shop.left - q.x, q.x + w - shop.right, Math.min(q.y + h - shop.bottom, h));
  if (clear(p) >= Math.min(GRIP, h)) return p;
  const grip = Math.min(GRIP, h);
  const out = [{ x: p.x, y: shop.top - grip }, { x: shop.left - grip, y: p.y }, { x: shop.right + grip - w, y: p.y }]
    .map(inWindow)
    .filter((q) => clear(q) >= grip)
    .sort((a, b) => Math.hypot(a.x - p.x, a.y - p.y) - Math.hypot(b.x - p.x, b.y - p.y));
  return out[0] || inWindow({ x: p.x, y: 0 });
}

/** How big a screen is here. In the rails on a wide window, beside the button on a narrow one. */
export function screenSize({ W, railW, flank, maxH, flankH = maxH }, { ratio = 1.1, wide = false, scale = 1 } = {}) {
  if (railW >= 170) {
    const w = Math.round(clamp((railW - 16) * scale, 130, 300));
    return { w, h: Math.round(w * ratio) };
  }
  const w = Math.round(wide ? Math.min(W * 0.62, 340) : flank);
  return { w, h: Math.round(Math.min(w * ratio, Math.max(60, wide ? maxH : flankH))) };
}

/** The first spot for a new screen. Fills the emptier side, from the bottom of the rails up (the dock fills them from the
 *  top), or the open room under the shop, up from the dock strip, when there are no rails. */
export function defaultSpot({ W, H, railW, top = TOP, floor = H }, { w, h }, placed = []) {
  const left = placed.filter((p) => p.x + p.w / 2 < W / 2), right = placed.filter((p) => p.x + p.w / 2 >= W / 2);
  const onLeft = left.length <= right.length;
  const col = onLeft ? left : right;
  const edge = onLeft ? PAD : W - w - PAD;
  if (railW >= 170) {
    const y = H - PAD - h - col.reduce((sum, p) => sum + p.h + PAD, 0);
    return { x: edge, y: y >= top ? y : top + col.length * 18 };
  }
  const y = floor - PAD - h - col.reduce((sum, p) => sum + p.h + PAD, 0);
  return { x: edge + (onLeft ? 1 : -1) * col.length * 10, y: y >= top ? y : top + col.length * 14 };
}

/** The picked themes' niches come first, since the theme comes first now. roll() is 0..1. */
export function themeNiche(lists, base, roll) {
  const all = lists.flat();
  if (!all.length || roll() >= 0.8) return base;
  return all[Math.floor(roll() * all.length)] ?? base;
}

/* ---------------- the DOM ---------------- */
const CSS = `
.scr{position:relative;width:100%;pointer-events:auto;user-select:none;-webkit-user-select:none;cursor:grab;touch-action:none}
.scr[hidden]{display:none}
.scr-in{position:absolute;inset:0;display:flex;flex-direction:column;overflow:hidden;background:transparent;border:0;border-radius:5px}
.scr.handle .scr-in{padding:6px;background:var(--panel);border-radius:10px}
.scr-body{position:relative;flex:1 1 0;min-height:0;overflow:hidden}
.scr-tag{flex:none;min-height:18px;text-align:center;font:600 11px/18px Arial,Helvetica,sans-serif;color:var(--ink);background:var(--panel);font-variant-numeric:tabular-nums;cursor:grab;touch-action:none}
#counter,#l-meters{text-shadow:0 0 3px var(--page),0 0 6px var(--page)}
.scr-slot{position:absolute;inset:0;overflow:hidden;pointer-events:none}
.scr-slot>img,.scr-slot>video,.scr-slot>canvas{position:absolute;inset:0;width:100%;height:100%;object-fit:cover;pointer-events:none;-webkit-user-drag:none}
.scr-credit{position:absolute;right:2px;bottom:1px;z-index:2;font:10px Arial,Helvetica,sans-serif;color:#fff;text-shadow:0 1px 2px #000;pointer-events:none}
.scr-card{position:absolute;inset:0;z-index:1;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:4px;padding:4px;text-align:center;color:var(--niche-ink);background:linear-gradient(90deg,var(--niche) 0 20%,var(--niche-soft) 20% 40%,var(--niche-2) 40% 60%,var(--niche-soft) 60% 80%,var(--niche) 80%);pointer-events:none}
.scr-card b{display:block;max-width:100%;padding:1px 4px;background:var(--niche-soft);font:400 12px/1.15 Arial,Helvetica,sans-serif}
.scr-card button{min-height:36px;max-width:100%;padding:2px 8px;background:var(--niche-soft);color:var(--niche-ink);border:2px solid var(--niche-ink);font:700 12px/1.1 Arial,Helvetica,sans-serif;pointer-events:auto;cursor:pointer}
.scr-card button:active{transform:translateY(1px)}
.scr-small .scr-card b,.scr-small .scr-hint{display:none}
`;

const live = new Set();
/** Every screen on the page, for rows that decorate them (Favourites). */
export const liveScreens = () => [...live].filter((s) => s.el.isConnected);

function ensureCss(ctx) {
  const doc = ctx.layer('over').ownerDocument;
  if (doc.getElementById('screens-css')) return;
  const s = doc.createElement('style');
  s.id = 'screens-css';
  s.textContent = CSS;
  doc.head.appendChild(s);
}

/** What the shop really holds. Its box runs to the foot of the page and lets the pointer through; only the tiles are in the way. */
export function shopContent(box) {
  const whole = box?.getBoundingClientRect();
  if (!whole?.width) return whole || null;
  const kids = [...box.children].map((k) => k.getBoundingClientRect()).filter((r) => r.width && r.height);
  // Before the first shop draw the flex box fills the page but contains no tiles.
  // Reserving that empty box would push a fresh screen up behind the future shop.
  return { top: whole.top, left: whole.left, right: whole.right, width: whole.width,
    bottom: kids.length ? Math.min(whole.bottom, Math.max(...kids.map((r) => r.bottom))) : whole.top };
}

function room(ctx) {
  const { w: W, h: H } = ctx.bounds();
  const shop = shopContent(ctx.parts.shop);
  const btn = ctx.button.el.getBoundingClientRect();
  /* The shop can still be hidden at the first mount of a load: it will start about here. */
  const free = shop && shop.width ? shop.top : btn.bottom + 12;
  /* No rails: the room under the shop. A frame takes a fair part of the window, not the crack beside the button. */
  const strip = ctx.button.el.ownerDocument.getElementById('dock-strip')?.getBoundingClientRect();
  const railW = (W - 800) / 2;
  return { W, H, shop, railW, floor: strip?.height ? strip.top : H - 50, flank: clamp(W * 0.42, 120, 200), maxH: railW >= 170 ? free - TOP - 6 : H * 0.34, flankH: railW >= 170 ? free - TOP - 6 : H * 0.28, flankTop: Math.max(TOP, Math.round(btn.top) - 8) };
}

/** The file picker has to open inside the press itself. Own files stay on this device. */
function addFiles(ctx) {
  const input = ctx.el('input', 'display:none;', null, ctx.layer('over'));
  input.type = 'file';
  input.multiple = true;
  input.accept = 'image/*,video/*,.gif';
  input.addEventListener('change', () => {
    const kept = ctx.pictures.addOwn(input.files);
    input.remove();
    if (!kept) { ctx.toast(C.screenNotFiles); return; }
    if (ctx.pictures.source !== 'own') ctx.pictures.setSource('own');
  });
  input.addEventListener('cancel', () => input.remove());
  input.click();
}

/** A plain test card in the theme's colours. It still pays. */
export function testCard(ctx, parent, kind = 'picture') {
  const card = ctx.el('div', '', null, parent);
  card.className = 'scr-card';
  const remote = ctx.pictures.source === 'scrolller';
  const label = remote ? 'Waiting for Scrolller media.' : kind === 'gif' ? C.screenNoGif : kind === 'clip' ? C.screenNoClip : C.screenNone;
  ctx.el('b', '', label, card);
  const local = ctx.pictures.source === 'own';
  const b = ctx.el('button', '', local ? C.screenAdd : 'Choose pictures', card);
  b.type = 'button';
  b.addEventListener('click', e => {
    e.stopPropagation();
    if (local) addFiles(ctx); else ctx.emit('choose-pictures');
  });
  return card;
}

/** One real media element, made outside ctx so a long run does not keep every old one. Its parent takes it down.
 *  Muted, always: a clip here never makes a sound. */
export function mediaEl(ctx, parent, pic) {
  const doc = parent.ownerDocument;
  const node = ctx.pictures?.takePlayer?.(pic,doc) || createPlayer(doc,pic);
  parent.appendChild(node);
  return node;
}

/** Gone for good: a clip lets go of its decoder at once. */
export function dropMedia(m) {
  if (!m) return;
  m.__still?.remove();
  m.__still = null;
  releasePlayer(m);
}

const starting = new WeakSet();

/** Play only when nothing holds it: pause, suspend and Gentle all stop a clip. A gif can't be paused, so a still covers it. */
export function holdMedia(ctx, m, wanted = true) {
  if (!m) return;
  const stop = ctx.paused || ctx.gentle || ctx.reduced || ctx.state.ended || m.ownerDocument.hidden || !wanted;
  if (m.tagName === 'VIDEO') {
    if (stop) {
      if (!m.paused || starting.has(m)) m.pause();
    } else if (m.paused && !starting.has(m)) {
      starting.add(m);
      m.play().catch(() => {}).finally(() => starting.delete(m));
    }
    return;
  }
  if (m.dataset.kind !== 'gif') return;
  if (!stop) { m.style.visibility = ''; m.__still?.remove(); m.__still = null; return; }
  if (m.__still) return;
  if (!m.naturalWidth) { m.style.visibility = 'hidden'; return; }
  const c = m.ownerDocument.createElement('canvas');
  const scale = Math.min(1, 640 / Math.max(m.naturalWidth, m.naturalHeight));
  c.width = Math.max(1, Math.round(m.naturalWidth * scale));
  c.height = Math.max(1, Math.round(m.naturalHeight * scale));
  c.getContext('2d').drawImage(m, 0, 0, c.width, c.height);
  c.style.cssText = 'position:absolute;inset:0;width:100%;height:100%;object-fit:cover;pointer-events:none;';
  m.after(c);
  m.__still = c;
  m.style.visibility = 'hidden';
}

/** Browser visibility changes even while the game's own clock is stopped. */
export function watchMedia(ctx, doc, sync) {
  doc.addEventListener('visibilitychange', sync);
  ctx.cleanup(() => doc.removeEventListener('visibilitychange', sync));
  for (const event of ['pause', 'resume', 'suspend', 'ending']) ctx.on(event, sync);
  // Browser-paused videos and a live motion preference change both need a fresh playback check.
  ctx.every(500, sync);
}

/** A box that crossfades between media of one kind. A null answer shows the test card, or nothing with card:false.
 *  stillFallback: a motion slot (gif/clip) shows a still when the source has no motion, and asks for motion again next time. */
export function mediaSlot(ctx, parent, { kind = 'picture', card = true, visible = () => true, stillFallback = false } = {}) {
  ensureCss(ctx);
  const box = ctx.el('div', '', null, parent);
  box.className = 'scr-slot';
  const credit = ctx.el('div', '', '', box);
  credit.className = 'scr-credit';
  let top = null, topUrl = null, ask = 0, cardEl = null, requesting = false, shownAt = 0;
  let requested = false, lastNiche, appended = false;
  const retired = new Set(), loading = new Set();

  const showCard = () => {
    dropMedia(top);
    top = null;
    topUrl = null;
    credit.textContent = '';
    if (!cardEl && card) cardEl = testCard(ctx, parent, stillFallback ? 'picture' : kind);
  };
  let reported = null;
  const sync = () => {
    const shown = visible();
    holdMedia(ctx, top, shown);
    if (top && top !== reported && shown && !ctx.paused && !ctx.state.ended && !box.ownerDocument.hidden) {
      reported = top;
      ctx.pictures.noteView?.();
    }
  };
  const slot = {
    box,
    get media() { return top; },
    get busy() { return requesting || loading.size > 0; },
    /** When the media on show came up (Date.now()), 0 for none. */
    get shownAt() { return top ? shownAt : 0; },
    /** Show the next one. Same file as now: nothing to do, so a lone clip keeps playing. */
    async next(niche, tries = 2) {
      const mine = ++ask;
      requested = true;
      lastNiche = niche;
      appended = false;
      requesting = true;
      let pic = await ctx.pictures.next({ niche, kind, previousUrl: topUrl, allowScrolller: true });
      if (!pic && (kind === 'picture' || stillFallback)) pic = await ctx.pictures.next({ niche, still: true, allowScrolller: true });
      if (mine !== ask || !box.isConnected) return;
      requesting = false;
      if (!pic) {
        if (appended && visible() && !ctx.paused && !box.ownerDocument.hidden) return slot.next(niche);
        showCard(); return;
      }
      if (pic.url === topUrl) return;
      const m = mediaEl(ctx, box, pic);
      m.style.opacity = '0';
      loading.add(m);
      box.insertBefore(m, credit);
      let cancelTimeout;
      const ready = () => {
        cancelTimeout?.();
        if (!loading.has(m)) return;
        loading.delete(m);
        if (mine !== ask || !box.isConnected) { dropMedia(m); return; }
        cardEl?.remove();
        cardEl = null;
        credit.textContent = pic.credit || '';
        m.style.opacity = '';
        // Gentle visuals: a slower plain fade (only ever longer, never a flash).
        const fadeMs = ctx.gentle ? 1200 : 450;
        ctx.anim(m, [{ opacity: 0 }, { opacity: 1 }], { duration: fadeMs });
        const old = top;
        top = m;
        topUrl = pic.url;
        shownAt = Date.now();
        sync();
        if (old) {
          holdMedia(ctx, old, false);
          retired.add(old);
          ctx.after(fadeMs + 20, () => { if (retired.delete(old)) dropMedia(old); });
        }
      };
      const bad = () => {
        cancelTimeout?.();
        if (!loading.has(m)) return;
        loading.delete(m);
        dropMedia(m);
        if (mine !== ask) return;
        if ((tries > 0 || appended) && visible() && !ctx.paused && !box.ownerDocument.hidden) slot.next(niche, Math.max(0, tries - 1));
        else if (!top) showCard();
      };
      m.addEventListener(m.tagName === 'VIDEO' ? 'loadeddata' : 'load', ready, { once: true });
      m.addEventListener('error', bad, { once: true });
      cancelTimeout = ctx.after(12000, bad);
      if (mediaReady(m)) ready();
    },
    clear() {
      ask++;
      requesting = requested = appended = false;
      for (const media of [...loading, ...retired]) dropMedia(media);
      loading.clear(); retired.clear();
      dropMedia(top); top = null; reported = null; topUrl = null; cardEl?.remove(); cardEl = null; credit.textContent = '';
    },
    sync,
  };
  watchMedia(ctx, box.ownerDocument, sync);
  ctx.on('pictures-added', () => {
    if (top || !requested || !visible() || ctx.paused || ctx.state.ended || box.ownerDocument.hidden) return;
    if (slot.busy) appended = true;
    else slot.next(lastNiche);
  });
  ctx.cleanup(slot.clear);
  return slot;
}

/** A tab-controlled media object. handle reserves the feed's interior for its own gestures.
 * Old fractional positions migrate once into the shared layout preferences. */
export function screen(ctx, { kind = null, ratio = 1.1, wide = false, scale = 1, handle = false, niche = 'pretty', every = 0, first = 0, card = true, css = '', place = null, hint = false, key = 'spot', pays = true, edge = undefined, title = undefined, stillFallback = false, open = false } = {}) {
  ensureCss(ctx);
  const width = Math.round(Math.min(wide ? 300 : 220, Math.max(140, ctx.bounds().w - 24)) * scale);
  const size = { w: width, h: Math.round(width * ratio) };
  const old = ctx.store.get(key);
  const legacy = old && Number.isFinite(old.fx) && Number.isFinite(old.fy)
    ? fromShare(old, size, { W: ctx.bounds().w, H: ctx.bounds().h }) : {};
  let syncVisibility = () => {};
  // edge: the side a fresh object prefers; a saved layout always wins. title: its tab (default: the row's name).
  // open: show it at once on a phone too when there is free room (no room: a NEW tab); a saved layout still wins.
  const host = ctx.dock({ width, layout: legacy, edge, title, ...(open ? { min: false } : {}), onMin: () => syncVisibility() });
  const el = ctx.el('div', '', null, host);
  el.className = handle ? 'scr handle' : 'scr';
  el.dataset.dockSurface = '';
  const inner = ctx.el('div', css, null, el); inner.className = 'scr-in';
  const body = ctx.el('div', '', null, inner); body.className = 'scr-body';
  // A feed owns swipes inside its screen. Its physical bezel remains a move surface.
  if (handle) body.dataset.dockInteractive = '';
  const waiting = ctx.el('div', 'padding:6px 8px;background:var(--panel);color:var(--ink);border-radius:6px;font:12px var(--font);', 'Next clip soon.', host);
  waiting.hidden = true;
  let available = true;
  const me = {
    el, body, inner, size, grip: null,
    get visible() { return available && host.dock?.visible !== false; },
    openDock() { host.dock?.setVisible(true); },
    show(on = true) {
      available = on; el.hidden = !on; waiting.hidden = on;
      syncVisibility();
    },
    frame() { const r = el.getBoundingClientRect(); return { x: r.left, y: r.top, w: size.w, h: size.h }; },
  };
  const resize = () => {
    const w = host.clientWidth || width;
    const h = Math.round(Math.min(w * ratio, Math.max(100, ctx.bounds().h - 130)));
    if (size.w === w && size.h === h && el.style.height) return;
    size.w = w; size.h = h; el.style.height = `${h}px`;
    el.classList.toggle('scr-small', w < 150);
    place?.({ ...size, rail: ctx.bounds().w >= 1100 });
  };
  resize(); ctx.after(0, resize);
  ctx.every(500, resize);
  live.add(me);
  ctx.cleanup(() => live.delete(me));
  const fps = pays ? ctx.item?.fps || 0 : 0;
  if (fps) {
    const tag = ctx.el('div', '', C.screenPay.replace('{n}', ctx.fmt(fps * (ctx.state.pace || 1))), inner);
    tag.className = 'scr-tag'; tag.dataset.dockSurface = '';
  }
  if (kind) {
    const slot = mediaSlot(ctx, body, { kind, card, visible: () => me.visible, stillFallback });
    me.slot = slot;
    const pickNiche = () => themeNiche(themeLists(ctx), typeof niche === 'function' ? niche() : niche, ctx.rng);
    me.next = () => {
      if (!slot.busy && me.visible && !el.ownerDocument.hidden && !ctx.paused && !ctx.state.ended) return slot.next(pickNiche());
    };
    if (first >= 0 && every >= 0) ctx.after(first, me.next);
    if (every > 0) ctx.every(every, () => { if (me.visible) me.next(); });
    ctx.on('pictures', () => { slot.clear(); if (me.visible) me.next(); });
    const refill = () => { if (available && !slot.media) me.next(); };
    ctx.on('resume', refill);
    ctx.on('suspend', ({ on }) => { if (!on) refill(); });
    el.ownerDocument.addEventListener('visibilitychange', refill);
    ctx.cleanup(() => el.ownerDocument.removeEventListener('visibilitychange', refill));
    syncVisibility = () => {
      slot.sync();
      refill();
    };
  }
  return me;
}

/** The niches of the themes picked so far, first theme first. */
export function themeLists(ctx) {
  const ids = [...(ctx.palette?.().themes || []), ctx.pick('f5')].filter(Boolean);
  return [...new Set(ids)].map((id) => THEME_TASTES[id]?.niches || []);
}
