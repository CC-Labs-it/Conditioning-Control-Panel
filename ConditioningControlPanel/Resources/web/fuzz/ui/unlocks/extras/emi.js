/* ui/unlocks/extras/emi.js - EMI herself: the art in assets/img/emi with a face drawn on her screen, one per page.
 * Whoever needs her first (em_head, or Watched on its way in) brings her; every other EMI unlock talks to the same one. */

import { FONT, h } from './kit.js';

const POSE = { happy: 'body', glee: 'body-pet', annoyed: 'body-smug', rage: 'body-shock', sleep: 'body-sad' };
const SWAY = ['body-sway1', 'body-sway2', 'body-sway3', 'body-sway4'];
const art = (name) => `assets/img/emi/${name}.png`;

const CSS = `
.xe{position:absolute;left:0;top:0;pointer-events:auto;touch-action:manipulation;user-select:none;-webkit-user-select:none;cursor:pointer;will-change:transform;}
.xe img{position:absolute;inset:0;width:100%;height:100%;image-rendering:pixelated;pointer-events:none;}
.xe-f{position:absolute;left:28.5%;top:25%;width:44.5%;height:37%;pointer-events:none;}
.xe-f i{position:absolute;display:block;background:#ff7ac0;}
.xe-f .l,.xe-f .r{top:26%;width:15%;height:24%;border-radius:40%;}.xe-f .l{left:21%;}.xe-f .r{right:21%;}
.xe-f .m{left:32%;width:36%;top:62%;height:14%;background:transparent;border:0 solid #ff7ac0;border-width:0 .16em .16em;border-radius:0 0 50% 50%;box-sizing:border-box;}
.xe-f[data-m=glee] .l,.xe-f[data-m=glee] .r{background:transparent;border:solid #ff7ac0;border-width:.16em 0 0 .16em;transform:rotate(45deg);top:30%;border-radius:0;}
.xe-f[data-m=glee] .m{height:26%;background:#ff7ac0;border:0;top:56%;}
.xe-f[data-m=annoyed] .l,.xe-f[data-m=annoyed] .r{height:7%;top:36%;width:20%;border-radius:0;}.xe-f[data-m=annoyed] .m{height:6%;background:#ff7ac0;border:0;border-radius:0;top:66%;}
.xe-f[data-m=rage] i{background:#ff3b3b;}.xe-f[data-m=rage] .l{height:9%;width:24%;top:28%;border-radius:0;transform:rotate(24deg);}.xe-f[data-m=rage] .r{height:9%;width:24%;top:28%;border-radius:0;transform:rotate(-24deg);}
.xe-f[data-m=rage] .m{height:10%;background:#ff3b3b;border:0;border-radius:0;top:64%;width:44%;left:28%;}
.xe-f[data-m=sleep] .l,.xe-f[data-m=sleep] .r{height:6%;top:42%;width:20%;border-radius:0;background:#b95b8f;}.xe-f[data-m=sleep] .m{width:12%;left:44%;height:12%;background:#b95b8f;border:0;border-radius:50%;top:62%;}
.xe-h{position:absolute;left:0;right:0;bottom:79%;text-align:center;line-height:1;font-family:"Segoe UI Emoji","Apple Color Emoji","Noto Color Emoji",sans-serif;pointer-events:none;}
.xe-s{position:absolute;z-index:4;width:max-content;max-width:min(230px,70vw);padding:4px 8px;background:#fff;color:#111;border:1px solid #333;border-radius:7px;font:600 14px/1.2 ${FONT};pointer-events:none;box-shadow:1px 2px 0 rgba(0,0,0,.25);}
.xe-c{position:absolute;display:flex;gap:4px;left:0;pointer-events:auto;}
.xe-b{min-height:30px;padding:2px 8px;background:#fff;color:#111;border:1px solid #333;border-radius:5px;font:600 13px ${FONT};touch-action:manipulation;white-space:nowrap;}
.xe-t{position:absolute;left:0;font:600 12px ${FONT};color:var(--ink-soft);white-space:nowrap;pointer-events:none;}
`;

let emi = null;

/** Her face, for anything that shows her art at the size given by the parent's font-size. */
export function drawFace(parent, mood) {
  const face = h('div', '', null, parent);
  face.className = 'xe-f';
  face.dataset.m = mood;
  for (const c of ['l', 'r', 'm']) { const p = h('i', '', null, face); p.className = c; }
  return face;
}

/** The one EMI. A later caller gets the same one. Her elements belong to the scope that brought her. */
export function getEmi(ctx) {
  if (emi?.el.isConnected) { emi.adopt?.(ctx); return emi; }
  ctx.css(CSS);
  const wide = ctx.bounds().w >= 1040;
  const size = wide ? 104 : 66;
  const el = ctx.el('div', `width:${size}px;height:${size}px;z-index:3;font-size:${size / 36}px;`, null, ctx.layer('over'));
  el.className = 'xe';
  el.dataset.emi = '1';
  const img = h('img', '', null, el);
  img.alt = 'EMI';
  img.src = art('body');
  img.draggable = false;
  const face = drawFace(el, 'happy');
  const hat = h('div', `font-size:${Math.round(size * 0.42)}px;`, '', el);
  hat.className = 'xe-h';
  const controls = h('div', wide ? 'bottom:100%;' : 'top:100%;', null, el);
  controls.className = 'xe-c';
  for (const name of [...Object.values(POSE), ...SWAY]) { const p = new Image(); p.src = art(name); }

  /* Home: bottom-left on a wide screen, tucked left of the counter on a phone. */
  let ctxs = ctx;
  const home = () => { const b = ctxs.bounds(); return wide ? { x: 14, y: b.h - size - 16 } : { x: 2, y: 38 }; };
  const pos = { ...home(), s: 1 };
  const place = () => { el.style.transform = `translate(${pos.x}px,${pos.y}px) scale(${pos.s})`; };
  place();

  let mood = 'happy', moodUntil = 0, swayAt = 0, bubble = null, bubbleOff = null, busyPose = false;
  const setPose = (name) => { img.src = art(name); };
  const self = {
    el, size, wide, face, hat, controls, home, pos, place,
    lastTouch: ctxs.state.clock,
    tapFns: new Set(),
    adopt(c) { ctxs = c; },
    /* Mood for ms, then back to happy. No ms: until the next mood call. */
    mood(m, ms = 0) {
      mood = m;
      moodUntil = ms ? ctxs.state.clock + ms : 0;
      face.dataset.m = m;
      setPose(POSE[m] || 'body');
    },
    get currentMood() { return mood; },
    say(text, ms = 3000) {
      bubbleOff?.();
      bubble?.remove();
      bubble = h('div', '', ctxs.brand.text(text), el);
      bubble.className = 'xe-s';
      const scale = 1 / (pos.s || 1);
      bubble.style.transform = `scale(${scale})`;
      /* Above her in the bottom corner; below her, and below her buttons, near the top of the page. */
      const buttons = controls.childElementCount && controls.style.display !== 'none';
      if (self.wide && pos.y > 200) { bubble.style.left = '8px'; bubble.style.bottom = `calc(100% + ${buttons ? 34 : 4}px)`; bubble.style.transformOrigin = 'left bottom'; } else { bubble.style.left = '4px'; bubble.style.top = `calc(100% + ${buttons ? 32 : 0}px)`; bubble.style.transformOrigin = 'left top'; }
      const b = bubble;
      ctxs.anim(b, [{ opacity: 0, translate: '0 6px' }, { opacity: 1, translate: '0 0' }], { duration: 160, easing: 'ease-out' });
      bubbleOff = ctxs.after(ms, () => { b.remove(); if (bubble === b) bubble = null; });
    },
    /* Move in one go; the transition is the tween. */
    moveTo(x, y, s = 1, ms = 420) {
      pos.x = x; pos.y = y; pos.s = s;
      el.style.transition = ctxs.gentle ? 'none' : `transform ${ms}ms ease-in-out`;
      place();
    },
    goHome(ms) { const p = home(); self.moveTo(p.x, p.y, 1, ms); },
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
  el.addEventListener('pointerdown', (ev) => {
    if (ev.target.closest?.('.xe-c')) return;
    ev.preventDefault();
    self.lastTouch = ctxs.state.clock;
    for (const f of [...self.tapFns]) f(ev);
  });
  el.addEventListener('contextmenu', (ev) => ev.preventDefault());
  emi = self;
  return self;
}

/** Register a tap handler that dies with this unlock. */
export function onEmiTap(ctx, e, fn) {
  e.tapFns.add(fn);
  return { off() { e.tapFns.delete(fn); } };
}
