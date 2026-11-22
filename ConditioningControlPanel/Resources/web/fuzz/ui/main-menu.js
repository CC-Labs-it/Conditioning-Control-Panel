/* ui/main-menu.js - the main menu's surface (lane M slice M1, owner 2026-11-22). The sticker sheet: the logo and a column
 * of die-cut sticker rows. Which rows and what they say is ui/menu-model.js; what a press does is the caller's (main.js
 * hands one function per row id). This file only draws, moves the selection and fades in and out.
 *
 *   Keyboard: arrows move, Home/End jump, Enter or Space presses. Escape is never handled here: the game's own Escape
 *   (the pause card) and the host's kept-Escape run as on any other screen, and nothing on the menu is destructive.
 *   Gentle and prefers-reduced-motion: fades only (M1 has no other motion yet; the juice is slice M2).
 *   Phone width: one column, no sideways scroll. */

import { menuKey } from './menu-model.js';
import { BRAND } from '../data/brand.js';

const CSS = `
/* The game's own faces: ShopRound (styles.css, Fredoka) and RrMono (ui/recap/recap.css, Courier Prime), same files. */
@font-face { font-family: RrMono; src: url("assets/fonts/courier-prime-700-latin.woff2") format("woff2"); font-weight: 700; font-display: swap; }
body.mm-on #game-controls { visibility: hidden; pointer-events: none; }   /* lock, Depth badge, audio, Wake Up: not on the menu */
#main-menu { position: fixed; inset: 0; z-index: 88; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 14px;
  padding: 16px; overflow-x: hidden; overflow-y: auto; background: #f7f5ef; color: #1b1a17; user-select: none; -webkit-user-select: none; }
#main-menu[hidden] { display: none; }
#main-menu .mm-logo { display: block; width: min(440px, 88%); height: auto; aspect-ratio: 15 / 4; margin: 0 auto; filter: drop-shadow(2px 4px 0 rgba(0, 0, 0, .22)); }
#main-menu .mm-rows { list-style: none; margin: 0; padding: 0; display: grid; gap: 9px; justify-items: center; }
#main-menu .mm-row { --tilt: -1.6deg; --dx: -24px; --fill: #1b1a17; --on: #fff; appearance: none; margin: 0; cursor: pointer; touch-action: manipulation; -webkit-tap-highlight-color: transparent;
  position: relative; display: flex; align-items: center; gap: 10px; min-height: 43px; padding: 6px 21px; border: 0; border-radius: 999px; background: var(--fill); color: var(--on);
  font: 600 20px/1.1 ShopRound, "Arial Rounded MT Bold", "Trebuchet MS", sans-serif; box-shadow: 0 0 0 4px #fff, 2px 5px 0 4px rgba(0, 0, 0, .22); translate: var(--dx) 0; rotate: var(--tilt);
  transition: translate .2s cubic-bezier(.2, 1.5, .4, 1), rotate .2s cubic-bezier(.2, 1.5, .4, 1), scale .2s cubic-bezier(.2, 1.5, .4, 1), box-shadow .2s; }
#main-menu .mm-rows li:nth-child(even) .mm-row { --tilt: 1.4deg; --dx: 24px; }
#main-menu .mm-rows li:nth-child(4n+2) .mm-row { --fill: #ff4fa8; --on: #2a0a1c; }
#main-menu .mm-rows li:nth-child(4n+3) .mm-row { --fill: #7b5cff; }
#main-menu .mm-rows li:nth-child(4n) .mm-row { --fill: #fffdf7; --on: #1b1a17; }
#main-menu .mm-row:focus-visible { outline: 2px solid #d42f86; outline-offset: 2px; }
#main-menu .mm-row.sel { rotate: 0deg; scale: 1.09; translate: var(--dx) -3px; box-shadow: 0 0 0 4px #fff, 5px 11px 0 4px rgba(0, 0, 0, .18); z-index: 2; }
#main-menu .mm-row:active { scale: .95; transition-duration: .06s; }
#main-menu .mm-meta, #main-menu .mm-tag { white-space: nowrap; }
#main-menu .mm-meta { padding: 1px 8px; border-radius: 99px; background: color-mix(in srgb, var(--on) 16%, transparent); font: 600 12px ShopRound, "Trebuchet MS", sans-serif; font-variant-numeric: tabular-nums; }
#main-menu .mm-tag { padding: 2px 7px; border-radius: 4px; background: #fff; color: #a3232b; font: 700 10.5px RrMono, "Courier New", monospace; letter-spacing: .04em; rotate: -3deg; }
#main-menu .k-wait .mm-tag { animation: mm-wait 1.3s linear infinite; }
@keyframes mm-wait { 0%, 55% { opacity: 1; } 56%, 100% { opacity: .15; } }
#main-menu .mm-rows li .mm-row.k-door { --fill: #b4b0a6; --on: #1b1a17; }
#main-menu .k-door .mm-tag { color: #444; rotate: 0deg; font: 600 11px ShopRound, "Trebuchet MS", sans-serif; }
#main-menu .mm-rows li .mm-row.k-ready { --fill: #ffd23f; --on: #1b1a17; }
#main-menu .mm-rows li .mm-row.k-sealed { min-width: 214px; justify-content: center; background: rgba(247, 245, 239, .78); color: #5c5a52; box-shadow: none; outline: 3px dashed #5c5a52; outline-offset: 1px; }
#main-menu .mm-rows li .mm-row.k-sealed:focus-visible { outline-color: #d42f86; }
#main-menu .k-sealed::before { content: "?"; font-size: 22px; }
@media (max-width: 620px) {
  #main-menu .mm-row { --dx: -9px; font-size: 17px; padding-inline: 16px; min-height: 42px; }
  #main-menu .mm-rows li:nth-child(even) .mm-row { --dx: 9px; }
}
@media (prefers-reduced-motion: reduce) {
  #main-menu .mm-row { transition: opacity .15s linear, background-color .15s linear; }
  #main-menu .k-wait .mm-tag { animation: none; }
}
body.gentle #main-menu .mm-row { transition: opacity .15s linear, background-color .15s linear; }
body.gentle #main-menu .k-wait .mm-tag { animation: none; }
`;

/**
 * Opens the menu. rows: () => the model's rows (asked again on refresh). actions: { [rowId]: (ev, el) => void }.
 * gentle: () => bool. Answers { close(): Promise, refresh(), rattle(id), el, get open }.
 */
export function openMainMenu({ doc = document, rows, actions = {}, gentle = () => false, logo = 'assets/adcard/logo.webp' }) {
  const win = doc.defaultView;
  if (!doc.getElementById('main-menu-css')) { const st = doc.createElement('style'); st.id = 'main-menu-css'; st.textContent = CSS; doc.head.appendChild(st); }
  const calm = () => !!gentle() || !!win?.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
  const root = doc.createElement('nav');
  root.id = 'main-menu';
  const img = doc.createElement('img');
  img.className = 'mm-logo'; img.src = logo; img.alt = BRAND.game; img.width = 904; img.height = 240;
  const list = doc.createElement('ul');
  list.className = 'mm-rows';
  root.append(img, list);
  doc.body.appendChild(root);
  doc.body.classList.add('mm-on');

  let model = [], els = [], sel = 0, open = true, away = false;   // away: hidden while a visit (the Expo) is up
  let px = -1, py = -1, moved = false;
  const onMove = (e) => { moved = e.clientX !== px || e.clientY !== py; px = e.clientX; py = e.clientY; };
  doc.addEventListener('pointermove', onMove, true);

  function select(i) { els[sel]?.classList.remove('sel'); sel = i; els[sel]?.classList.add('sel'); }
  function draw() {
    model = rows();
    list.replaceChildren(); els = [];
    model.forEach((r, i) => {
      const li = doc.createElement('li'), b = doc.createElement('button');
      b.type = 'button'; b.className = 'mm-row' + (r.kind ? ` k-${r.kind}` : ''); b.dataset.row = r.id;
      if (r.label) { const s = doc.createElement('span'); s.className = 'mm-lab'; s.textContent = r.label; b.appendChild(s); }
      if (r.meta) { const s = doc.createElement('span'); s.className = 'mm-meta'; s.textContent = r.meta; b.appendChild(s); }
      if (r.tag) { const s = doc.createElement('span'); s.className = 'mm-tag'; s.textContent = r.tag; b.appendChild(s); }
      if (r.aria) b.setAttribute('aria-label', r.aria);
      /* Only a pointer that really moved selects: a row under a resting mouse never steals the keyboard's place. */
      b.addEventListener('pointermove', () => { if (moved) select(i); });
      b.addEventListener('focus', () => select(i));
      b.addEventListener('click', (ev) => press(i, ev));
      li.appendChild(b); list.appendChild(li); els.push(b);
    });
    sel = Math.min(sel, els.length - 1);
    els[sel]?.classList.add('sel');
  }
  function press(i, ev) {
    if (!open || away) return;
    const r = model[i];
    select(i);
    const fn = actions[r?.id];
    if (fn) fn(ev, els[i], r); else rattle(r?.id);
  }
  function rattle(id) {
    const el = els[model.findIndex((r) => r.id === id)];
    if (!el?.animate) return;
    if (calm()) el.animate([{ opacity: .4 }, { opacity: 1 }], { duration: 200 });
    else el.animate([{ transform: 'translateX(0)' }, { transform: 'translateX(-7px)' }, { transform: 'translateX(6px)' }, { transform: 'translateX(-3px)' }, { transform: 'translateX(0)' }], { duration: 280, easing: 'ease-out' });
  }
  /* Keys only while nothing sits above the menu (a card, the pause card, the Sticker Book): their own keys win. */
  const above = () => !!doc.querySelector('.backdrop:not([hidden]), #sticker-book:not([hidden]), #renewal-menu:not([hidden])');
  function onKey(e) {
    if (!open || away || e.altKey || e.ctrlKey || e.metaKey || above()) return;
    const n = menuKey(e.key, sel, els.length);
    if (n !== null) { e.preventDefault(); select(n); els[n].focus({ preventScroll: true }); return; }
    if ((e.key === 'Enter' || e.key === ' ') && !e.target?.closest?.('#main-menu button')) { e.preventDefault(); press(sel, e); }
  }
  doc.addEventListener('keydown', onKey);

  draw();
  root.animate?.([{ opacity: 0 }, { opacity: 1 }], { duration: calm() ? 200 : 260, easing: 'ease-out' });
  els[sel]?.focus({ preventScroll: true });

  return {
    el: root,
    get open() { return open; },
    refresh() { if (open) { draw(); els[sel]?.focus({ preventScroll: true }); } },
    rattle,
    /** Out of the way for a visit: hidden, the game's controls back (the Expo uses Wake Up and the pause card). */
    hide() { if (!open) return; away = true; root.hidden = true; doc.body.classList.remove('mm-on'); },
    /** Back after the visit: the rows asked again, a fade in, the keyboard's place kept. */
    show() {
      if (!open) return;
      away = false; root.hidden = false; doc.body.classList.add('mm-on'); draw();
      root.animate?.([{ opacity: 0 }, { opacity: 1 }], { duration: calm() ? 200 : 260, easing: 'ease-out' });
      els[sel]?.focus({ preventScroll: true });
    },
    focus() { els[sel]?.focus({ preventScroll: true }); },
    /** Plain leave: a fade, then the menu is gone. */
    close() {
      if (!open) return Promise.resolve();
      open = false;
      doc.body.classList.remove('mm-on');
      doc.removeEventListener('keydown', onKey);
      doc.removeEventListener('pointermove', onMove, true);
      const a = root.animate?.([{ opacity: 1 }, { opacity: 0 }], { duration: calm() ? 160 : 200, easing: 'ease-in', fill: 'forwards' });
      const gone = () => root.remove();
      return (a?.finished || Promise.resolve()).then(gone, gone);
    },
  };
}
