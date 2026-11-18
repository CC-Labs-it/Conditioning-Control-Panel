/* sheet/section-art.js - the game shelves' emblems: a cartoon knight on the Chess shelf head and a cartoon kart on the
 * Kart shelf head, drawn as inline SVG in the same hand as the item pictures (thick plum ink, glossy beans, candy fills).
 * The emblem stands where the head's little star sits, so a shelf reads at a glance while scrolling. The themed ground
 * behind each shelf (pink and cream board, a race track) is sheet.css (.sec.fam-chess / .sec.fam-kart); the idle life
 * and the hover reaction are sheet-juice.css (.emb ...), so the motion levels and reduced motion gate them like the rest.
 * Decorative only: aria-hidden, no words, no network. */

const INK = '#1d0c26';

/* The knight: cream head, pink mane, pink collar on a cream plinth with a pink band. Parts the juice moves:
 * .emb-body (tips and bobs from the plinth), .emb-spark (twinkles in on hover). */
const KNIGHT = `<svg viewBox="0 0 80 84" width="80" height="84" aria-hidden="true" focusable="false">
<g class="emb-shadow"><ellipse cx="40" cy="79" rx="27" ry="4" fill="${INK}" opacity=".35"/></g>
<g class="emb-body">
<path d="M28 58 C30 49 34 45 38 43 C32 45 24 47 19 43 C14.5 39.6 14.8 34 19 30 L31.5 17.5 C33.5 12.5 38 9.5 43 9.5 L45 3.5 L50.5 11 C58.5 15 63.5 26.5 61.5 40.5 C60.5 48 57.5 53 56.5 58 Z" fill="#fff3df" stroke="${INK}" stroke-width="3.2" stroke-linejoin="round"/>
<path d="M47 11.5 C52.5 12.5 56 15.5 58 19.5 C62 21.5 62.3 25.5 61 28.5 C64 31.5 63.3 35.5 62 38.5 C63.8 42.5 62 46.5 59.6 49.5 L57 58 L52.6 58 C55.6 46 56.6 32 50.6 20.5 Z" fill="#ff6fb1" stroke="${INK}" stroke-width="2.6" stroke-linejoin="round"/>
<path d="M21.5 31.5 C24 30.5 27 31 28.5 33.5" fill="none" stroke="${INK}" stroke-width="2" stroke-linecap="round" opacity=".55"/>
<circle cx="20.6" cy="37.4" r="1.6" fill="${INK}"/>
<circle cx="31" cy="35.5" r="3.4" fill="#ff9cc9" opacity=".75"/>
<circle cx="36.6" cy="22.6" r="3.1" fill="${INK}"/><circle cx="37.6" cy="21.5" r="1.05" fill="#fff"/>
<ellipse cx="41.5" cy="15.5" rx="4.2" ry="2.1" transform="rotate(-28 41.5 15.5)" fill="#fff" opacity=".95"/>
<ellipse cx="47" cy="47" rx="2.2" ry="6" transform="rotate(10 47 47)" fill="#e9d2b4" opacity=".8"/>
<rect x="19" y="55" width="42" height="11" rx="5" fill="#ff6fb1" stroke="${INK}" stroke-width="3.2"/>
<ellipse cx="27" cy="58.6" rx="4" ry="1.6" fill="#fff" opacity=".8"/>
</g>
<rect x="11" y="64" width="58" height="13" rx="6" fill="#fff3df" stroke="${INK}" stroke-width="3.2"/>
<path d="M14.6 71 h50.8" stroke="#ff6fb1" stroke-width="3.4" stroke-linecap="round"/>
<ellipse cx="20" cy="67.6" rx="4.2" ry="1.5" fill="#fff" opacity=".95"/>
<g class="emb-spark"><path d="M68 13 l1.8 5 5 1.8 -5 1.8 -1.8 5 -1.8 -5 -5 -1.8 5 -1.8z" fill="#ffc93c" stroke="${INK}" stroke-width="1.6" stroke-linejoin="round"/>
<path d="M10 17 l1.1 3 3 1.1 -3 1.1 -1.1 3 -1.1 -3 -3 -1.1 3 -1.1z" fill="#ff6fb1"/></g>
</svg>`;

/* The kart: tangerine body with a pink racing stripe and seat, black tyres with lilac hubs, a cream puff from the exhaust.
 * Parts the juice moves: .emb-body (idle rumble, rears up on hover), .emb-wheel (spins on hover), .emb-puff (exhaust). */
const wheel = (cx, cy, r) => `<g class="emb-wheel" style="transform-origin:${cx}px ${cy}px">
<circle cx="${cx}" cy="${cy}" r="${r}" fill="#2b2333" stroke="${INK}" stroke-width="3"/>
<circle cx="${cx}" cy="${cy}" r="${r * 0.45}" fill="#d9cde6" stroke="${INK}" stroke-width="2.2"/>
<path d="M${cx - r * 0.45} ${cy} h${r * 0.9} M${cx} ${cy - r * 0.45} v${r * 0.9}" stroke="${INK}" stroke-width="1.8"/>
<ellipse cx="${cx - r * 0.42}" cy="${cy - r * 0.52}" rx="${r * 0.3}" ry="${r * 0.14}" transform="rotate(-35 ${cx - r * 0.42} ${cy - r * 0.52})" fill="#fff" opacity=".55"/>
</g>`;
const KART = `<svg viewBox="0 0 104 72" width="104" height="72" aria-hidden="true" focusable="false">
<g class="emb-shadow"><ellipse cx="58" cy="67.5" rx="38" ry="3.6" fill="${INK}" opacity=".35"/></g>
<g class="emb-puffs">
<circle class="emb-puff p1" cx="12" cy="41" r="5" fill="#fff3df" stroke="${INK}" stroke-width="2"/>
<circle class="emb-puff p2" cx="7" cy="33" r="3.6" fill="#fff3df" stroke="${INK}" stroke-width="2"/>
<circle class="emb-puff p3" cx="4" cy="44" r="2.8" fill="#fff3df" stroke="${INK}" stroke-width="1.8"/>
</g>
<g class="emb-body">
<rect x="14" y="40" width="11" height="6" rx="2.5" fill="#d9cde6" stroke="${INK}" stroke-width="2.4"/>
<path d="M30 38 L31.5 19 C31.8 15.6 35.8 14.6 37.8 17.4 L44 37 Z" fill="#ff6fb1" stroke="${INK}" stroke-width="3" stroke-linejoin="round"/>
<ellipse cx="34.6" cy="21" rx="1.4" ry="3.2" fill="#fff" opacity=".85"/>
<path d="M66 33 L72 20" stroke="${INK}" stroke-width="3.4" stroke-linecap="round"/>
<ellipse cx="72.6" cy="19" rx="6.2" ry="2.8" transform="rotate(-24 72.6 19)" fill="#b99cff" stroke="${INK}" stroke-width="2.6"/>
<path d="M22 47 C22 40 26 36 34 36 L56 36 C61 36 63 31.5 68 31.5 L88 35 C97 36.6 99.6 42 98 47 C97 51.4 92 53.6 86 53.6 L30 53.6 C25 53.6 22 51.4 22 47 Z" fill="#ff9440" stroke="${INK}" stroke-width="3.2" stroke-linejoin="round"/>
<path d="M27 45.5 L95.5 44.5" stroke="#ff6fb1" stroke-width="4.2" stroke-linecap="round"/>
<path d="M27 45.5 L95.5 44.5" stroke="#fff" stroke-width="1.2" stroke-linecap="round" opacity=".7"/>
<ellipse cx="41" cy="39.6" rx="7" ry="1.9" fill="#fff" opacity=".9"/>
<ellipse cx="78" cy="35.6" rx="5" ry="1.5" transform="rotate(8 78 35.6)" fill="#fff" opacity=".8"/>
<circle cx="88" cy="40.5" r="4.6" fill="#fff3df" stroke="${INK}" stroke-width="2"/>
<path d="M85.6 40.5 h4.8 M88 38.1 v4.8" stroke="#ff6fb1" stroke-width="1.8" stroke-linecap="round"/>
</g>
${wheel(36, 55, 11.5)}${wheel(85, 57, 9.5)}
</svg>`;

/** The section keys that wear an emblem and a themed ground. */
export const THEMED = Object.freeze(['chess', 'kart']);
const ART = Object.freeze({ chess: KNIGHT, kart: KART });

/** The emblem's SVG markup for a section key, or null for an untouched shelf. */
export const emblemSvg = (key) => (Object.prototype.hasOwnProperty.call(ART, key) ? ART[key] : null);

/** The emblem as a CSS image (a data: URI, no network), for the small badge a chess or kart card wears away from its shelf. */
export const emblemUri = (key) => {
  const svg = emblemSvg(key);
  if (!svg) return null;
  const file = svg.replace(/<g class="emb-spark">[\s\S]*?<\/g>/, '').replace('<svg ','<svg xmlns="http://www.w3.org/2000/svg" ').replace(/\s*\n\s*/g, ' ');   // an image file needs its namespace
  return `url("data:image/svg+xml,${encodeURIComponent(file)}")`;
};

/** Hands the emblems to sheet.css as --emb-chess / --emb-kart on the sheet's root (a search or the Owned list badges a
 * game card with its shelf's emblem). Quietly does nothing on a DOM without styles. */
export function lendEmblems(root) {
  const st = root && root.style;
  if (!st || typeof st.setProperty !== 'function') return;
  for (const k of THEMED) st.setProperty('--emb-' + k, emblemUri(k));
}

/** The emblem node for a shelf head (a span.emb.emb-<key>, decorative), or null: the head keeps its little star. */
export function sectionEmblem(doc, key) {
  const svg = emblemSvg(key);
  if (!svg || !doc) return null;
  const s = doc.createElement('span');
  s.className = 'emb emb-' + key;
  s.setAttribute('aria-hidden', 'true');
  s.innerHTML = svg;
  return s;
}
