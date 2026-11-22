/* ui/main-menu.js - the main menu's surface (lane M, owner 2026-11-22; look, motion and timings from the approved mockup,
 * C:/wt-tce/notes/menu-mockup/src.html). The sticker sheet: a slow two-colour crayon spiral, the logo and a column of
 * die-cut sticker rows. Which rows and what they say is ui/menu-model.js; what a press does is the caller's (main.js
 * hands one function per row id, and asks for leave() / flash() itself). This file draws, moves and sounds.
 *
 *   Motion, every piece with an IN and an OUT:
 *     enter   the logo pops in, the rows slap down one by one from alternating sides (replayed on every return)
 *     idle    the logo's slow wiggle, the rows' small drift, the spiral turning (drawn once, then only rotated)
 *     select  the sticker lifts, straightens, its corner peels; press squashes it
 *     leave   the picked sticker bursts the game's star and heart sprites, the others peel off to the sides
 *     rattle  the sealed row shakes
 *   Gentle (body.gentle) and prefers-reduced-motion: fades only. No drift, no turn, no burst.
 *   Idle motion holds (animation-play-state) while the page is hidden or anything covers the menu.
 *   Sounds: the game's own files on its sfx bus (ui hands `sound(name)`), so its mute holds.
 *   Keyboard: arrows move, Home/End jump, Enter or Space presses. Escape is never handled here.
 *   A row that slides in under a resting mouse never selects itself: only a pointer that really moved does.
 *   Phone width: one column, no sideways scroll. */

import { menuKey } from './menu-model.js';
import { BRAND } from '../data/brand.js';

const THUD = 'cubic-bezier(.2,1.5,.4,1)';
const STAR = 'assets/adcard/sprite-star.webp', HEART = 'assets/adcard/sprite-heart.webp';
const TINTS = ['#ff4fa8', '#7b5cff', '#ffd23f', '#1b1a17'];

const CSS = `
/* The game's own faces: ShopRound (styles.css, Fredoka) and RrMono (ui/recap/recap.css, Courier Prime), same files. */
@font-face { font-family: RrMono; src: url("assets/fonts/courier-prime-700-latin.woff2") format("woff2"); font-weight: 700; font-display: swap; }
body.mm-on #game-controls { visibility: hidden; pointer-events: none; }   /* lock, Depth badge, audio, Wake Up: not on the menu */
#main-menu { position: fixed; inset: 0; z-index: 88; overflow: hidden; background: #f7f5ef; color: #1b1a17; user-select: none; -webkit-user-select: none; isolation: isolate; }
#main-menu[hidden] { display: none; }
#main-menu .mm-bg, #main-menu .mm-fx { position: absolute; inset: 0; pointer-events: none; }
#main-menu .mm-bg { z-index: 0; }
/* The collection either side: the Sticker Book's own cut-out look (ui/sticker-book.js .sb-stk), loose on the sheet. A place
   is clamped inside the screen; one that would sit under the logo or the rows is left out (fit()). */
#main-menu .mm-sides { position: absolute; inset: 0; z-index: 2; pointer-events: none; }
#main-menu .sd { --k: 1; --w: calc(var(--s) * var(--k) * 1px); position: absolute; width: var(--w); aspect-ratio: 1; translate: -50% -50%; rotate: calc(var(--r) * 1deg); pointer-events: auto;
  left: clamp(calc(var(--w) / 2), calc(var(--x) * 1%), calc(100% - var(--w) / 2)); top: clamp(calc(var(--w) / 2), calc(var(--y) * 1%), calc(100% - var(--w) / 2));
  transition: scale .2s ${THUD}, rotate .25s ${THUD}; }
#main-menu .sd[hidden] { display: none; }
#main-menu .sd:hover { scale: 1.1; rotate: calc(var(--r) * -.35deg); z-index: 1; }
#main-menu .sd > span { display: grid; place-items: center; width: 100%; height: 100%; animation: mm-drift 6s ease-in-out infinite; animation-delay: calc(var(--i) * -.83s); }
#main-menu .sd img { max-width: 92%; max-height: 92%; object-fit: contain; filter: drop-shadow(2.5px 0 0 #fff) drop-shadow(-2.5px 0 0 #fff) drop-shadow(0 2.5px 0 #fff) drop-shadow(0 -2.5px 0 #fff) drop-shadow(2px 4px 0 rgba(0, 0, 0, .15)); }
#main-menu .sd.round img, #main-menu .sd.ticket img { width: 88%; height: 88%; max-width: none; max-height: none; object-fit: cover; border: 4px solid #fff; background: #fff; filter: drop-shadow(2px 4px 0 rgba(0, 0, 0, .2)); }
#main-menu .sd.round img { border-radius: 50%; object-position: 50% 28%; }
#main-menu .sd.ticket img { border-radius: 8px; }
#main-menu .sd.slot > span { border: 2px dashed #b9ae9a; border-radius: 14px; background: rgba(247, 245, 239, .8); animation: none; }
#main-menu .sd.slot img { max-width: 66%; max-height: 66%; width: auto; height: auto; border: 0; background: transparent; filter: brightness(0); opacity: .13; }
#main-menu .sd.slot:hover { scale: 1.04; }
#main-menu .mm-fx { z-index: 5; }
#main-menu .mm-spiral { display: block; position: absolute; left: 50%; top: 50%; width: max(140%, 900px); aspect-ratio: 1; translate: -50% -50%; opacity: .62; animation: mm-spin 140s linear infinite; }
#main-menu .mm-sheet { position: absolute; inset: 0; z-index: 3; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 14px; padding: 16px; overflow-x: hidden; overflow-y: auto; }
#main-menu .mm-logo { display: block; width: min(440px, 88%); height: auto; aspect-ratio: 15 / 4; margin: 0 auto; filter: drop-shadow(2px 4px 0 rgba(0, 0, 0, .22)); animation: mm-wig 4.6s ease-in-out infinite; }
#main-menu .mm-rows { list-style: none; margin: 0; padding: 0; display: grid; gap: 9px; justify-items: center; }
#main-menu .mm-rows li { animation: mm-drift 5s ease-in-out infinite; animation-delay: calc(var(--i) * -.7s); }
#main-menu .mm-row { --tilt: -1.6deg; --dx: -24px; --fill: #1b1a17; --on: #fff; appearance: none; margin: 0; cursor: pointer; touch-action: manipulation; -webkit-tap-highlight-color: transparent;
  position: relative; display: flex; align-items: center; gap: 10px; min-height: 43px; padding: 6px 21px; border: 0; border-radius: 999px; background: var(--fill); color: var(--on);
  font: 600 20px/1.1 ShopRound, "Arial Rounded MT Bold", "Trebuchet MS", sans-serif; box-shadow: 0 0 0 4px #fff, 2px 5px 0 4px rgba(0, 0, 0, .22); translate: var(--dx) 0; rotate: var(--tilt);
  transition: translate .2s ${THUD}, rotate .2s ${THUD}, scale .2s ${THUD}, box-shadow .2s, filter .25s; }
#main-menu .mm-rows li:nth-child(even) .mm-row { --tilt: 1.4deg; --dx: 24px; }
#main-menu .mm-rows li:nth-child(4n+2) .mm-row { --fill: #ff4fa8; --on: #2a0a1c; }
#main-menu .mm-rows li:nth-child(4n+3) .mm-row { --fill: #7b5cff; }
#main-menu .mm-rows li:nth-child(4n) .mm-row { --fill: #fffdf7; --on: #1b1a17; }
#main-menu .mm-row:focus-visible { outline: 2px solid #d42f86; outline-offset: 2px; }
#main-menu .mm-rows:has(.sel) .mm-row:not(.sel) { filter: saturate(.9) brightness(.98); }
#main-menu .mm-row.sel { rotate: 0deg; scale: 1.09; translate: var(--dx) -3px; box-shadow: 0 0 0 4px #fff, 5px 11px 0 4px rgba(0, 0, 0, .18); z-index: 2; }
#main-menu .mm-row:active, #main-menu .mm-row.down { scale: .95; translate: var(--dx) 2px; box-shadow: 0 0 0 4px #fff, 1px 2px 0 4px rgba(0, 0, 0, .24); transition-duration: .06s; }
#main-menu .mm-peel { position: absolute; right: 3px; top: -4px; width: 20px; height: 20px; background: linear-gradient(225deg, transparent 50%, #fff 50%); border-radius: 0 0 0 7px; filter: drop-shadow(-1px 2px 1px rgba(0, 0, 0, .25)); scale: 0; transform-origin: top right; transition: scale .18s ${THUD}; pointer-events: none; }
#main-menu .mm-row.sel .mm-peel { scale: 1; }
#main-menu .mm-meta, #main-menu .mm-tag { white-space: nowrap; }
#main-menu .mm-meta { padding: 1px 8px; border-radius: 99px; background: color-mix(in srgb, var(--on) 16%, transparent); font: 600 12px ShopRound, "Trebuchet MS", sans-serif; font-variant-numeric: tabular-nums; }
#main-menu .mm-tag { padding: 2px 7px; border-radius: 4px; background: #fff; color: #a3232b; font: 700 10.5px RrMono, "Courier New", monospace; letter-spacing: .04em; rotate: -3deg; }
#main-menu .k-wait .mm-tag { animation: mm-wait 1.3s linear infinite; }
#main-menu .mm-rows li .mm-row.k-door { --fill: #b4b0a6; --on: #1b1a17; }
#main-menu .k-door .mm-tag { color: #444; rotate: 0deg; font: 600 11px ShopRound, "Trebuchet MS", sans-serif; }
#main-menu .mm-rows li .mm-row.k-ready { --fill: #ffd23f; --on: #1b1a17; }
#main-menu .mm-rows li .mm-row.k-sealed { min-width: 214px; justify-content: center; background: rgba(247, 245, 239, .78); color: #5c5a52; box-shadow: none; outline: 3px dashed #5c5a52; outline-offset: 1px; }
#main-menu .mm-rows li .mm-row.k-sealed:focus-visible { outline-color: #d42f86; }
#main-menu .k-sealed::before { content: "?"; font-size: 22px; }
#main-menu .k-sealed .mm-peel { display: none; }
#main-menu .mm-conf { position: absolute; left: 0; top: 0; width: 18px; height: 18px; margin: -9px 0 0 -9px; }
#main-menu i.mm-conf { width: 10px; height: 10px; margin: -5px 0 0 -5px; border-radius: 50%; }
#main-menu.mm-still *, #main-menu.mm-still { animation-play-state: paused !important; }
@keyframes mm-spin { to { rotate: 360deg; } }
@keyframes mm-wait { 0%, 55% { opacity: 1; } 56%, 100% { opacity: .15; } }
@keyframes mm-drift { 0%, 100% { rotate: -.6deg; translate: 0 0; } 50% { rotate: .6deg; translate: 0 -1.5px; } }
@keyframes mm-wig { 0%, 100% { rotate: -1.4deg; } 50% { rotate: 1.2deg; } }
@media (max-width: 900px) { #main-menu .sd { --k: .8; } }
@media (max-width: 620px) {
  /* A phone: eight small ones, tucked half off the edges and behind the rows. */
  #main-menu .sd { --k: .62; left: calc(var(--mx) * 1%); top: calc(var(--my) * 1%); }
  #main-menu .sd:not(.ph) { display: none; }
  #main-menu .mm-row { --dx: -9px; font-size: 17px; padding-inline: 16px; min-height: 42px; }
  #main-menu .mm-rows li:nth-child(even) .mm-row { --dx: 9px; }
}
@media (prefers-reduced-motion: reduce) {
  #main-menu .mm-logo, #main-menu .mm-spiral, #main-menu .mm-rows li, #main-menu .mm-tag { animation: none !important; }
  #main-menu .mm-row, #main-menu .mm-peel { transition: opacity .15s linear, background-color .15s linear !important; }
}
#main-menu.mm-calm .sd > span { animation: none !important; }
#main-menu.mm-calm .sd { transition: none !important; }
body.gentle #main-menu .mm-logo, body.gentle #main-menu .mm-spiral, body.gentle #main-menu .mm-rows li, body.gentle #main-menu .mm-tag { animation: none !important; }
body.gentle #main-menu .mm-row, body.gentle #main-menu .mm-peel { transition: opacity .15s linear, background-color .15s linear !important; }
`;

/** The spiral: two crayon arms in the logo's pink and violet, drawn once. */
function drawSpiral(c) {
  const n = 900, g = c.getContext?.('2d');
  c.width = c.height = n;
  if (!g) return;
  g.translate(n / 2, n / 2); g.lineCap = 'round'; g.lineJoin = 'round';
  ['#ff69b4', '#7b5cff'].forEach((col, k) => {
    for (let pass = 0; pass < 3; pass++) {
      g.beginPath();
      for (let t = 0; t <= Math.PI * 2 * 5.4; t += .04) {
        const r = 4 + t * 13.2 + (pass ? Math.sin(t * 9 + pass * 2) * 2.2 : 0), a = t + k * Math.PI;
        if (t) g.lineTo(Math.cos(a) * r, Math.sin(a) * r); else g.moveTo(Math.cos(a) * r, Math.sin(a) * r);
      }
      g.strokeStyle = col; g.globalAlpha = pass ? .4 : .85; g.lineWidth = pass ? 15 - pass * 3 : 11; g.stroke();
    }
  });
}

/**
 * Opens the menu. rows: () => the model's rows (asked again on every entrance). actions: { [rowId]: (ev, el, row) }.
 * gentle: () => bool. sound: (name) => void, names 'land' | 'tick' | 'press' | 'burst' | 'denied'.
 * sides: () => [{ kind: 'got'|'slot'|'portrait', src, cls, spot, side }] (ui/menu-model.js sideStickers, art resolved).
 * Answers { el, open, refresh(), rattle(id), flash(id), leave(id, { back }), hide(), show(), focus(), close() }.
 */
export function openMainMenu({ doc = document, rows, actions = {}, gentle = () => false, sound = () => {}, sides = () => [], logo = 'assets/adcard/logo.webp' }) {
  const win = doc.defaultView;
  if (!doc.getElementById('main-menu-css')) { const st = doc.createElement('style'); st.id = 'main-menu-css'; st.textContent = CSS; doc.head.appendChild(st); }
  const calm = () => !!gentle() || !!win?.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
  const mk = (tag, cls, parent) => { const e = doc.createElement(tag); if (cls) e.className = cls; parent?.appendChild(e); return e; };

  const root = mk('nav'); root.id = 'main-menu';
  const bg = mk('div', 'mm-bg', root); bg.setAttribute('aria-hidden', 'true');
  const spiral = mk('canvas', 'mm-spiral', bg);
  const sheet = mk('div', 'mm-sheet', root);
  const img = mk('img', 'mm-logo', sheet); img.src = logo; img.alt = BRAND.game; img.width = 904; img.height = 240;
  const list = mk('ul', 'mm-rows', sheet);
  const fx = mk('div', 'mm-fx', root); fx.setAttribute('aria-hidden', 'true');
  const sideLayer = mk('div', 'mm-sides'); sideLayer.setAttribute('aria-hidden', 'true');
  root.insertBefore(sideLayer, sheet);
  let sideEls = [];
  doc.body.appendChild(root);
  doc.body.classList.add('mm-on');
  drawSpiral(spiral);

  let model = [], els = [], sel = 0, open = true, away = false, busy = false, token = 0, anims = [], timers = [];
  let px = -1, py = -1, moved = false;
  const onMove = (e) => { moved = e.clientX !== px || e.clientY !== py; px = e.clientX; py = e.clientY; };
  doc.addEventListener('pointermove', onMove, true);

  /* ---- motion helpers: calm keeps only the fades ---- */
  function an(el, frames, o) {
    if (!el?.animate) return null;
    if (calm()) {
      const a = frames[0].opacity, b = frames[frames.length - 1].opacity;
      if (a === undefined && b === undefined) return null;
      frames = [{ opacity: a ?? 1 }, { opacity: b ?? 1 }];
      o = { ...o, duration: Math.min(o.duration || 200, 220), delay: Math.min(o.delay || 0, 160), easing: 'linear' };
    }
    const x = el.animate(frames, o); anims.push(x); return x;
  }
  const done = (x) => (x ? x.finished.then(() => {}, () => {}) : Promise.resolve());
  const later = (ms, fn) => { timers.push(win.setTimeout(fn, calm() ? Math.min(ms, 160) : ms)); };
  function clearFx() {
    for (const x of anims) { try { x.cancel(); } catch { /* gone */ } }
    anims = [];
    for (const t of timers) win.clearTimeout(t);
    timers = [];
    fx.replaceChildren();
  }
  const quiet = (name) => { if (!busy) sound(name); };

  /* ---- the rows ---- */
  function select(i, silent = false) {
    if (i === sel && els[i]?.classList.contains('sel')) return;
    els[sel]?.classList.remove('sel'); sel = i; els[sel]?.classList.add('sel');
    if (!silent) quiet('tick');
  }
  /* ---- the collection: not a door, not focusable; it wiggles and nothing else ---- */
  function wiggle(d) { an(d.firstChild, [{ transform: 'rotate(0deg)' }, { transform: 'rotate(-8deg)' }, { transform: 'rotate(6deg)' }, { transform: 'rotate(-3deg)' }, { transform: 'rotate(0deg)' }], { duration: 340, easing: 'ease-out' }); }
  function drawSides() {
    sideLayer.replaceChildren(); sideEls = [];
    sides().forEach((it, i) => {
      const d = mk('div', `sd ${it.side}${it.kind === 'slot' ? ' slot' : ''}${it.cls ? ' ' + it.cls : ''}${it.spot.m ? ' ph' : ''}`, sideLayer);
      const p = it.spot;
      d.style.cssText = `--x:${p.x};--y:${p.y};--s:${p.s};--r:${p.r};--i:${i}` + (p.m ? `;--mx:${p.m[0]};--my:${p.m[1]}` : '');
      const im = mk('img', '', mk('span', '', d));
      im.alt = ''; im.decoding = 'async'; im.loading = 'lazy'; im.draggable = false; im.src = it.src;
      d.addEventListener('pointerenter', () => { if (moved && !busy) wiggle(d); });
      d.addEventListener('click', () => { if (!busy) { wiggle(d); sound('tick'); } });
      sideEls.push(d);
    });
    fit();
  }
  /* Leave out any place that would sit under the logo or the rows (a narrow window). Read once per draw and resize. */
  function fit() {
    if (!sideEls.length || win.innerWidth <= 620) { for (const d of sideEls) d.hidden = false; return; }
    const boxes = [img.getBoundingClientRect(), list.getBoundingClientRect()].map((r) => ({ l: r.left - 34, r: r.right + 34, t: r.top - 8, b: r.bottom + 8 }));
    for (const d of sideEls) {
      d.hidden = false;
      const r = d.getBoundingClientRect();
      d.hidden = boxes.some((q) => r.left < q.r && r.right > q.l && r.top < q.b && r.bottom > q.t);
    }
  }
  let fitTimer = 0;
  const onResize = () => { win.clearTimeout(fitTimer); fitTimer = win.setTimeout(fit, 150); };
  win.addEventListener('resize', onResize);

  function draw() {
    model = rows();
    list.replaceChildren(); els = [];
    model.forEach((r, i) => {
      const li = mk('li', '', list), b = mk('button', 'mm-row' + (r.kind ? ` k-${r.kind}` : ''), li);
      li.style.setProperty('--i', i);
      b.type = 'button'; b.dataset.row = r.id;
      if (r.label) mk('span', 'mm-lab', b).textContent = r.label;
      if (r.meta) mk('span', 'mm-meta', b).textContent = r.meta;
      if (r.tag) mk('span', 'mm-tag', b).textContent = r.tag;
      mk('span', 'mm-peel', b).setAttribute('aria-hidden', 'true');
      if (r.aria) b.setAttribute('aria-label', r.aria);
      /* Only a pointer that really moved selects: a row sliding in under a resting mouse never steals the keyboard's place. */
      b.addEventListener('pointermove', () => { if (moved && !busy) select(i); });
      b.addEventListener('focus', () => select(i, true));
      b.addEventListener('click', (ev) => press(i, ev));
      els.push(b);
    });
    sel = Math.max(0, Math.min(sel, els.length - 1));
    els[sel]?.classList.add('sel');
    root.classList.toggle('mm-calm', calm());
    drawSides();
  }
  function press(i, ev) {
    if (!open || away || busy) return;
    const r = model[i];
    select(i, true);
    const fn = actions[r?.id];
    if (fn) fn(ev, els[i], r); else rattle(r?.id);
  }
  const rowOf = (id) => els[model.findIndex((r) => r.id === id)];
  function flash(id) {
    const el = rowOf(id); if (!el) return;
    sound('press');
    el.classList.add('down'); win.setTimeout(() => el.classList.remove('down'), 120);
  }
  function rattle(id) {
    const el = rowOf(id); if (!el) return;
    sound('denied');
    if (calm()) an(el, [{ opacity: .4 }, { opacity: 1 }], { duration: 200 });
    else an(el, [{ transform: 'translateX(0)' }, { transform: 'translateX(-7px)' }, { transform: 'translateX(6px)' }, { transform: 'translateX(-3px)' }, { transform: 'translateX(0)' }], { duration: 280, easing: 'ease-out' });
  }

  /* ---- arrive: the logo pops, the rows slap down one by one from alternating sides ---- */
  async function enter() {
    const my = ++token;
    busy = true;
    clearFx();
    let last = an(img, [{ transform: 'scale(0) rotate(-16deg)', opacity: 0 }, { transform: 'scale(1) rotate(0deg)', opacity: 1 }], { duration: 430, easing: THUD, fill: 'backwards' });
    later(140, () => sound('land'));
    els.forEach((r, i) => {
      const g = i % 2 ? 1 : -1;
      last = an(r, [{ transform: `translate(${g * 130}px,-46px) scale(1.55) rotate(${g * 19}deg)`, opacity: 0 }, { transform: 'translate(0,0) scale(1) rotate(0deg)', opacity: 1 }],
        { duration: 300, delay: 300 + i * 70, easing: THUD, fill: 'backwards' }) || last;
      later(430 + i * 70, () => sound('land'));
    });
    sideEls.forEach((d, i) => {
      if (d.hidden) return;
      const g = d.classList.contains('lf') ? -1 : 1;
      const x = an(d, [{ transform: `translate(${-g * 60}px,-30px) scale(1.8) rotate(${g * 26}deg)`, opacity: 0 }, { transform: 'translate(0,0) scale(1) rotate(0deg)', opacity: 1 }],
        { duration: 260, delay: 480 + i * 30, easing: THUD, fill: 'backwards' });
      if (i % 3 === 0) later(600 + i * 30, () => sound('land'));
      if (i > 12) last = x || last;
    });
    await done(last);
    if (my === token) busy = false;
  }

  /* ---- leave: the picked sticker bursts, everything else peels off toward its own side ---- */
  function burst(el, n = 18) {
    if (calm()) return;
    const a = el.getBoundingClientRect(), s = root.getBoundingClientRect(), cx = a.left - s.left + a.width / 2, cy = a.top - s.top + a.height / 2;
    for (let i = 0; i < n; i++) {
      let p;
      if (i % 3 === 2) { p = mk('i', 'mm-conf', fx); p.style.background = TINTS[i % 4]; } else { p = mk('img', 'mm-conf', fx); p.alt = ''; p.src = i % 3 ? HEART : STAR; }
      const ang = Math.random() * Math.PI * 2, d = 60 + Math.random() * 90, dx = Math.cos(ang) * d, dy = Math.sin(ang) * d * .7 - 30, rz = (Math.random() - .5) * 320;
      const x = p.animate([{ transform: `translate(${cx}px,${cy}px) scale(.3)`, opacity: 1 }, { transform: `translate(${cx + dx * .8}px,${cy + dy}px) scale(1.1) rotate(${rz / 2}deg)`, opacity: 1, offset: .5 },
        { transform: `translate(${cx + dx}px,${cy + dy + 70}px) scale(.7) rotate(${rz}deg)`, opacity: 0 }], { duration: 620 + Math.random() * 280, easing: 'cubic-bezier(.15,.7,.3,1)' });
      anims.push(x);
      x.onfinish = () => p.remove();
    }
  }
  /** The leave, then the door. back: the door is a sheet or card over the menu, so the entrance replays when it goes. */
  async function leave(id, { back = false } = {}) {
    const el = rowOf(id);
    if (!el || busy) return;
    const my = ++token;
    busy = true;
    el.classList.add('down'); win.setTimeout(() => el.classList.remove('down'), 120);
    sound('burst'); burst(el);
    an(img, [{ transform: 'scale(1)', opacity: 1 }, { transform: 'scale(.4) rotate(12deg)', opacity: 0 }], { duration: 260, delay: 120, easing: 'ease-in', fill: 'forwards' });
    els.forEach((r, i) => {
      if (r === el) return;
      const g = i % 2 ? 1 : -1;
      an(r, [{ transform: 'translate(0,0) rotate(0deg)', opacity: 1 }, { transform: `translate(${g * (180 + Math.random() * 140)}px,${(Math.random() - .5) * 240}px) rotate(${(Math.random() - .5) * 100}deg)`, opacity: 0 }],
        { duration: 300, delay: 60 + i * 28, easing: 'cubic-bezier(.5,0,.9,.5)', fill: 'forwards' });
    });
    sideEls.forEach((d, i) => {
      if (d.hidden) return;
      const g = d.classList.contains('lf') ? -1 : 1;
      an(d, [{ transform: 'translate(0,0) rotate(0deg)', opacity: 1 }, { transform: `translate(${g * (240 + Math.random() * 160)}px,${(Math.random() - .5) * 180}px) rotate(${g * (20 + Math.random() * 40)}deg)`, opacity: 0 }],
        { duration: 300, delay: i * 12, easing: 'cubic-bezier(.5,0,.9,.5)', fill: 'forwards' });
    });
    await done(an(el, [{ transform: 'scale(1)', opacity: 1 }, { transform: 'scale(1.28)', opacity: 1, offset: .45 }, { transform: 'scale(.2)', opacity: 0 }], { duration: 420, delay: 160, easing: 'ease-in-out', fill: 'forwards' }));
    if (my !== token) return;
    if (back) returnWhenUncovered(my);
  }
  /* A sheet or card the door opened: wait to see it (up to 1.5 s, the book loads its art first), then for it to go. */
  function returnWhenUncovered(my) {
    const t0 = win.performance.now();
    let seen = false;
    const look = () => {
      if (my !== token || !open || away) return;
      if (covered()) seen = true;
      else if (seen || win.performance.now() - t0 > 1500) { enter().then(() => els[sel]?.focus({ preventScroll: true })); return; }
      timers.push(win.setTimeout(look, 150));
    };
    look();
  }

  /* ---- cover and hidden: keys stand down, idle motion holds ---- */
  const covered = () => !!doc.querySelector('.backdrop:not([hidden]):not(.clear), #sticker-book:not([hidden]), #renewal-menu:not([hidden]), #ps-sky');
  const still = () => root.classList.toggle('mm-still', !!doc.hidden || covered());
  const holdTimer = win.setInterval(still, 400);
  doc.addEventListener('visibilitychange', still);

  function onKey(e) {
    if (!open || away || busy || e.altKey || e.ctrlKey || e.metaKey || covered()) return;
    const n = menuKey(e.key, sel, els.length);
    if (n !== null) { e.preventDefault(); select(n); els[n].focus({ preventScroll: true }); return; }
    if ((e.key === 'Enter' || e.key === ' ') && !e.target?.closest?.('#main-menu button')) { e.preventDefault(); press(sel, e); }
  }
  doc.addEventListener('keydown', onKey);

  draw();
  enter();
  els[sel]?.focus({ preventScroll: true });

  return {
    el: root,
    get open() { return open; },
    get busy() { return busy; },
    refresh() { if (open && !away) { draw(); els[sel]?.focus({ preventScroll: true }); } },
    rattle,
    flash,
    leave,
    /** Out of the way for a visit: hidden, the game's controls back (the Expo uses Wake Up and the pause card). */
    hide() { if (!open) return; ++token; clearFx(); away = true; busy = false; root.hidden = true; doc.body.classList.remove('mm-on'); },
    /** Back after the visit: the rows asked again and the entrance replayed, the keyboard's place kept. */
    show() {
      if (!open) return;
      away = false; root.hidden = false; doc.body.classList.add('mm-on');
      draw(); enter(); els[sel]?.focus({ preventScroll: true });
    },
    focus() { els[sel]?.focus({ preventScroll: true }); },
    /** The menu goes for good (Continue, New run): whatever leave() left is faded out, then removed. */
    close() {
      if (!open) return Promise.resolve();
      open = false;
      ++token;
      win.clearInterval(holdTimer);
      win.removeEventListener('resize', onResize);
      doc.removeEventListener('visibilitychange', still);
      doc.body.classList.remove('mm-on');
      doc.removeEventListener('keydown', onKey);
      doc.removeEventListener('pointermove', onMove, true);
      const a = root.animate?.([{ opacity: 1 }, { opacity: 0 }], { duration: calm() ? 160 : 200, easing: 'ease-in', fill: 'forwards' });
      const gone = () => { clearFx(); root.remove(); };
      return (a?.finished || Promise.resolve()).then(gone, gone);
    },
  };
}
