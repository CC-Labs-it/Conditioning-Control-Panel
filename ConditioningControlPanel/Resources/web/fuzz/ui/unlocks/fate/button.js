/* ui/unlocks/fate/button.js - the Living Button fate cards (handoff-1110/TC-FATE-CARDS-BRIEF.md): ft_depth, ft_cursor,
 * ft_pressed, ft_glass. Each one hints, without a word, that the player is the button: things on the page have a back,
 * someone else's cursor comes to press, the page flinches when pressed, and a curved pane of glass sits in front of it all.
 * The ending (o_out) keeps its show in ui/unlocks/routes/sell.js. Numbers and the cursor drawing: ./button/model.js.
 * Cosmetic randomness is Math.random throughout; every clock is a ctx clock, so Wake Up and pause freeze it all. */

import { LITE, frameRect } from '../../perf.js';
import {
  LEAN, normPointer, depthOf, leanFor, drift, approach, leanCss,
  CURSOR, TIP, nextVisitMs, pressCount, edgePoint, bendPoint, curve, smooth, aimOffset, cursorSvg,
  FLINCH, mayFlinch, GLASS, nextSheenMs,
} from './button/model.js';

const calm = (ctx) => ctx.gentle || ctx.reduced;
const rnd = Math.random;

/* ---- A Little Depth: the button, the counter and the dock objects lean with the pointer, each at its own depth. Never
 * the shop and never #page: only the objects themselves, through the individual translate/rotate properties, so their
 * own transforms (the button's swap, a toy's animations) stay theirs. ---- */
function mountDepth(ctx) {
  const doc = ctx.button.el.ownerDocument, win = doc.defaultView;
  const fine = !!win.matchMedia?.('(any-pointer: fine)').matches;
  let px = 0, py = 0, pointer = false, t = 0, sinceWrite = 0;
  const onMove = (ev) => {
    if (ev.pointerType === 'touch') return;
    const { w, h } = ctx.bounds();
    const n = normPointer(ev.clientX, ev.clientY, w, h);
    px = n.nx; py = n.ny; pointer = true;
  };
  win.addEventListener('pointermove', onMove, { passive: true });
  ctx.cleanup(() => win.removeEventListener('pointermove', onMove));

  const live = new Map();   // el -> { depth, x, y, r, was: [translate, rotate], wrote }
  function take(el, depth) {
    let s = live.get(el);
    if (!s) {
      s = { depth, x: 0, y: 0, r: 0, was: [el.style.translate, el.style.rotate], wrote: '' };
      live.set(el, s);
    }
    s.depth = depth;
    return s;
  }
  function give(el) {
    const s = live.get(el);
    if (!s) return;
    el.style.translate = s.was[0];
    el.style.rotate = s.was[1];
    live.delete(el);
  }
  const usable = (el) => el.isConnected && !el.hidden && !el.matches('.min,.door,.dragging');
  function refresh() {
    const keep = new Set();
    const add = (el, depth) => { if (el && usable(el)) { take(el, depth); keep.add(el); } };
    add(ctx.parts.buttonWrap, 1);
    add(ctx.parts.counter, 0.5);
    const cap = LITE ? LEAN.liteObjects : LEAN.maxObjects;
    const docks = [...ctx.layer('side').querySelectorAll('.dk')].filter(usable).slice(0, cap);
    docks.forEach((el, i) => add(el, depthOf(i)));
    for (const el of [...live.keys()]) if (!keep.has(el)) give(el);
  }
  refresh();
  ctx.every(1200, refresh);
  ctx.cleanup(() => { for (const el of [...live.keys()]) give(el); });

  return {
    tick(dt) {
      t += dt;
      let nx = 0, ny = 0;
      if (!calm(ctx)) {
        if (pointer) { nx = px; ny = py; }
        else if (!fine) { const d = drift(t); nx = d.nx; ny = d.ny; }
      }
      sinceWrite += dt * 1000;
      if (LITE && sinceWrite < LEAN.liteEveryMs) return;
      const step = LITE ? sinceWrite / 1000 : dt;
      sinceWrite = 0;
      for (const [el, s] of live) {
        if (el.classList.contains('dragging')) { give(el); continue; }
        const goal = leanFor(nx, ny, s.depth);
        s.x = approach(s.x, goal.x, step); s.y = approach(s.y, goal.y, step); s.r = approach(s.r, goal.r, step);
        const css = leanCss(s), key = css.translate + css.rotate;
        if (key === s.wrote) continue;
        s.wrote = key;
        el.style.translate = css.translate;
        el.style.rotate = css.rotate;
      }
    },
  };
}

/* ---- Second Cursor: now and then another cursor drifts in from an edge, presses the main button once or a few times
 * through ctx.button.press() (it pays like a tap), and leaves. It never touches anything else, and nothing can touch it. */
function mountCursor(ctx) {
  const doc = ctx.button.el.ownerDocument, win = doc.defaultView;
  const el = ctx.el('div', `position:absolute;left:0;top:0;width:${CURSOR.size}px;height:${CURSOR.size}px;pointer-events:none;opacity:0;transform-origin:${TIP}px ${TIP}px;`, null, 'fx');
  el.setAttribute('aria-hidden', 'true');
  el.dataset.fateCursor = '';
  // A pale cursor with a theme-coloured edge: almost the player's, not quite.
  const paint = () => {
    const edge = win.getComputedStyle(doc.documentElement).getPropertyValue('--accent-deep').trim() || '#2a1830';
    el.innerHTML = cursorSvg({ edge });
  };
  paint();
  ctx.on('palette', paint);

  let visit = null, offFrame = null;
  const place = (p, scale = 1) => { el.style.transform = `translate(${p.x - TIP}px, ${p.y - TIP}px) scale(${scale})`; };
  const target = (aim) => {
    const r = frameRect(ctx.button.el);
    if (!(r.width > 4 && r.height > 4)) return null;
    return { x: r.left + r.width * (0.5 + aim.fx), y: r.top + r.height * (0.5 + aim.fy) };
  };
  function end() {
    offFrame?.(); offFrame = null;
    visit = null;
    el.style.opacity = '0';
    ctx.after(nextVisitMs(rnd), start);
  }
  function press(at) {
    const gain = ctx.button.press();
    ctx.emit('fate-cursor', { x: at.x, y: at.y, gain });
    if (gain > 0) ctx.float(at.x + 12, at.y - 10, `+${ctx.fmt(gain)}`);
    return gain;
  }
  function start() {
    const { w, h } = ctx.bounds();
    const aim = aimOffset(rnd);
    const at = target(aim);
    if (!at) { ctx.after(nextVisitMs(rnd), start); return; }
    const gentle = calm(ctx);
    const from = edgePoint(rnd, w, h), out = edgePoint(rnd, w, h, from.side);
    visit = {
      phase: gentle ? 'fade-in' : 'in', t: 0, aim, from, out, bend: bendPoint(rnd, from, at),
      travel: CURSOR.travelMs[0] + (CURSOR.travelMs[1] - CURSOR.travelMs[0]) * rnd(),
      leave: CURSOR.leaveMs[0] + (CURSOR.leaveMs[1] - CURSOR.leaveMs[0]) * rnd(),
      left: pressCount(rnd), wait: 0, down: 0, at, gentle,
    };
    place(gentle ? at : from);
    el.style.opacity = gentle ? '0' : '1';
    offFrame = ctx.frame(step);
  }
  function step(dt) {
    const v = visit;
    if (!v) return;
    const ms = dt * 1000;
    v.t += ms;
    const now = target(v.aim) || v.at;
    v.at = now;
    if (v.phase === 'in') {
      place(curve(v.from, v.bend, now, smooth(v.t / v.travel)));
      if (v.t >= v.travel) { v.phase = 'press'; v.t = 0; v.wait = 140; }
    } else if (v.phase === 'fade-in') {
      place(now);
      el.style.opacity = String(Math.min(1, v.t / CURSOR.fadeMs));
      if (v.t >= CURSOR.fadeMs) { v.phase = 'press'; v.t = 0; v.wait = 240; }
    } else if (v.phase === 'press') {
      if (v.down > 0) v.down -= ms;
      place(now, v.down > 0 ? 0.86 : 1);
      v.wait -= ms;
      if (v.wait > 0) return;
      if (v.left <= 0) {
        v.phase = v.gentle ? 'fade-out' : 'out'; v.t = 0;
        v.from = now; v.bend = bendPoint(rnd, now, v.out);
        return;
      }
      v.left -= 1;
      press(now);
      if (!v.gentle) { v.down = 110; place(now, 0.86); }
      const [lo, hi] = CURSOR.pressGapMs;
      v.wait = v.gentle ? CURSOR.calmPressGapMs : lo + (hi - lo) * rnd();
    } else if (v.phase === 'out') {
      const k = smooth(v.t / v.leave);
      place(curve(v.from, v.bend, v.out, k * k));
      if (v.t >= v.leave) end();
    } else if (v.phase === 'fade-out') {
      el.style.opacity = String(Math.max(0, 1 - v.t / CURSOR.fadeMs));
      if (v.t >= CURSOR.fadeMs) end();
    }
  }
  ctx.after(nextVisitMs(rnd, { first: true, restored: ctx.restored }), start);
  return {};
}

/* ---- Pressed: every press of the player's own (and every press of that other cursor) makes the whole view flinch:
 * a 1-2 px dip of the world and a soft dark rim. Gentle and reduced motion: no movement, only the rim as a slow fade,
 * at most one every 520 ms. ---- */
function mountPressed(ctx) {
  const veil = ctx.el('div', 'position:absolute;inset:0;pointer-events:none;opacity:0;box-shadow:inset 0 0 90px 18px rgba(24,8,34,.42),inset 0 0 0 2px rgba(24,8,34,.12);', null, 'fx');
  veil.setAttribute('aria-hidden', 'true');
  veil.dataset.fatePressed = '';
  let last = -1e9;
  function flinch(strength) {
    const now = ctx.activeMs, quiet = calm(ctx);
    if (!mayFlinch(last, now, { calm: quiet, lite: LITE })) return;
    last = now;
    if (quiet) {
      ctx.anim(veil, [{ opacity: 0 }, { opacity: 0.32 }, { opacity: 0 }], { duration: FLINCH.calmMs, easing: 'ease-in-out' });
      return;
    }
    ctx.anim(veil, [{ opacity: 0 }, { opacity: Math.min(0.75, 0.5 * strength), offset: 0.22 }, { opacity: 0 }], { duration: FLINCH.veilMs, easing: 'ease-out' });
    const dip = (LITE ? FLINCH.litePx : FLINCH.dipPx) * Math.min(1.25, strength);
    ctx.anim(ctx.world, [{ translate: '0 0' }, { translate: `0 ${dip.toFixed(2)}px`, offset: 0.28 }, { translate: '0 0' }], { duration: FLINCH.ms, easing: 'ease-out' });
  }
  ctx.on('tap', (e) => { if (!e.auto) flinch(1); });
  ctx.on('fate-cursor', () => flinch(1.25));
  return {};
}

/* ---- Glass: a faint curved pane over the page edges. Static highlight arcs and a darker rim, all CSS; a slow sheen
 * crosses every 20-30 s (Gentle and reduced motion: it fades in and out where it stands). ---- */
const GLASS_CSS = `
.ft-glass{position:absolute;inset:0;pointer-events:none;overflow:hidden;border-radius:clamp(14px,3vmin,30px);
  box-shadow:inset 0 0 0 1px rgba(255,255,255,.16),inset 0 0 0 2px rgba(30,14,40,.06),inset 0 0 70px rgba(30,14,40,.13);
  background:radial-gradient(130% 110% at 50% 50%,transparent 74%,rgba(255,255,255,.035) 88%,rgba(255,255,255,.08) 97%,rgba(255,255,255,.02) 100%)}
.ft-glass i{position:absolute;display:block;border-radius:50%;border:0 solid transparent}
.ft-glass .a1{left:-14vmax;top:-12vmax;width:78vmax;height:56vmax;border-top:2px solid rgba(255,255,255,.34);border-left:1px solid rgba(255,255,255,.12);transform:rotate(-8deg);
  -webkit-mask-image:linear-gradient(90deg,transparent 4%,#000 30%,#000 52%,transparent 78%);mask-image:linear-gradient(90deg,transparent 4%,#000 30%,#000 52%,transparent 78%)}
.ft-glass .a2{left:-6vmax;top:-4vmax;width:64vmax;height:44vmax;border-top:5px solid rgba(255,255,255,.10);transform:rotate(-6deg);
  -webkit-mask-image:linear-gradient(90deg,transparent 12%,#000 34%,transparent 60%);mask-image:linear-gradient(90deg,transparent 12%,#000 34%,transparent 60%)}
.ft-glass .a3{right:-16vmax;bottom:-14vmax;width:70vmax;height:50vmax;border-bottom:2px solid rgba(255,255,255,.22);transform:rotate(-8deg);
  -webkit-mask-image:linear-gradient(270deg,transparent 6%,#000 30%,transparent 62%);mask-image:linear-gradient(270deg,transparent 6%,#000 30%,transparent 62%)}
/* a thin darker refraction line under each highlight, so the pane still reads on a pale page */
.ft-glass .a1::after,.ft-glass .a3::after{content:'';position:absolute;inset:4px;border-radius:50%;border:0 solid transparent}
.ft-glass .a1::after{border-top:1px solid rgba(30,10,40,.13)}
.ft-glass .a3::after{border-bottom:1px solid rgba(30,10,40,.11)}
.ft-glass .sheen{top:-25%;bottom:-25%;left:0;width:16vmax;border-radius:0;opacity:0;transform:translateX(-30vmax) skewX(-18deg);
  background:linear-gradient(90deg,transparent,rgba(255,255,255,.07) 35%,rgba(255,255,255,.14) 50%,rgba(255,255,255,.07) 65%,transparent)}
`;
function mountGlass(ctx) {
  ctx.css(GLASS_CSS);
  const pane = ctx.el('div', '', null, 'fx');
  pane.className = 'ft-glass';
  pane.setAttribute('aria-hidden', 'true');
  pane.dataset.fateGlass = '';
  for (const c of ['a1', 'a2', 'a3']) ctx.el('i', '', null, pane).className = c;
  const sheen = ctx.el('i', '', null, pane);
  sheen.className = 'sheen';
  function pass() {
    const { w } = ctx.bounds();
    if (calm(ctx)) {
      const at = `translateX(${Math.round(w * (0.18 + 0.2 * rnd()))}px) skewX(-18deg)`;
      ctx.anim(sheen, [{ opacity: 0, transform: at }, { opacity: 0.8, transform: at, offset: 0.5 }, { opacity: 0, transform: at }], { duration: GLASS.calmMs, easing: 'ease-in-out' });
    } else {
      ctx.anim(sheen, [
        { opacity: 0, transform: 'translateX(-20vmax) skewX(-18deg)' },
        { opacity: 1, offset: 0.2 },
        { opacity: 1, offset: 0.8 },
        { opacity: 0, transform: `translateX(${w + 40}px) skewX(-18deg)` },
      ], { duration: GLASS.sweepMs, easing: 'cubic-bezier(.45,.05,.4,1)' });
    }
    ctx.after(nextSheenMs(rnd), pass);
  }
  ctx.after(nextSheenMs(rnd, true), pass);
  return {};
}

export default {
  ft_depth: { mount: mountDepth },
  ft_cursor: { mount: mountCursor },
  ft_pressed: { mount: mountPressed },
  ft_glass: { mount: mountGlass },
};
