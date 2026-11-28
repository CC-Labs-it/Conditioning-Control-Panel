/* ui/legal.js - the window the contract, the update and the pictures card share: veil, title bar, dead X, toast, shake.
 * While one is up the world behind it is inert, so nothing there takes a press or a Tab. It hangs directly before Wake Up,
 * so Tab walks the window and then reaches Wake Up, which is never covered. */

import { BRAND, brandText } from '../data/brand.js';
import { richText } from './rich-text.js';
import { X_LINES, INTRO } from '../data/content/terms.js';

const ESCAPES = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' };
export const esc = (s) => String(s).replace(/[&<>"]/g, (c) => ESCAPES[c]);

/** Copy holding its own markup: the markup stays, the names are escaped. */
export const expandHtml = (s) => String(s)
  .replace(/\{game\}/g, esc(BRAND.game)).replace(/\{short\}/g, esc(BRAND.short)).replace(/\{cur\}/g, esc(BRAND.currencyPlural));

export function createLegal({ doc, gentle = () => false, above = false, xLines = X_LINES }) {
  const veil = doc.createElement('div');
  veil.className = above ? 'tc above' : 'tc';
  doc.body.insertBefore(veil, doc.getElementById('game-controls'));
  veil.addEventListener('keydown', () => veil.classList.add('kb'));
  const world = doc.getElementById('world');
  const worldWas = world?.inert;
  if (world) world.inert = true;
  const pauseCard = above ? doc.getElementById('pause') : null;
  if (pauseCard) pauseCard.inert = true;

  const notice = (className, role) => {
    const n = doc.createElement('div');
    n.className = className;
    n.hidden = true;
    if (role) n.setAttribute('role', role);
    return n;
  };
  const toastEl = notice('tc-toast', 'status');
  const wakeEl = notice('tc-wake');
  let toastTimer = 0, wakeTimer = 0, xPresses = 0;

  function nudge(el = veil.querySelector('.win')) {
    if (!el || gentle()) return;
    el.classList.remove('nudge');
    void el.offsetWidth;
    el.classList.add('nudge');
  }
  function toast(text, ms = 3400) {
    toastEl.textContent = text;
    toastEl.hidden = false;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => { toastEl.hidden = true; }, ms);
  }

  return {
    veil, nudge, toast,
    /** Replaces what is in the window. `head` is plain text with tokens; `body` is trusted markup from data/content/terms.js. */
    show({ title, head, body, small = true }) {
      const win = doc.createElement('div');
      win.className = small ? 'win small' : 'win';
      win.setAttribute('role', 'dialog');
      win.setAttribute('aria-modal', 'true');
      win.setAttribute('aria-labelledby', 'tc-head');
      win.innerHTML = `<div class="tbar"><span>${esc(brandText(title))}</span><button class="fakex" type="button" aria-label="${esc(INTRO.xLabel)}">X</button></div>`
        + `<div class="body"><h1 id="tc-head">${richText(head)}</h1>${body}</div>`;
      veil.replaceChildren(win, toastEl, wakeEl);
      toastEl.hidden = true;
      win.querySelector('.fakex').addEventListener('click', () => {
        nudge();
        toast(xLines[Math.min(xPresses++, xLines.length - 1)]);
      });
      return win;
    },
    /** Wake Up with nothing running: says so and does nothing else. */
    wakeNote(text) {
      wakeEl.textContent = text;
      wakeEl.hidden = false;
      clearTimeout(wakeTimer);
      wakeTimer = setTimeout(() => { wakeEl.hidden = true; }, 2200);
    },
    set inert(on) { veil.inert = !!on; },
    close() {
      clearTimeout(toastTimer);
      clearTimeout(wakeTimer);
      veil.remove();
      if (world) world.inert = !!worldWas;
      if (pauseCard) pauseCard.inert = false;
    },
  };
}
