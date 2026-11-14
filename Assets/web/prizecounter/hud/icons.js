/* icons.js - ink icons for the HUD, 100x100 strokes in currentColor; class "a" takes the accent fill from page.css. */

const P = (d, a) => `<path d="${d}"${a ? ' class="a"' : ''}/>`;
const C = (cx, cy, r, a) => `<circle cx="${cx}" cy="${cy}" r="${r}"${a ? ' class="a"' : ''}/>`;
export const svg = (body, cls = 'pc-ico') => `<svg class="${cls}" viewBox="0 0 100 100" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="7" stroke-linecap="round" stroke-linejoin="round">${body}</svg>`;

export const TAB_ICONS = {
  'rain-clothes': P('M40 32h20l4 14 16 42H20l16-42z', 1) + P('M40 32l-4-20M60 32l4-20M38 46h24'),
  'rain-hair': C(50, 48, 18) + P('M28 48q-2-32 22-32t22 32q4 24-6 40M28 48q-4 24 6 40', 1),
  kart: P('M10 62h80l-7-18H58l-8-12H30l-8 12H10z', 1) + C(28, 72, 10) + C(72, 72, 10),
  chess: P('M18 72l-4-44 20 18 16-28 16 28 20-18-4 44z', 1) + P('M18 84h64'),
  profile: P('M14 14h72v72H14z') + C(50, 42, 12, 1) + P('M28 76q22-22 44 0'),
  desk: P('M16 22h68v50H16z', 1) + P('M28 34h44v26H28z') + P('M40 72l-6 14M60 72l6 14'),
  prime: P('M28 28h44l16 18-38 42-38-42z', 1) + P('M12 46h76M40 28l-6 18 16 42 16-42-6-18'),
  backroom: P('M18 18h64v64H18z', 1) + C(35, 35, 4) + C(65, 35, 4) + C(50, 50, 4) + C(35, 65, 4) + C(65, 65, 4),
};
for (const k of Object.keys(TAB_ICONS)) TAB_ICONS[k] = svg(TAB_ICONS[k]);
export const STAR = '<i class="pc-star" aria-hidden="true"></i>';
export const LOCK = svg(P('M28 46h44v40H28z', 1) + P('M38 46V34a12 12 0 0 1 24 0v12'), 'pc-lock');
export const BACK = svg(P('M60 20L30 50l30 30'));
