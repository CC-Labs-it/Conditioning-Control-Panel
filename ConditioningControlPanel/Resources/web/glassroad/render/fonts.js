/* render/fonts.js - the diegetic faces for words drawn into canvases (road markings, bubble text, the word rush).
 *
 * Six OFL faces from the Afterimage word library, ASCII subsets (fonts/, licences beside them). A canvas only
 * draws a face the document has LOADED, so loadFonts() must resolve before any atlas is measured or drawn
 * (Afterimage rule: measure and draw with the same font and weight, load local fonts before measurement).
 * Use FACES[k].css(px, weight?) for ctx.font. Three faces are variable (weight range in `w`); the rest have one
 * weight baked in. A face that fails to load falls back to its system stack and the game carries on.
 */

export const FACES = Object.freeze({
  // Oswald, variable 200-700: tall, tight, road-paint lettering
  condensed: face('gr-condensed', 'condensed.ttf', [200, 700], 'Impact, "Arial Narrow", sans-serif'),
  // Barlow Condensed Bold: the highway sign / stencil face
  display: face('gr-display', 'display.ttf', [700, 700], '"Arial Narrow", sans-serif'),
  // DM Serif Display Italic: big, glossy, editorial
  editorial: face('gr-editorial', 'editorial.ttf', [400, 400], 'Georgia, serif', 'italic'),
  // Space Mono Bold: machine print, terminal, cue cards
  mono: face('gr-mono', 'mono.ttf', [700, 700], 'Consolas, "Courier New", monospace'),
  // Nunito, variable 200-1000: soft, round, sweet
  rounded: face('gr-rounded', 'rounded.ttf', [200, 1000], '"Segoe UI", sans-serif'),
  // Cormorant Garamond Light Italic, variable: whispered, silky, thin
  silk: face('gr-silk', 'silk.ttf', [300, 700], 'Georgia, serif', 'italic'),
});

function face(family, file, w, fallback, style = 'normal') {
  return Object.freeze({
    family, file, w: Object.freeze(w), style,
    css(px, weight = w[1]) {
      const wt = Math.max(w[0], Math.min(w[1], weight));
      return `${style === 'italic' ? 'italic ' : ''}${wt} ${Math.round(px)}px "${family}", ${fallback}`;
    },
  });
}

let loading = null;

/** Loads every face once (repeat calls share the promise). Never rejects. @returns {Promise<string[]>} the faces that loaded. */
export function loadFonts(base = new URL('../fonts/', import.meta.url)) {
  if (loading) return loading;
  if (typeof FontFace === 'undefined' || typeof document === 'undefined' || !document.fonts) return (loading = Promise.resolve([]));
  loading = Promise.all(Object.values(FACES).map(async (f) => {
    try {
      const ff = new FontFace(f.family, `url(${new URL(f.file, base).href})`,
        { style: f.style, weight: f.w[0] === f.w[1] ? String(f.w[0]) : `${f.w[0]} ${f.w[1]}` });
      document.fonts.add(await ff.load());
      return f.family;
    } catch { return null; }
  })).then((r) => r.filter(Boolean));
  return loading;
}
