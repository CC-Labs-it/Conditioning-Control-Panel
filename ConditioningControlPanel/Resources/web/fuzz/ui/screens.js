/* ui/screens.js - a screen is a framed panel that shows pictures, gifs or clips, pays over time, and goes where the player
 * drags it, by mouse or finger. It lives in its own layer under the button, the shop and Wake Up, so no screen can ever make
 * those unpressable. Its spot is kept in the save as a share of the free room, so a resize or another window keeps it in the
 * same corner. The pay itself is the row's own per-second number in the sim: a screen only shows it.
 * Top half: pure helpers (tests/screens.test.mjs). Bottom half: the DOM. Nothing touches the DOM at import time. */

import { THEME_TASTES } from './unlocks/theme-skins.js';
import { ROUTE_COPY as C } from './unlocks/routes/copy.js';

const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));
/** How much of a screen must stand clear of the shop, so there is always something to grab. */
export const GRIP = 44;
const PAD = 8, TOP = 44;
const DRAG_FROM = 5;

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
.scr{position:absolute;left:0;top:0;pointer-events:auto;touch-action:none;user-select:none;-webkit-user-select:none;cursor:grab;will-change:transform}
.scr.held{cursor:grabbing}
.scr.handle{cursor:default}
.scr-in{position:absolute;inset:0;display:flex;flex-direction:column;overflow:hidden;background:var(--panel);border:3px solid var(--niche);border-image:linear-gradient(135deg,var(--niche),var(--niche-2)) 1;box-shadow:2px 2px 0 rgba(0,0,0,.35);transition:box-shadow .12s ease-out,transform .12s ease-out}
.scr.held .scr-in{box-shadow:5px 7px 0 rgba(0,0,0,.28);transform:scale(1.03)}
body.gentle .scr.held .scr-in{transform:none}
.scr-grip{position:relative;flex:none;height:24px;display:flex;align-items:center;justify-content:center;background:linear-gradient(90deg,var(--niche),var(--niche-2));cursor:grab;touch-action:none}
.scr-grip::before{content:"";width:34px;height:8px;border-top:2px solid var(--niche-ink);border-bottom:2px solid var(--niche-ink);opacity:.7}
.scr.held .scr-grip{cursor:grabbing}
.scr-body{position:relative;flex:1 1 0;min-height:0;overflow:hidden}
.scr-tag{flex:none;height:15px;text-align:center;font:700 11px/15px Arial,Helvetica,sans-serif;background:linear-gradient(90deg,var(--niche),var(--niche-2));color:var(--niche-ink);pointer-events:none;font-variant-numeric:tabular-nums}
.scr-grip .scr-tag{position:absolute;right:4px;top:4px;background:none}
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
let zTop = 1;
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
function shopContent(box) {
  const whole = box?.getBoundingClientRect();
  if (!whole?.width) return whole || null;
  const kids = [...box.children].map((k) => k.getBoundingClientRect()).filter((r) => r.width && r.height);
  return kids.length ? { top: whole.top, left: whole.left, right: whole.right, width: whole.width, bottom: Math.max(...kids.map((r) => r.bottom)) } : whole;
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
  const line = ctx.el('b', '', kind === 'gif' ? C.screenNoGif : kind === 'clip' ? C.screenNoClip : C.screenNone, card);
  void line;
  const b = ctx.el('button', '', C.screenAdd, card);
  b.type = 'button';
  b.addEventListener('click', (e) => { e.stopPropagation(); addFiles(ctx); });
  return card;
}

/** One real media element, made outside ctx so a long run does not keep every old one. Its parent takes it down.
 *  Muted, always: a clip here never makes a sound. */
export function mediaEl(ctx, parent, pic) {
  const doc = parent.ownerDocument;
  if (pic.kind === 'clip') {
    const v = doc.createElement('video');
    v.muted = true;
    v.defaultMuted = true;
    v.setAttribute('muted', '');
    v.loop = true;
    v.playsInline = true;
    v.setAttribute('playsinline', '');
    v.disablePictureInPicture = true;
    v.preload = 'auto';
    v.src = pic.url;
    v.dataset.kind = pic.kind;
    parent.appendChild(v);
    return v;
  }
  const i = doc.createElement('img');
  i.alt = '';
  i.draggable = false;
  i.decoding = 'async';
  i.src = pic.url;
  i.dataset.kind = pic.kind;
  parent.appendChild(i);
  return i;
}

/** Gone for good: a clip lets go of its decoder at once. */
export function dropMedia(m) {
  if (!m) return;
  m.__still?.remove();
  if (m.tagName === 'VIDEO') { m.pause(); m.removeAttribute('src'); m.load(); }
  m.remove();
}

/** Play only when nothing holds it: pause, suspend and Gentle all stop a clip. A gif can't be paused, so a still covers it. */
export function holdMedia(ctx, m, wanted = true) {
  if (!m) return;
  const stop = ctx.paused || ctx.gentle || !wanted;
  if (m.tagName === 'VIDEO') {
    if (stop) m.pause();
    else m.play?.()?.catch?.(() => {});
    return;
  }
  if (m.dataset.kind !== 'gif') return;
  if (!stop) { m.style.visibility = ''; m.__still?.remove(); m.__still = null; return; }
  if (m.__still || !m.naturalWidth) return;
  const c = m.ownerDocument.createElement('canvas');
  c.width = Math.min(640, m.naturalWidth);
  c.height = Math.round(c.width * (m.naturalHeight / m.naturalWidth));
  c.getContext('2d').drawImage(m, 0, 0, c.width, c.height);
  c.style.cssText = 'position:absolute;inset:0;width:100%;height:100%;object-fit:cover;pointer-events:none;';
  m.after(c);
  m.__still = c;
  m.style.visibility = 'hidden';
}

/** A box that crossfades between media of one kind. A null answer shows the test card, or nothing with card:false. */
export function mediaSlot(ctx, parent, { kind = 'picture', card = true } = {}) {
  const box = ctx.el('div', '', null, parent);
  box.className = 'scr-slot';
  const credit = ctx.el('div', '', '', box);
  credit.className = 'scr-credit';
  let top = null, topUrl = null, ask = 0, cardEl = null;

  const showCard = () => {
    dropMedia(top);
    top = null;
    topUrl = null;
    credit.textContent = '';
    if (!cardEl && card) cardEl = testCard(ctx, parent, kind);
  };
  const sync = () => holdMedia(ctx, top);
  const slot = {
    box,
    get media() { return top; },
    /** Show the next one. Same file as now: nothing to do, so a lone clip keeps playing. */
    async next(niche, tries = 2) {
      const mine = ++ask;
      const pic = await ctx.pictures.next({ niche, kind });
      if (mine !== ask || !box.isConnected) return;
      if (!pic) { showCard(); return; }
      if (pic.url === topUrl) return;
      const m = mediaEl(ctx, box, pic);
      box.insertBefore(m, credit);
      const ready = () => {
        if (mine !== ask || !box.isConnected) { dropMedia(m); return; }
        cardEl?.remove();
        cardEl = null;
        credit.textContent = pic.credit || '';
        ctx.anim(m, [{ opacity: 0 }, { opacity: 1 }], { duration: 450 });
        const old = top;
        top = m;
        topUrl = pic.url;
        holdMedia(ctx, m);
        if (old) { old.pause?.(); ctx.after(470, () => dropMedia(old)); }
      };
      const bad = () => {
        dropMedia(m);
        if (mine !== ask) return;
        if (tries > 0) slot.next(niche, tries - 1);
        else if (!top) showCard();
      };
      m.addEventListener(m.tagName === 'VIDEO' ? 'loadeddata' : 'load', ready, { once: true });
      m.addEventListener('error', bad, { once: true });
    },
    clear() { ask += 1; dropMedia(top); top = null; topUrl = null; cardEl?.remove(); cardEl = null; credit.textContent = ''; },
    sync,
  };
  ctx.on('pause', sync);
  ctx.on('resume', sync);
  ctx.on('suspend', sync);
  /* Gentle can change from the host with no pause around it. */
  let wasGentle = ctx.gentle;
  ctx.every(1000, () => { if (ctx.gentle !== wasGentle) { wasGentle = ctx.gentle; sync(); } });
  return slot;
}

/** A draggable screen.
 *  kind: 'picture' | 'gif' | 'clip' draws media; null leaves an empty body for the caller.
 *  ratio: height / width. wide: takes more room on a narrow window. handle: only the grip strip drags (the body has gestures).
 *  niche: base niche, or a function answering one. every / first: ms between changes, first change. card: false for no test card.
 *  place(size): hears every new size. hint: says it can be dragged, once. key: a store key, for an unlock with two screens. */
export function screen(ctx, { kind = null, ratio = 1.1, wide = false, scale = 1, handle = false, niche = 'pretty', every = 0, first = 0, card = true, css = '', place = null, hint = false, key = 'spot', pays = true } = {}) {
  ensureCss(ctx);
  const el = ctx.el('div', '', null, 'screens');
  el.className = handle ? 'scr handle' : 'scr';
  const inner = ctx.el('div', css, null, el);
  inner.className = 'scr-in';
  const grip = handle ? ctx.el('div', '', null, inner) : null;
  if (grip) { grip.className = 'scr-grip'; grip.setAttribute('aria-label', C.screenDrag); }
  const body = ctx.el('div', '', null, inner);
  body.className = 'scr-body';

  const size = { w: 0, h: 0 };
  let spot = { x: 0, y: 0 };
  const apply = () => { el.style.transform = `translate3d(${Math.round(spot.x)}px,${Math.round(spot.y)}px,0)`; };
  const resize = (r) => {
    const s = screenSize(r, { ratio, wide, scale });
    if (s.w !== size.w || s.h !== size.h) {
      size.w = s.w; size.h = s.h;
      el.style.width = `${s.w}px`;
      el.style.height = `${s.h}px`;
      el.classList.toggle('scr-small', s.w < 150);
      place?.({ ...size, rail: r.railW >= 170 });
    }
  };
  const settle = (r) => { spot = clampSpot({ ...spot, ...size }, r); apply(); };
  const remember = (r) => ctx.store.set(key, toShare({ ...spot, ...size }, r));

  const me = {
    el, body, inner, size, grip,
    get visible() { return el.style.display !== 'none'; },
    show(on = true) { if (!on) drop(); el.style.display = on ? '' : 'none'; el.ownerDocument.dispatchEvent(new Event('fz-screens')); },
  };

  /* First spot: what the save says, or a free place that then goes in the save. */
  {
    const r = room(ctx);
    resize(r);
    const saved = ctx.store.get(key);
    const placed = liveScreens().map((s) => s.frame());
    spot = saved && Number.isFinite(saved.fx) && Number.isFinite(saved.fy) ? fromShare(saved, size, r) : defaultSpot({ ...r, top: r.railW >= 170 ? TOP : r.flankTop }, size, placed);
    settle(r);
    if (!saved) remember(r);
  }
  me.frame = () => ({ ...spot, ...size });
  live.add(me);
  ctx.cleanup(() => { live.delete(me); me.slot?.clear(); });   // a clip lets go of its decoder, and nothing keeps the old frame alive

  /* The whole frame drags, or only the grip. On a small frame the test card's button is most of it, and a finger is fat, so
   * a drag may start on that button too: if it never moves, the press stays the button's. A heart or a fake close (they
   * stop the event themselves) stay theirs. */
  let drag = null;
  const from = handle ? grip : el;
  const lift = (on) => { el.classList.toggle('held', on); el.parentElement?.classList.toggle('lifted', on); };
  /* Can it still be grabbed where it landed? Under the counter, the button or a dock panel may leave nothing to press. */
  const reachable = () => {
    const doc = el.ownerDocument;
    const hold = handle ? 24 : size.h;
    for (let i = 0; i < 5; i++) {
      for (let j = 0; j < 5; j++) {
        const x = spot.x + (size.w * (i * 2 + 1)) / 10, y = spot.y + (hold * (j * 2 + 1)) / 10;
        const hit = doc.elementFromPoint(x, y);
        if (hit && from.contains(hit)) return true;
      }
    }
    return false;
  };
  const tellDock = () => el.ownerDocument.dispatchEvent(new Event('fz-screens'));
  /* The dock grew over it, or the shop did. It walks to the nearest place with something showing, and stays there. */
  const rescue = (r) => {
    const doc = el.ownerDocument;
    const clearAt = (p) => {
      const hit = doc.elementFromPoint(p.x + size.w / 2, p.y + (handle ? 12 : size.h / 2));
      return !hit || hit.closest('.scr') === el || !hit.closest('.dk,#shop,#btn,#dock-strip,.xq,.scr');
    };
    const spots = [];
    for (let y = Math.max(0, r.H - size.h - PAD); y >= TOP; y -= Math.max(40, size.h / 2)) {
      for (let x = PAD; x <= r.W - size.w - PAD; x += Math.max(40, size.w / 2)) spots.push({ x, y });
    }
    spots.sort((a, b) => Math.hypot(a.x - spot.x, a.y - spot.y) - Math.hypot(b.x - spot.x, b.y - spot.y));
    const to = spots.find(clearAt);
    if (!to) return;
    spot = to;
    settle(r);
    remember(r);
    tellDock();
  };
  const drop = (keep = true) => {
    if (!drag) return;
    const d = drag;
    drag = null;
    try { from.releasePointerCapture(d.id); } catch { /* gone */ }
    lift(false);
    if (!d.moved) return;
    const r = room(ctx);
    if (keep) {
      settle(r);
      if (!reachable()) { spot = d.start; settle(r); }   // nothing to grab it by there: it goes back
      remember(r);
      tellDock();
    }
    if (!ctx.gentle) ctx.anim(inner, [{ transform: 'scale(1.03)' }, { transform: 'scale(.985)' }, { transform: 'none' }], { duration: 180, easing: 'ease-out' });
  };
  from.addEventListener('pointerdown', (e) => {
    if (e.button > 0 || ctx.paused) return;
    const onButton = !!e.target.closest?.('button,a,input');
    if (!onButton) e.preventDefault();
    drag = { id: e.pointerId, sx: e.clientX, sy: e.clientY, ox: spot.x, oy: spot.y, start: { ...spot }, moved: false, r: room(ctx) };
    if (!onButton) { try { from.setPointerCapture(e.pointerId); } catch { /* fine */ } }
    el.style.zIndex = String(++zTop);   // the one in hand comes to the front
  });
  from.addEventListener('pointermove', (e) => {
    if (!drag || e.pointerId !== drag.id) return;
    const dx = e.clientX - drag.sx, dy = e.clientY - drag.sy;
    if (!drag.moved) {
      if (Math.hypot(dx, dy) < DRAG_FROM) return;
      drag.moved = true;
      lift(true);
      try { from.setPointerCapture(e.pointerId); } catch { /* fine */ }
    }
    spot = { x: clamp(drag.ox + dx, 0, Math.max(0, drag.r.W - size.w)), y: clamp(drag.oy + dy, 0, Math.max(0, drag.r.H - size.h)) };
    apply();
  });
  from.addEventListener('pointerup', (e) => { if (drag && e.pointerId === drag.id) drop(); });
  from.addEventListener('pointercancel', (e) => { if (drag && e.pointerId === drag.id) drop(); });
  from.addEventListener('dragstart', (e) => e.preventDefault());
  ctx.on('pause', () => drop());
  ctx.on('suspend', ({ on }) => { if (on) drop(); });

  tellDock();
  /* Buried twice running, with no card up to explain it: move. */
  let buried = 0;
  ctx.every(2500, () => {
    if (drag || !me.visible || el.ownerDocument.querySelector('#cards .backdrop')) { buried = 0; return; }
    const on = el.ownerDocument.elementFromPoint(spot.x + size.w / 2, spot.y + size.h / 2);
    buried = reachable() || !on?.closest('.dk,#shop') ? 0 : buried + 1;
    if (buried >= 2) { buried = 0; rescue(room(ctx)); }
  });

  /* A resize keeps the spot's share of the free room. */
  let seen = ctx.bounds();
  let shopUp = !!ctx.parts.shop?.getBoundingClientRect().width;
  ctx.every(500, () => {
    const b = ctx.bounds();
    if (!shopUp && !drag && ctx.parts.shop?.getBoundingClientRect().width) {
      shopUp = true;
      const r = room(ctx);
      resize(r);
      settle(r);
    }
    if ((b.w === seen.w && b.h === seen.h) || drag) return;
    const share = toShare({ ...spot, ...size }, { W: seen.w, H: seen.h });
    seen = b;
    const r = room(ctx);
    resize(r);
    spot = fromShare(share, size, r);
    settle(r);
  });

  /* What it pays, on the frame and rising from it now and then. The money itself is the row's fps in the sim. */
  const fps = pays ? ctx.item?.fps || 0 : 0;
  if (fps) {
    const tag = ctx.el('div', '', C.screenPay.replace('{n}', ctx.fmt(fps * (ctx.state.pace || 1))), grip || inner);
    tag.className = 'scr-tag';
    ctx.every(4000, () => {
      if (!me.visible) return;
      ctx.float(spot.x + size.w / 2, spot.y + 6, `+${ctx.fmt(fps * (ctx.state.pace || 1) * 4)}`, 'font-weight:700;color:var(--ink);text-shadow:0 0 3px var(--page);');
    });
  }

  if (!ctx.gentle) ctx.anim(inner, [{ transform: 'translateY(-14px) scale(.9)', opacity: 0 }, { transform: 'none', opacity: 1 }], { duration: 320, easing: 'cubic-bezier(.2,1.4,.5,1)' });
  if (hint) {
    const h = ctx.el('div', 'position:absolute;left:0;right:0;top:4px;z-index:4;text-align:center;font:700 12px Arial,Helvetica,sans-serif;color:var(--niche-ink);pointer-events:none;', C.screenDrag, body);
    h.classList.add('scr-hint');
    h.style.textShadow = '0 0 3px var(--niche-soft),0 0 3px var(--niche-soft)';
    ctx.anim(h, [{ opacity: 0 }, { opacity: 1, offset: 0.1 }, { opacity: 1, offset: 0.8 }, { opacity: 0 }], { duration: 3600, fill: 'both' });
    ctx.after(3700, () => h.remove());
  }

  /* The media, and the theme's niches first. */
  if (kind) {
    const slot = mediaSlot(ctx, body, { kind, card });
    me.slot = slot;
    const pickNiche = () => {
      const base = typeof niche === 'function' ? niche() : niche;
      return themeNiche(themeLists(ctx), base, ctx.rng);
    };
    me.next = () => slot.next(pickNiche());
    if (first >= 0 && every >= 0) ctx.after(first, me.next);
    if (every > 0) ctx.every(every, () => { if (me.visible) me.next(); });
    ctx.on('pictures', () => { if (me.visible) { slot.clear(); me.next(); } });
  }
  return me;
}

/** The niches of the themes picked so far, first theme first. */
export function themeLists(ctx) {
  const ids = [...(ctx.palette?.().themes || []), ctx.pick('f5')].filter(Boolean);
  return [...new Set(ids)].map((id) => THEME_TASTES[id]?.niches || []);
}
