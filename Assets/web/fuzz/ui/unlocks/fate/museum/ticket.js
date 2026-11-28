/* ui/unlocks/fate/museum/ticket.js - ft_ticket: a paper ticket stub pinned to the page, as a small movable dock object.
 * ADMIT ONE, a serial from the run seed, today's date. Code-drawn paper, ink and a red pin in the game's clip art look
 * (chunky dark outline, one lamp top left, so the shadow falls down right and never moves). The row's own fps pays. */

import { TICKET } from '../../../../data/content/museum.js';
import { ticketDate, ticketSerial } from './lines.js';

const INK = '#2a2031';
const PAPER = '#f4e7c6';
const RED = '#b8323f';

/* A slightly hand-cut outline: notches halfway down both sides, a perforation before the stub. Math.random only. */
function outline(w, h, notch) {
  const j = () => (Math.random() - 0.5) * 1.6;
  const mid = h / 2;
  return [
    `M${4 + j()} ${6 + j()}`, `L${w - 4 + j()} ${5 + j()}`,
    `L${w - 5 + j()} ${mid - notch}`, `A${notch} ${notch} 0 0 0 ${w - 5} ${mid + notch}`,
    `L${w - 4 + j()} ${h - 5 + j()}`, `L${5 + j()} ${h - 4 + j()}`,
    `L${4 + j()} ${mid + notch}`, `A${notch} ${notch} 0 0 0 4 ${mid - notch}`, 'Z',
  ].join(' ');
}

export function ticketSvg(doc, { serial, date }) {
  const W = 220, H = 104, STUB = 164;
  const T = TICKET;
  const wrap = doc.createElement('div');
  wrap.style.cssText = `position:relative;width:100%;filter:drop-shadow(3px 4px 0 rgba(42,32,49,.2));`;
  wrap.innerHTML = `<svg viewBox="0 0 ${W} ${H}" width="100%" role="img" aria-label="${T.admit}, ${T.serial} ${serial}, ${date}" style="display:block;overflow:visible">
  <path d="${outline(W, H, 9)}" fill="${PAPER}" stroke="${INK}" stroke-width="2.6" stroke-linejoin="round"/>
  <path d="M14 15 H${STUB - 10} V${H - 15} H14 Z" fill="none" stroke="${RED}" stroke-width="1.4" opacity=".55"/>
  <path d="M${STUB} 9 V${H - 9}" stroke="${INK}" stroke-width="2" stroke-dasharray="3 5" stroke-linecap="round"/>
  <text x="${(14 + STUB - 10) / 2}" y="31" text-anchor="middle" fill="${INK}" style="font:700 9px var(--font,'Barlow',system-ui,sans-serif);letter-spacing:4px">${T.house}</text>
  <text x="${(14 + STUB - 10) / 2}" y="60" text-anchor="middle" fill="${RED}" stroke="${INK}" stroke-width=".6" textLength="126" lengthAdjust="spacingAndGlyphs" style="font:800 22px Georgia,'Times New Roman',serif">${T.admit}</text>
  <text x="${(14 + STUB - 10) / 2}" y="78" text-anchor="middle" fill="${INK}" style="font:600 10px var(--font,'Barlow',system-ui,sans-serif);letter-spacing:1px">${date}</text>
  <text x="${(14 + STUB - 10) / 2}" y="${H - 21}" text-anchor="middle" fill="${INK}" opacity=".7" style="font:italic 8px Georgia,serif">${T.rule}</text>
  <g transform="translate(${(STUB + W) / 2 - 2} ${H / 2}) rotate(-90)">
    <text x="0" y="-6" text-anchor="middle" fill="${INK}" style="font:700 8px var(--font,'Barlow',system-ui,sans-serif);letter-spacing:2px">${T.serial}</text>
    <text x="0" y="9" text-anchor="middle" fill="${RED}" style="font:800 14px 'Courier New',Consolas,monospace;letter-spacing:1px">${serial}</text>
  </g>
  <text x="${(STUB + W) / 2 + 20}" y="${H / 2}" text-anchor="middle" fill="${INK}" opacity=".6" transform="rotate(-90 ${(STUB + W) / 2 + 20} ${H / 2})" style="font:italic 7px Georgia,serif">${T.keep}</text>
  <g transform="translate(${W / 2 - 18} 4)">
    <ellipse cx="3" cy="5" rx="7" ry="3" fill="${INK}" opacity=".18"/>
    <circle cx="0" cy="0" r="7.5" fill="#d8414f" stroke="${INK}" stroke-width="2.2"/>
    <circle cx="-2.4" cy="-2.6" r="2.2" fill="#fff" opacity=".75"/>
  </g>
</svg>`;
  return wrap;
}

export function mountTicket(ctx) {
  /* The date is the day it was bought, kept for the run, so a reload the next morning still shows the visit's date. */
  let date = ctx.store.get('date');
  if (typeof date !== 'string' || !date) { date = ticketDate(new Date()); ctx.store.set('date', date); }
  const serial = ticketSerial(ctx.state.seed);
  const host = ctx.dock({ title: ctx.item?.name || 'Museum Ticket', icon: '🎟️', width: 190 });
  const paper = ticketSvg(host.ownerDocument || document, { serial, date });
  paper.dataset.dockSurface = '';
  host.appendChild(paper);
  ctx.cleanup(() => paper.remove());
  return {};
}
