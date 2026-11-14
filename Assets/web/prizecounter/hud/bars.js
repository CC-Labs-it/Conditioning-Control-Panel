/* bars.js - a kart body's stats on paper: the type word and four rows of pips. Holds no button, never moves. */
import { PIPS, STAT_WORDS, barsLine } from '../state/kart-bars.js';

export function renderBars(doc, bars) {
  const el = (tag, cls, text) => { const n = doc.createElement(tag); n.className = cls; if (text != null) n.textContent = text; return n; };
  const tag = el('div', 'pc-bars');
  tag.setAttribute('role', 'img');
  tag.setAttribute('aria-label', barsLine(bars));
  tag.append(el('p', 'pc-bars-type', bars.word));
  for (const [key, word] of STAT_WORDS) {
    const row = el('div', 'pc-bar'), pips = el('span', 'pc-pips');
    row.dataset.stat = key;
    for (let i = 0; i < PIPS; i++) pips.append(el('i', i < bars[key] ? 'pc-pip pc-on' : 'pc-pip'));
    row.append(el('span', 'pc-bar-word', word), pips);
    tag.append(row);
  }
  return tag;
}
