/* ui/unlocks/basics/sticker-toast.js - the corner card for a sticker earned while playing (owner 2026-11-25: "like
 * the Steam achievements"). The Achievements row (trunk.js depth) owns it; the queue rules are sim/sticker-notes.js.
 *
 * A paper card in the Sticker Book's own look (paper, ink edge, the sticker with its white edge, ShopRound) slides up
 * in the bottom-right corner, as Steam does, with the sticker's picture, its name and its line, holds NOTE_HOLD_MS, and
 * slides out. The game's own quiet corner notes (ui/cards.js corner) moved to the bottom left for it (owner
 * 2026-11-25, "bottom right, move the notes"); where the two would meet (a phone), the card steps up over the note.
 * - One at a time; the queue (sim/sticker-notes.js) never holds more than three cards.
 * - IN and OUT on every card. Gentle (which includes reduced motion) gets a fade, no slide.
 * - One short chime per card through sfx() (the game's own sound row, its switch and its mute); the summary card is
 *   silent. No speech of any kind.
 * - Under every card the player must answer: its own fixed host outside #world at z 88 (below the tips, #cards and
 *   the pause card), it waits while the game is paused, a run has ended or such a card is open, it leaves at once on
 *   an ending or a panic, and it never covers the main button (the button sits at the top; the card is pinned above
 *   the dock at the bottom).
 * - It takes no clicks and never takes focus: pointer-events none except its own close button. */

import { createNoteQueue, moreCopy, NOTE_HOLD_MS, waitingCopy } from '../../../sim/sticker-notes.js';
import { STICKER_NOTE_COPY } from '../../../data/content/stickers.js';
import { stickerArt } from '../../sticker-book.js';
import { crayonStarSvg, starVariant, LOOPY, RUSHED } from './gold-fx.js';
import { docOf, sfx } from './fx.js';

const CSS = `
#stk-notes { position: fixed; inset: 0; z-index: 88; pointer-events: none; }
.stk-note { position: absolute; right: max(12px, env(safe-area-inset-right, 0px)); bottom: calc(var(--dock-h, 0px) + env(safe-area-inset-bottom, 0px) + 14px);
  width: min(340px, calc(100vw - 24px)); box-sizing: border-box; display: flex; align-items: center; gap: 12px; padding: 10px 10px 10px 10px;
  background: #fffdf7; color: #28232b; border: 2px solid #28232b; border-radius: 12px; box-shadow: 4px 5px 0 rgba(40, 35, 43, .32);
  font: 600 15px/1.2 ShopRound, Fredoka, "Trebuchet MS", system-ui, sans-serif; text-align: left; pointer-events: none; will-change: transform, opacity; }
.stk-art { flex: none; width: 64px; height: 64px; display: grid; place-items: center; transform: rotate(-6deg); filter: drop-shadow(2px 3px 0 rgba(40, 35, 43, .3)); }
.stk-art img { max-width: 100%; max-height: 100%; object-fit: contain; filter: drop-shadow(2px 0 0 #fff) drop-shadow(-2px 0 0 #fff) drop-shadow(0 2px 0 #fff) drop-shadow(0 -2px 0 #fff); }
.stk-art.round img, .stk-art.ticket img { width: 58px; height: 58px; max-width: none; max-height: none; object-fit: cover; box-sizing: border-box; border: 3px solid #fff; background: #fff; filter: none; }
.stk-art.round img { border-radius: 50%; object-position: 30% 25%; }
.stk-art.ticket img { border-radius: 6px; }
.stk-txt { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 2px; }
.stk-txt small { font-size: 11px; letter-spacing: .08em; text-transform: uppercase; color: #d42f86; }
.stk-txt b { font-size: 17px; line-height: 1.15; overflow-wrap: anywhere; }
.stk-txt span { font-size: 13.5px; font-weight: 500; line-height: 1.25; color: #6b6370; }
.stk-x { flex: none; align-self: flex-start; width: 28px; height: 28px; margin: -2px -2px 0 0; padding: 0; border: 2px solid #28232b; border-radius: 50%;
  background: #fffdf7; color: #28232b; font: 700 15px/1 ShopRound, system-ui, sans-serif; cursor: pointer; pointer-events: auto; touch-action: manipulation; }
.stk-x:focus-visible { outline: 3px solid #f06ea9; outline-offset: 2px; }
`;

const BOOK_ART = { src: 'assets/clipart/book.webp', cls: '' };

/** A card the player must answer is open (ui/cards.js): a backdrop that is not a quiet corner note. */
function answering(doc) {
  const host = doc.getElementById?.('cards');
  return !!host && [...(host.children || [])].some((b) => !b.hidden && !b.classList?.contains('corner'));
}

/** The quiet corner notes on screen (ui/cards.js corner, bottom left): their cards. */
function cornerNotes(doc) {
  const host = doc.getElementById?.('cards');
  return host ? [...(host.children || [])].filter((b) => !b.hidden && b.classList?.contains('corner')).map((b) => b.querySelector?.('.card')).filter(Boolean) : [];
}

/** Keeps the card clear of a corner note: where they would meet (a narrow screen), the card sits just above the note.
 *  Reads layout only; the slide lives on `transform`, so moving `bottom` never fights it. */
export function clearOfNotes(el, doc) {
  const win = doc.defaultView;
  el.style.bottom = '';
  const notes = cornerNotes(doc);
  if (!notes.length || !win) return false;
  const r = el.getBoundingClientRect();
  for (const n of notes) {
    const nr = n.getBoundingClientRect();
    if (nr.width && nr.left < r.right + 8 && nr.right > r.left - 8) {
      el.style.bottom = `${Math.max(0, Math.round(win.innerHeight - nr.top + 12))}px`;
      return true;
    }
  }
  return false;
}

/** Mounts the corner card for one Achievements row. onShow(card) runs when a card that earned something slides in. */
export function createStickerToasts(ctx, { onShow = () => {} } = {}) {
  ctx.css(CSS);
  const doc = docOf(ctx);
  /* Outside #world, like #cards: the Page Tilt never leans it and the spiral wash never dims it. z 88 keeps it under
     the shop tips (89), every card the player must answer (90) and the pause card (95). */
  const host = ctx.el('div', '', null, doc.body);
  host.id = 'stk-notes';
  const queue = createNoteQueue();
  let showing = null, stars = null, leaveNow = null, leaving = false;
  /* An ending or a panic: the card on screen goes at once; what waits stays in line (the book has it already). */
  const drop = () => { leaveNow?.(); };
  ctx.on('ending', drop);
  ctx.on('suspend', (d) => { if (d?.on) drop(); });

  const art = (s) => {
    if (s.art?.k === 'gold' && !stars) {
      try { stars = [crayonStarSvg(0), starVariant(LOOPY, 0), starVariant(RUSHED, 0)].map((v) => `data:image/svg+xml;charset=utf-8,${encodeURIComponent(v)}`); } catch { stars = []; }
    }
    return stickerArt(s, ctx.life, stars || []);
  };

  function build(card) {
    const words = card.kind === 'one' ? card.sticker : card.kind === 'more' ? moreCopy(card.stickers.length) : waitingCopy(card.n);
    const pic = card.kind === 'one' ? art(card.sticker) : BOOK_ART;
    const kicker = card.kind === 'one' ? STICKER_NOTE_COPY.kicker : STICKER_NOTE_COPY.kickerMany;
    const { name, line } = words;
    const el = ctx.el('div', '', null, host);
    el.className = 'stk-note';
    el.setAttribute('role', 'status');
    el.setAttribute('aria-live', 'polite');
    const frame = doc.createElement('div');
    frame.className = `stk-art${pic.cls ? ` ${pic.cls}` : ''}`;
    const img = doc.createElement('img');
    img.alt = '';
    img.src = pic.src || BOOK_ART.src;
    frame.appendChild(img);
    const txt = doc.createElement('div');
    txt.className = 'stk-txt';
    for (const [tag, text] of [['small', kicker], ['b', name], ['span', line]]) {
      const t = doc.createElement(tag);
      t.textContent = ctx.brand.text(text);
      txt.appendChild(t);
    }
    const x = doc.createElement('button');
    x.type = 'button';
    x.className = 'stk-x';
    x.textContent = '×';
    x.title = STICKER_NOTE_COPY.close;
    x.setAttribute('aria-label', STICKER_NOTE_COPY.close);
    el.appendChild(frame);
    el.appendChild(txt);
    el.appendChild(x);
    return { el, x };
  }

  function show(card) {
    const { el, x } = build(card);
    clearOfNotes(el, doc);   // a corner note on a phone: step up over it
    const calm = !!ctx.gentle;
    const at = calm ? [{ opacity: 0 }, { opacity: 1 }] : [{ transform: 'translateY(calc(100% + 28px))', opacity: 0 }, { transform: 'translateY(-4px)', opacity: 1, offset: 0.75 }, { transform: 'none', opacity: 1 }];
    ctx.anim(el, at, { duration: calm ? 450 : 380, easing: 'ease-out', fill: 'backwards' });
    if (card.kind !== 'waiting') sfx(ctx, 'sfx/chime2.mp3', 0.4);
    onShow(card);
    let gone = false;
    const leave = () => {
      if (gone) return;
      gone = true;
      leaving = true;
      stopHold();
      const out = calm ? [{ opacity: 1 }, { opacity: 0 }] : [{ transform: 'none', opacity: 1 }, { transform: 'translateY(calc(100% + 28px))', opacity: 0 }];
      const a = ctx.anim(el, out, { duration: calm ? 450 : 300, easing: 'ease-in', fill: 'forwards' });
      const done = () => { el.remove(); if (showing === el) { showing = null; leaving = false; } pump(); };
      if (a?.finished) a.finished.then(done, done); else done();
    };
    const stopHold = ctx.after(NOTE_HOLD_MS, leave);
    x.addEventListener('click', leave);
    showing = el;
    leaveNow = () => {
      if (gone) return;
      gone = true;
      stopHold();
      el.remove();
      if (showing === el) { showing = null; leaving = false; }
      leaveNow = null;
    };
  }

  function pump() {
    if (showing) { if (!leaving) clearOfNotes(showing, doc); return; }   // a note that opened meanwhile
    if (!queue.size || ctx.paused || ctx.state?.ended || answering(doc)) return;
    show(queue.next());
  }

  return {
    /** A sticker earned now: one card, in turn. */
    add(sticker) { queue.add(sticker); },
    /** n stickers went in the book quietly: one silent summary card. */
    waiting(n) { queue.waiting(n); },
    pump,
    get showing() { return !!showing; },
  };
}
