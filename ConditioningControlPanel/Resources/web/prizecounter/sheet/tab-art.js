/* sheet/tab-art.js - the small emblems beside four tab labels (owner, 2026-11-17): a kart on Karts, a knight on Chess,
 * a bubble with a spiral in it on Bubbles and effects, a toy rubber duck on Accessories. Same hand as the shelf emblems
 * in sheet/section-art.js (thick plum ink, candy fills, a glossy bean), redrawn small so the ink stays thick at 22 px.
 * The hover and selected reactions are sheet-juice.css (.tab-emb ...) and sheet/juice.js (the pop on a tab change), so
 * the motion levels gate them like the rest. Decorative only: aria-hidden, no words, no network. */

const INK = '#1d0c26';

/* The kart: tangerine body, pink stripe, pink seat back, two ink tyres with lilac hubs. .te-wheel spins on hover. */
const tyre = (cx, cy, r) => `<g class="te-wheel" style="transform-origin:${cx}px ${cy}px"><circle cx="${cx}" cy="${cy}" r="${r}" fill="#2b2333" stroke="${INK}" stroke-width="2"/><circle cx="${cx}" cy="${cy}" r="${r * 0.42}" fill="#d9cde6"/><path d="M${cx - r * 0.42} ${cy} h${r * 0.84}" stroke="${INK}" stroke-width="1.3"/></g>`;
const KART = `<svg viewBox="0 0 34 24" width="35" height="25" aria-hidden="true" focusable="false">
<g class="te-body">
<path d="M10 13.5 L10.8 5.2 C11 3.6 13 3.3 13.8 4.6 L16.6 13 Z" fill="#ff6fb1" stroke="${INK}" stroke-width="2" stroke-linejoin="round"/>
<path d="M4 15.8 C4 12.9 5.8 11.6 9 11.6 L18.5 11.6 C20.6 11.6 21.4 9.8 23.6 9.8 L28.4 10.8 C31.6 11.4 32.6 13.4 32 15.4 C31.6 17.3 29.6 18 27.2 18 L7.6 18 C5.4 18 4 17.4 4 15.8 Z" fill="#ff9440" stroke="${INK}" stroke-width="2.2" stroke-linejoin="round"/>
<path d="M6.6 15.2 L30 14.8" stroke="#ff6fb1" stroke-width="2.2" stroke-linecap="round"/>
<ellipse cx="11.6" cy="13" rx="3" ry="0.9" fill="#fff" opacity=".9"/>
</g>
${tyre(10, 18.6, 4.4)}${tyre(27.2, 19.2, 3.8)}
</svg>`;

/* The knight: cream head with a pink mane on a pink plinth. .te-body tips from the plinth on hover. */
const KNIGHT = `<svg viewBox="0 0 24 26" width="23" height="25" aria-hidden="true" focusable="false">
<g class="te-body">
<path d="M7.6 18.6 C8.2 15.6 9.6 14.4 11 13.6 C8.8 14.2 6.4 14.8 4.9 13.6 C3.5 12.5 3.6 10.8 4.9 9.6 L8.8 5.6 C9.4 4 10.8 3.1 12.4 3.1 L13 1.3 L14.7 3.6 C17.2 4.9 18.8 8.4 18.2 12.8 C17.9 15.2 17 16.7 16.7 18.6 Z" fill="#fff3df" stroke="${INK}" stroke-width="2" stroke-linejoin="round"/>
<path d="M13.8 3.9 C15.6 4.3 16.7 5.2 17.4 6.5 C18.6 7.2 18.7 8.4 18.3 9.3 C19.2 10.3 19 11.5 18.6 12.4 C19.1 13.7 18.6 14.9 17.8 15.8 L17 18.6 L15.6 18.6 C16.6 14.8 16.8 10.4 15 6.8 Z" fill="#ff6fb1" stroke="${INK}" stroke-width="1.6" stroke-linejoin="round"/>
<circle cx="10.6" cy="7.4" r="1.15" fill="${INK}"/>
<ellipse cx="11.9" cy="5.2" rx="1.4" ry="0.7" transform="rotate(-28 11.9 5.2)" fill="#fff"/>
</g>
<rect x="3.6" y="18.2" width="16.8" height="5.6" rx="2.6" fill="#ff6fb1" stroke="${INK}" stroke-width="2"/>
<ellipse cx="7" cy="19.9" rx="1.8" ry="0.7" fill="#fff" opacity=".85"/>
</svg>`;

/* The bubble: a sky soap bubble with a pink spiral inside and a glossy bean. .te-spin turns the spiral on hover. */
const BUBBLE = `<svg viewBox="0 0 24 24" width="24" height="24" aria-hidden="true" focusable="false">
<g class="te-body">
<circle cx="12" cy="12.4" r="9.6" fill="#bfe9ff" stroke="${INK}" stroke-width="2.2"/>
<g class="te-spin" style="transform-origin:12px 12.6px">
<path d="M12.2 12.6 C12.2 11.3 14 11.1 14.4 12.5 C15 14.4 12.8 16 10.8 15.3 C8.2 14.4 8.1 10.4 10.8 9.1 C14 7.6 17.8 9.7 17.8 13.2 C17.8 17 14 19.2 10.2 18.6" fill="none" stroke="${INK}" stroke-width="3.8" stroke-linecap="round"/>
<path d="M12.2 12.6 C12.2 11.3 14 11.1 14.4 12.5 C15 14.4 12.8 16 10.8 15.3 C8.2 14.4 8.1 10.4 10.8 9.1 C14 7.6 17.8 9.7 17.8 13.2 C17.8 17 14 19.2 10.2 18.6" fill="none" stroke="#ff6fb1" stroke-width="2" stroke-linecap="round"/>
</g>
<ellipse cx="8.2" cy="7.4" rx="2.6" ry="1.3" transform="rotate(-35 8.2 7.4)" fill="#fff" opacity=".95"/>
<circle cx="17.2" cy="17.4" r="0.9" fill="#fff" opacity=".8"/>
</g>
</svg>`;

/* The duck: a yellow rubber duck with an orange beak, an ink eye and a bean on its back. .te-body bobs on hover. */
const DUCK = `<svg viewBox="0 0 26 24" width="28" height="26" aria-hidden="true" focusable="false">
<g class="te-body">
<path d="M3 13.6 C3 11.8 4.6 11.4 6 12.2 C7.4 13 9 12.6 9.6 11.4 C8 10.2 7.6 7.8 8.6 6 C10 3.4 13.8 3 15.8 5.2 C17.4 7 17.2 9.6 16 11 C18.4 10.6 20.4 9.2 21.6 8 C23 9.6 23.6 12.6 22.4 15.4 C21 18.8 17.4 20.6 12.6 20.6 C7 20.6 3 18 3 13.6 Z" fill="#ffd23c" stroke="${INK}" stroke-width="2" stroke-linejoin="round"/>
<path d="M8.8 8.4 C7.4 8.2 5.6 8.6 4.8 9.6 C5.8 10.6 7.6 10.6 9 10.2 Z" fill="#ff9440" stroke="${INK}" stroke-width="1.6" stroke-linejoin="round"/>
<circle cx="11.6" cy="7" r="1.15" fill="${INK}"/>
<path d="M12.6 14 C14.4 15.6 17.4 15.4 18.8 13.4" fill="none" stroke="${INK}" stroke-width="1.5" stroke-linecap="round" opacity=".55"/>
<ellipse cx="12.4" cy="4.9" rx="1.6" ry="0.7" transform="rotate(-20 12.4 4.9)" fill="#fff" opacity=".95"/>
<ellipse cx="6.4" cy="14.6" rx="1.5" ry="0.7" fill="#fff" opacity=".8"/>
</g>
</svg>`;

/** The tab ids that wear an emblem (sheet/model.js sheetTabs ids), and their art. */
const ART = Object.freeze({ kart: KART, chess: KNIGHT, effects: BUBBLE, accessories: DUCK });
export const TAB_EMBLEMS = Object.freeze(Object.keys(ART));

/** The emblem's SVG markup for a tab id, or null for a tab without one. */
export const tabEmblemSvg = (id) => (Object.prototype.hasOwnProperty.call(ART, id) ? ART[id] : null);

/** The emblem node for a tab button (a span.tab-emb.te-<id>, decorative), or null. */
export function tabEmblem(doc, id) {
  const svg = tabEmblemSvg(id);
  if (!svg || !doc) return null;
  const s = doc.createElement('span');
  s.className = 'tab-emb te-' + id;
  s.setAttribute('aria-hidden', 'true');
  s.innerHTML = svg;
  return s;
}
