/* preview/catalog.js - which preview an item gets (lane B3-W5). Pure: no DOM, no three.
 *
 * assets/preview/index.json (made from Astra's out/ tree) names one spec per item id; paths are under assets/:
 *   rain    the light Rain avatar (index.rain.file) with library groups shown / default groups hidden, or a wearable
 *   model   one GLB on a turntable (kart bodies, podium trail; outfits and the umbrella use their shelf GLB)
 *   chess   the approved knight with one attachment under its root
 *   card    a profile frame drawn on Astra's card;  avatar  a portrait in a round frame
 *   desk    EMI's idle desk body with the item's idle overlay (same 859 x 869 canvas)
 *   swatch  a flat paint (srcs walk in turn, still = the one picture when nothing moves)
 * An id the index does not name falls back to its shelf GLB from assets/manifest.json (Back Room prizes); a retired
 * id with no stand-in art gets no preview. */

export const KINDS = ['rain', 'model', 'chess', 'card', 'avatar', 'desk', 'swatch'];
/** Kinds drawn by the three.js stage; the rest are plain pictures. */
export const STAGE_KINDS = new Set(['rain', 'model', 'chess']);

/** The spec for one item id, or null when it has no preview. */
export function previewSpec(id, index, manifest) {
  if (!id) return null;
  const own = index && index.items && index.items[id];
  if (own) return own.kind === 'rain' ? { ...own, base: index.rain.file, root: index.rain.root, library: index.rain.library } : own;
  const items = (manifest && manifest.items) || [];
  const retired = manifest && manifest.retired && Object.prototype.hasOwnProperty.call(manifest.retired, id) ? manifest.retired[id] : undefined;
  if (retired === null) return null;
  const art = items.find((i) => i.id === (retired || id));
  if (retired && index && index.items && index.items[retired]) return previewSpec(retired, index, manifest);
  return art && art.file ? { kind: 'model', file: art.file } : null;
}

/** Every file a spec loads (relative to assets/), shared ones first. */
export function specFiles(spec) {
  if (!spec) return [];
  switch (spec.kind) {
    case 'rain': return [spec.base, spec.wear].filter(Boolean);
    case 'chess': return [spec.piece, spec.file];
    case 'model': return [spec.file];
    case 'card': case 'avatar': return [spec.src];
    case 'desk': return [spec.base, spec.over];
    case 'swatch': return [...new Set([spec.still, ...(spec.srcs || [])])].filter(Boolean);
    default: return [];
  }
}

/** The file a kind keeps loaded while the focus stays in that kind (dropped when it leaves). */
export const sharedFile = (spec) => (spec && spec.kind === 'rain' ? spec.base : spec && spec.kind === 'chess' ? spec.piece : null);
