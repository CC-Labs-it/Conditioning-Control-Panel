/* ============================================================================
 * ui/replay-bar.js - what a host-handed replay (net/replay-seat.js) puts on
 * screen: a strip of controls for the whole game, a caption for the reel, and
 * the result line at the end. Built only when the page boots as a replay.
 * No timers: it repaints when the seat says something changed.
 * ==========================================================================*/

import { resultLine } from '../door/replay.js';
import { captionOf } from '../game/cup-replay.js';

const el = (tag, cls, text) => { const n = document.createElement(tag); if (cls) n.className = cls; if (text) n.textContent = text; return n; };

export function createReplayBar({ seat, record, reel = false, root = document.body }) {
  const wrap = el('div', 'cup-bar' + (reel ? ' reel' : ''));
  const cap = el('div', 'cup-cap');
  const capSeat = cap.appendChild(el('span', 'cup-seat'));
  cap.appendChild(el('b', 'cup-names', record.white + ' vs ' + record.black));
  const capMove = cap.appendChild(el('span', 'cup-move'));
  wrap.appendChild(cap);
  const line = wrap.appendChild(el('p', 'cup-result'));
  line.setAttribute('role', 'status');
  line.hidden = true;

  let scrub = null, playBtn = null, san = null;
  if (!reel) {
    const strip = wrap.appendChild(el('div', 'cup-strip'));
    const btn = (act, label, glyph) => { const b = el('button', 'hud-btn cup-btn', glyph); b.type = 'button'; b.dataset.cup = act; b.title = label; b.setAttribute('aria-label', label); strip.appendChild(b); return b; };
    btn('prev', 'Back one move', '◀');
    playBtn = btn('play', 'Play', '▶');
    btn('next', 'Forward one move', '▶▏');
    san = strip.appendChild(el('span', 'cup-san'));
    scrub = strip.appendChild(el('input', 'cup-scrub'));
    scrub.type = 'range'; scrub.min = '0'; scrub.step = '1';
    scrub.setAttribute('aria-label', 'Move');
    scrub.addEventListener('input', () => { seat.pause(); seat.seek(Number(scrub.value)); });
    strip.addEventListener('click', (e) => {
      const b = e.target.closest('[data-cup]');
      if (!b) return;
      if (b.dataset.cup === 'play') seat.toggle(); else seat.step(b.dataset.cup === 'next' ? 1 : -1);
    });
  }
  root.appendChild(wrap);

  function paint(s) {
    const c = s.caption || captionOf(record, s.i);
    capSeat.textContent = c.seat;
    capSeat.hidden = !c.seat;
    capMove.textContent = reel ? c.move : '';
    line.hidden = !s.over;
    // "White won by checkmate", with the winner's own name in it
    if (s.over) line.textContent = resultLine({ result: s.over }).replace(/^(White|Black)/, (w) => (w === 'White' ? record.white : record.black));
    if (reel) return;
    scrub.max = String(s.total); scrub.value = String(s.i);
    scrub.style.setProperty('--p', (s.total ? (100 * s.i) / s.total : 0).toFixed(1) + '%');
    san.textContent = s.i ? Math.ceil(s.i / 2) + (s.i % 2 ? '. ' : '... ') + s.san : 'Start';
    playBtn.textContent = s.playing ? '❚❚' : '▶';
    playBtn.title = s.playing ? 'Pause' : 'Play';
    playBtn.setAttribute('aria-label', playBtn.title);
  }
  return { paint, root: wrap };
}
