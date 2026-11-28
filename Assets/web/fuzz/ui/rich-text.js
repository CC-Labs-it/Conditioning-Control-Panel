/* ui/rich-text.js - the two copy markups (owner 2026-11-08: "put the keywords in bold and in the theme colour").
 *   **word**        a KEYWORD: bold, in the picked theme's accent (the game's pink before a pick). Rare: one or two a line.
 *   [[pink:word]]   a DIEGETIC colour: the words name a colour, so they are drawn in it, bold. Colours: see COLOURS.
 * richText escapes everything first and only then turns the markups into spans, so copy can never inject HTML.
 * plainText strips the markups for anything that is not drawn: aria labels, titles, the save, speech, analytics.
 * A string with no markup renders exactly as it did before. Styles: ui/rich-text.css. Rules: handoff-1108/tc-copy-keywords.md. */

import { brandTokens } from '../data/brand.js';

export const COLOURS = Object.freeze(['pink', 'green', 'blue', 'purple', 'red', 'gold', 'black', 'white', 'silver']);

const KEYWORD = /\*\*([^*\n]+?)\*\*/g;
const DIEGETIC = new RegExp(`\\[\\[(${COLOURS.join('|')}):([^\\]\\n]+?)\\]\\]`, 'g');
const ESCAPES = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' };

const escapeHtml = (s) => s.replace(/[&<>"']/g, (c) => ESCAPES[c]);

/** True when the string carries either markup. */
export function hasMarks(s) {
  const t = String(s ?? '');
  KEYWORD.lastIndex = 0; DIEGETIC.lastIndex = 0;
  return KEYWORD.test(t) || DIEGETIC.test(t);
}

/** The markups removed, nothing else touched (no brand tokens). data/brand.js uses it, so every brandText is plain. */
export function stripMarks(s) {
  return String(s ?? '').replace(DIEGETIC, '$2').replace(KEYWORD, '$1');
}

/** Plain words for anything that is not drawn: brand tokens filled, markups removed. */
export function plainText(s) {
  return stripMarks(brandTokens(s));
}

/** Safe HTML: brand tokens filled, everything escaped, then the two markups become spans. */
export function richText(s) {
  return escapeHtml(brandTokens(s))
    .replace(DIEGETIC, (_, c, t) => `<span class="kw-c" data-c="${c}">${t}</span>`)
    .replace(KEYWORD, (_, t) => `<span class="kw">${t}</span>`);
}

/** Puts copy into an element: textContent when it has no markup (exactly as before), the safe spans when it has. */
export function setRich(el, s) {
  if (!el) return el;
  if (hasMarks(s)) el.innerHTML = richText(s);
  else el.textContent = plainText(s);
  return el;
}
