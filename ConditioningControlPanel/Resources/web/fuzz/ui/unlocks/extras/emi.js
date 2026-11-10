import {drawFace, animateEmiFace, FACE_CSS} from './emi-face.js';
export {drawFace} from './emi-face.js';
/* ui/unlocks/extras/emi.js - EMI herself: the art in assets/img/emi with a face drawn on her screen, one per page.
 * Whoever needs her first (em_head, or Watched on its way in) brings her; every other EMI unlock talks to the same one. */

import { HOLD_SLOP, railWidth } from '../../dock.js';
import { FONT, h } from './kit.js';

const POSE = { happy: 'body-idle', glee: 'body-pet', annoyed: 'body-smug', rage: 'body-shock', sleep: 'body-sad' };
const SWAY = ['body-sway1', 'body-sway2', 'body-sway3', 'body-sway4'];
const art = (name) => `assets/img/emi/${name}.png`;
/* She swaps pose every 420 ms. A host that sends no-store would refetch the png on every swap (60 s of sway = 1.4 MB a frame),
 * so each pose is fetched once and kept as a blob url; until it lands the plain path stands in. */
const frames = new Map();
const frame = (name) => {
  if (!frames.has(name)) {
    frames.set(name, art(name));
    fetch(art(name)).then(r => (r.ok ? r.blob() : null)).then(b => { if (b) frames.set(name, URL.createObjectURL(b)); }).catch(() => {});
  }
  return frames.get(name);
};

const CSS = `
.xe{position:absolute;left:0;top:0;transform-origin:0 0;pointer-events:auto;touch-action:none;user-select:none;-webkit-user-select:none;cursor:grab;will-change:transform;}
.xe[data-held]{cursor:grabbing;}
.xe-sprite{position:absolute;inset:0;transform-origin:50% 85%;pointer-events:none;}
.xe-sprite>img{position:absolute;inset:0;width:100%;height:100%;image-rendering:pixelated;pointer-events:none;}
${FACE_CSS}
.xe-h{position:absolute;left:0;right:0;bottom:79%;text-align:center;line-height:1;font-family:"Segoe UI Emoji","Apple Color Emoji","Noto Color Emoji",sans-serif;pointer-events:auto;}
.xe-s{position:absolute;z-index:4;width:max-content;max-width:min(230px,70vw);padding:4px 8px;background:#fff;color:#111;border:1px solid #333;border-radius:7px;font:600 14px/1.2 ${FONT};pointer-events:none;box-shadow:1px 2px 0 rgba(0,0,0,.25);}
.xe-c{position:absolute;display:flex;gap:4px;pointer-events:auto;}
.xe .xe-c,.xe .xe-s,.xe .xe-t{left:0;}
.xe[data-align-right] :is(.xe-c,.xe-s,.xe-t){left:auto;right:0;}
.xe-c{top:100%;}
.xe[data-above] .xe-c{top:auto;bottom:100%;}
.xe-s,.xe-t{top:calc(100% + var(--emi-gap,4px));transform-origin:left top;}
.xe[data-above] :is(.xe-s,.xe-t){top:auto;bottom:calc(100% + var(--emi-gap,4px));transform-origin:left bottom;}
.xe[data-align-right] .xe-s{transform-origin:right top;}
.xe[data-align-right][data-above] .xe-s{transform-origin:right bottom;}
.xe-t{--emi-gap:34px;}
.xe-b{min-height:30px;padding:2px 8px;background:#fff;color:#111;border:1px solid #333;border-radius:5px;font:600 13px ${FONT};touch-action:manipulation;white-space:nowrap;}
.xe-t{position:absolute;left:0;font:600 12px ${FONT};color:var(--ink-soft);white-space:nowrap;pointer-events:none;}
.xe[data-borrowed]{pointer-events:none;cursor:auto;}
.xe[data-borrowed]>:is(.xe-sprite,.xe-c,.xe-s,.xe-t){visibility:hidden!important;pointer-events:none!important;}
`;

let emi = null;

/** The one EMI. A later caller gets the same one. Her elements belong to the scope that brought her. */
export function getEmi(ctx) {
  if (emi?.el.isConnected) return emi;
  ctx.css(CSS);
  const wide = ctx.bounds().w >= 1040;
  const baseSize = wide ? 104 : 66;
  let size = baseSize;
  const el = ctx.el('div', `width:${size}px;height:${size}px;z-index:3;font-size:${size / 36}px;`, null, ctx.layer('over'));
  el.className = 'xe';
  el.dataset.emi = '1';
  const sprite = h('div', '', null, el);
  sprite.className = 'xe-sprite';
  const img = h('img', '', null, sprite);
  img.alt = 'EMI';
  img.src = art('body');
  img.draggable = false;
  const face = drawFace(sprite, 'happy');
  animateEmiFace(ctx, face, el);
  const hat = h('div', `font-size:${Math.round(size * 0.42)}px;`, '', sprite);
  hat.className = 'xe-h';
  const controls = h('div', '', null, el);
  controls.className = 'xe-c';
  for (const name of [...Object.values(POSE), ...SWAY, 'body']) frame(name);

  /* Home: bottom-left on a wide screen, tucked left of the counter on a phone. */
  let ctxs = ctx;
  const layoutKey = 'em_head:mascot';
  let resting = ctx.store.getLayout(layoutKey);
  const legacyQuiz = ctx.store.getLayout('quiz:_dock1');
  if (!Number.isFinite(resting.x) && !legacyQuiz.attached && Number.isFinite(legacyQuiz.x) && Number.isFinite(legacyQuiz.y)) {
    resting = { x: legacyQuiz.x + 8, y: legacyQuiz.y + 28 };
    ctx.store.setLayout(layoutKey, resting);
  }
  const placements = new Set();
  let grab = null;
  const fit = (x, y, scale = 1) => {
    const { w, h } = ctxs.bounds();
    const strip = el.ownerDocument.getElementById('dock-strip')?.offsetHeight || 0;
    return {
      x: Math.round(Math.max(0, Math.min(x, w - size * scale))),
      y: Math.round(Math.max(0, Math.min(y, h - strip - size * scale))),
    };
  };
  const home = () => {
    if (Number.isFinite(resting.x) && Number.isFinite(resting.y)) return fit(resting.x, resting.y);
    const { w, h } = ctxs.bounds();
    return wide ? fit(railWidth(w) + 126, h - size - 16) : fit(2, Math.max(38, ctxs.button.el.getBoundingClientRect().top - 4));
  };
  const pos = { ...home(), s: 1 };
  const place = () => {
    el.style.transform = `translate(${pos.x}px,${pos.y}px) scale(${pos.s})`;
    const { w, h } = ctxs.bounds();
    el.toggleAttribute('data-above', pos.y > (wide ? Math.min(130, h / 2) : h / 2));
    el.toggleAttribute('data-align-right', pos.x + size * pos.s / 2 > w / 2);
    for (const align of placements) align();
  };
  place();

  let mood = 'happy', moodUntil = 0, swayAt = 0, bubble = null, bubbleOff = null, busyPose = false;
  const setPose = (name) => { const url = frame(name); if (img.getAttribute('src') !== url) img.src = url; };
  let petBubble = null;
  const self = {
    el, sprite, img, wide, face, hat, controls, home, pos, place,
    get size() { return size; },
    setQuizScale(scale) {
      const next = Math.round(baseSize * scale);
      if (size === next) return;
      size = next;
      Object.assign(el.style, { width:`${size}px`, height:`${size}px`, fontSize:`${size / 36}px` });
      hat.style.fontSize = `${Math.round(size * .42)}px`;
      Object.assign(pos, self.napping || self.blocking || grab ? fit(pos.x, pos.y, pos.s) : home());
      place();
    },
    onPlace(fn) { placements.add(fn); return () => placements.delete(fn); },
    shift(dx, dy) {
      resting = fit(pos.x + dx, pos.y + dy);
      ctx.store.setLayout(layoutKey, resting);
      self.moveTo(resting.x, resting.y, 1, 0);
    },
    lastTouch: ctxs.state.clock,
    get held() { return grab !== null; },
    tapFns: new Set(),
    petAt(event, showLine = null) {
      self.lastTouch = ctxs.state.clock;
      petBubble = showLine;
      try { for (const fn of [...self.tapFns]) fn(event); }
      finally { petBubble = null; }
    },
    adopt(c) { ctxs = c; },
    /* Mood for ms, then back to happy. No ms: until the next mood call. */
    mood(m, ms = 0) {
      mood = m;
      moodUntil = ms ? ctxs.state.clock + ms : 0;
      face.dataset.m = m;
      setPose(POSE[m] || 'body');
    },
    get currentMood() { return mood; },
    /* The EMI Takeover borrows her: its main sprite stands where she stood and she is hidden (the sprite, her buttons
     * and bubbles; a question card anchored to her stays). While borrowed she says nothing and takes no grab or tap;
     * the lane features check `borrowed` and stand down. Released, she is back where she was. */
    borrowed: false,
    borrow(on) {
      on = !!on;
      if (self.borrowed === on) return;
      self.borrowed = on;
      el.toggleAttribute('data-borrowed', on);
      if (on) { cancel(); bubbleOff?.(); bubble?.remove(); bubble = null; return; }
      self.lastTouch = ctxs.state.clock;
      place();
    },
    say(text, ms = 3000) {
      if (self.borrowed) { petBubble?.(text); return; }
      bubbleOff?.();
      bubble?.remove();
      bubble = h('div', '', ctxs.brand.text(text), el);
      bubble.className = 'xe-s';
      const scale = 1 / (pos.s || 1);
      bubble.style.transform = `scale(${scale})`;
      bubble.style.setProperty('--emi-gap', controls.childElementCount && controls.style.display !== 'none' ? '34px' : '4px');
      const b = bubble;
      ctxs.anim(b, [{ opacity: 0, translate: '0 6px' }, { opacity: 1, translate: '0 0' }], { duration: 160, easing: 'ease-out' });
      bubbleOff = ctxs.after(ms, () => { b.remove(); if (bubble === b) bubble = null; });
    },
    /* Move in one go; the transition is the tween. */
    moveTo(x, y, s = 1, ms = 420) {
      Object.assign(pos, fit(x, y, s), {s});
      el.style.transition = ctxs.gentle ? 'none' : `transform ${ms}ms ease-in-out`;
      place();
    },
    goHome(ms) { if (!grab) { const p = home(); self.moveTo(p.x, p.y, 1, ms); } },
    wiggle() {
      if (ctxs.gentle) return;
      ctxs.anim(img, [{ transform: 'rotate(-5deg)' }, { transform: 'rotate(5deg)' }, { transform: 'none' }], { duration: 260 });
    },
    setBusy(v) { busyPose = v; },
    tick() {
      const t = ctxs.state.clock;
      if (moodUntil && t >= moodUntil) self.mood('happy');
      if (mood !== 'happy' || busyPose || ctxs.gentle || t < swayAt) return;
      swayAt = t + 420;
      setPose(SWAY[Math.floor(t / 420) % SWAY.length]);
    },
  };
  const boundsChange = () => {
    if (grab) return;
    if (self.napping || self.blocking) Object.assign(pos, fit(pos.x, pos.y, pos.s));
    else Object.assign(pos, home());
    place();
  };
  el.addEventListener('pointerdown', ev => {
    if (ctxs.paused || grab || self.borrowed || ev.button !== 0 || !ev.isPrimary || ev.target.closest('button,input,textarea,select,a,.xe-c,[data-dock-interactive]')) return;
    ev.preventDefault();
    const transform = new DOMMatrixReadOnly(getComputedStyle(el).transform);
    const world = new DOMMatrixReadOnly(getComputedStyle(ctx.world).transform);
    grab = {
      id: ev.pointerId, x: ev.clientX, y: ev.clientY, left: transform.e, top: transform.f,
      scale: transform.a, world: world.inverse(), previous: { ...pos }, moved: false,
      tapBody: !ev.target.closest('.xq-dock'),
    };
    self.lastTouch = ctxs.state.clock;
    el.setPointerCapture(ev.pointerId);
  });
  const track = ev => {
    if (!grab || ev.pointerId !== grab.id || ctxs.paused) return;
    const dx = ev.clientX - grab.x, dy = ev.clientY - grab.y;
    if (!grab.moved && Math.hypot(dx, dy) <= HOLD_SLOP) return;
    if (!grab.moved) {
      // A new grab can interrupt her walk in or her return from a nap.
      for (const animation of el.getAnimations()) animation.cancel();
      el.style.transition = 'none';
      el.setAttribute('data-held', '');
      grab.moved = true;
      pos.s = grab.scale;
    }
    const delta = new DOMPoint(dx, dy, 0, 0).matrixTransform(grab.world);
    Object.assign(pos, fit(grab.left + delta.x, grab.top + delta.y, pos.s));
    self.lastTouch = ctxs.state.clock;
    place();
  };
  const finish = ev => {
    if (!grab || (ev && ev.pointerId !== grab.id)) return;
    const released = ev?.type === 'pointerup' && !ctxs.paused;
    if (released) track(ev);
    const { id, moved, previous, tapBody } = grab;
    grab = null;
    el.removeAttribute('data-held');
    if (el.hasPointerCapture(id)) el.releasePointerCapture(id);
    if (moved) {
      if (released) {
        resting = fit(pos.x, pos.y);
        ctx.store.setLayout(layoutKey, resting);
      }
      if (!self.napping && !self.blocking) self.goHome(0);
      else if (!released) self.moveTo(previous.x, previous.y, previous.s, 0);
    } else if (released && tapBody) {
      self.petAt(ev);
    }
  };
  el.addEventListener('pointermove', track);
  for (const type of ['pointerup','pointercancel','lostpointercapture']) el.addEventListener(type, finish);
  const cancel = () => finish(null);
  const win = el.ownerDocument.defaultView;
  for (const type of ['pause','suspend','ending']) ctx.on(type, cancel);
  win.addEventListener('resize', boundsChange);
  win.addEventListener('blur', cancel);
  ctx.cleanup(() => {
    cancel();
    win.removeEventListener('resize', boundsChange);
    win.removeEventListener('blur', cancel);
  });
  el.addEventListener('contextmenu', (ev) => ev.preventDefault());
  emi = self;
  return self;
}

/** The EMI on the page, or null. Never brings her (her elements belong to the scope that did). */
export const liveEmi = () => (emi?.el.isConnected ? emi : null);

/** Register a tap handler that dies with this unlock. */
export function onEmiTap(ctx, e, fn) {
  e.tapFns.add(fn);
  return { off() { e.tapFns.delete(fn); } };
}

export { CSS as EMI_CSS };
