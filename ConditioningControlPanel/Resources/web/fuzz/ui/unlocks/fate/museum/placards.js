/* ui/unlocks/fate/museum/placards.js - ft_placard: small brass museum labels beside the button and the counter and under
 * a few open dock objects, with dry catalogue lines built from live numbers (data/content/museum.js). At most five, three
 * on a phone. They ignore the pointer, sit in the floaters layer, and are aimed with frameRect a few times a second. The
 * words change slowly: numbers every few seconds, the line itself every half minute or so (Math.random, never ctx.rng). */

import { frameRect, LITE } from '../../../perf.js';
import { placardCap, placardLine, placardValues } from './lines.js';

const CSS = `
.fz-plq { position:absolute; left:0; top:0; z-index:2; max-width:210px; padding:3px 11px 4px; box-sizing:border-box;
  background:linear-gradient(#f3dc94,#cfa245 52%,#a87b25); border:2px solid #2a2031; border-radius:3px; color:#2a1a04;
  font:600 11px/1.2 Georgia,'Times New Roman',serif; letter-spacing:.01em; text-align:center; pointer-events:none;
  box-shadow:2px 3px 0 rgba(42,32,49,.22); visibility:hidden; will-change:transform; }
.fz-plq::before, .fz-plq::after { content:''; position:absolute; top:50%; width:4px; height:4px; margin-top:-2px; border-radius:50%;
  background:#5d420e; box-shadow:inset 1px 1px 0 #e9c873; }
.fz-plq::before { left:3px; } .fz-plq::after { right:3px; }
`;

const AIM_EVERY = 0.12;   // seconds between aims
const TEXT_EVERY = 4000;  // ms between number refreshes
const MIN_SIDE = { button: 76, counter: 120 };   // px a side label needs beside the button or the counter
const MAX_W = 210;

export function mountPlacards(ctx) {
  ctx.css(CSS + (LITE ? '.fz-plq{font-size:10px;padding:2px 9px 3px}' : ''));
  const layer = ctx.layer('floaters');
  const side = ctx.layer('side', 'left').parentElement || ctx.layer('side');   // both rails: ctx.layer('side') is one of them
  const cap = placardCap(LITE);
  const cur = ctx.brand?.currencyPlural || '';
  const items = [];   // { kind, target, el, index, nextTurn, text, w, h, name }

  const make = (kind, target, name = '') => {
    const el = ctx.el('div', '', null, layer);
    el.className = 'fz-plq';
    el.setAttribute('aria-hidden', 'true');
    const it = { kind, target, el, name, index: Math.floor(Math.random() * 8), nextTurn: 0, text: '', w: 0, h: 0, shown: false };
    items.push(it);
    if (!ctx.gentle && !ctx.reduced) ctx.anim(el, [{ opacity: 0 }, { opacity: 1 }], { duration: 700, easing: 'ease-out' });
    return it;
  };
  const drop = (it) => { it.el.remove(); items.splice(items.indexOf(it), 1); };

  if (ctx.button?.el) make('button', ctx.button.el);
  if (ctx.parts?.counter) make('counter', ctx.parts.counter);

  /* Open dock objects, newest kept as long as they stay open. */
  function scan() {
    const open = [...side.querySelectorAll('section.dk.open:not(.door)')];
    for (const it of [...items]) if (it.kind === 'object' && !open.includes(it.target)) drop(it);
    for (const sec of open) {
      if (items.length >= cap) break;
      if (items.some((it) => it.target === sec)) continue;
      make('object', sec, (sec.getAttribute('aria-label') || '').trim());
    }
  }

  function write(now) {
    const values = placardValues(ctx.state, (n) => ctx.fmt(n), cur);
    for (const it of items) {
      if (now >= it.nextTurn) {
        if (it.nextTurn) it.index += 1 + Math.floor(Math.random() * 2);
        it.nextTurn = now + 26000 + Math.random() * 22000;
      }
      const line = placardLine(it.kind, it.index, { ...values, name: it.name || null });
      if (line !== it.text) { it.text = line; it.el.textContent = line; it.w = 0; }
    }
  }

  let since = AIM_EVERY;
  function aim(dt) {
    since += dt;
    if (since < AIM_EVERY) return;
    since = 0;
    const base = frameRect(layer);
    const W = base.width || 0, H = base.height || 0;
    for (const it of items) {
      const r = frameRect(it.target);
      const visible = r.width > 4 && r.height > 4 && r.bottom > base.top && r.top < base.bottom && it.target.isConnected;
      if (!visible) { if (it.shown) { it.el.style.visibility = 'hidden'; it.shown = false; } continue; }
      /* The button and the counter get their label beside them, on whichever side has room; with no room (a phone and a
       * long counter) the label waits rather than cover what it describes. */
      let sideOf = 0;
      if (it.kind !== 'object') {
        /* The button's right side keeps room for Hold to Earn's meter (goon runs). */
        const gapL = it.kind === 'button' ? 20 : 12, gapR = it.kind === 'button' ? 52 : 12;
        const left = r.left - base.left - gapL - 4, right = base.right - r.right - gapR - 4;
        const prefer = it.kind === 'button' ? [[-1, left], [1, right]] : [[1, right], [-1, left]];
        const pick = prefer.find(([, room]) => room >= MIN_SIDE[it.kind]);
        if (!pick) { if (it.shown) { it.el.style.visibility = 'hidden'; it.shown = false; } continue; }
        sideOf = pick[0];
        const max = Math.floor(Math.min(MAX_W, pick[1]) / 8) * 8;   // stepped, so a moving page rarely re-measures
        if (max !== it.max) { it.max = max; it.el.style.maxWidth = `${max}px`; it.w = 0; }
      } else {
        const max = Math.floor(Math.max(120, Math.min(MAX_W, r.width - 16)) / 8) * 8;   // never wider than its object
        if (max !== it.max) { it.max = max; it.el.style.maxWidth = `${max}px`; it.w = 0; }
      }
      if (!it.w) { it.w = it.el.offsetWidth; it.h = it.el.offsetHeight; }   // once per text or width change, never per frame
      let x, y;
      if (it.kind === 'button') {
        x = sideOf < 0 ? r.left - base.left - it.w - 20 : r.right - base.left + 52;
        y = r.bottom - base.top - it.h;
      } else if (it.kind === 'counter') {
        x = sideOf > 0 ? r.right - base.left + 12 : r.left - base.left - it.w - 12;
        y = r.top - base.top + (r.height - it.h) / 2;
      } else {
        x = r.left - base.left + (r.width - it.w) / 2; y = r.bottom - base.top + 6;
        if (y + it.h > H - 4) y = r.top - base.top - it.h - 6;
      }
      x = Math.max(4, Math.min(W - it.w - 4, x));
      y = Math.max(4, Math.min(H - it.h - 4, y));
      it.el.style.transform = `translate(${Math.round(x)}px,${Math.round(y)}px)`;
      if (!it.shown) { it.el.style.visibility = 'visible'; it.shown = true; }
    }
  }

  let clock = 0;
  scan(); write(0);
  ctx.every(TEXT_EVERY, () => { clock += TEXT_EVERY; scan(); write(clock); });
  ctx.frame((dt) => aim(dt));
  return {};
}
