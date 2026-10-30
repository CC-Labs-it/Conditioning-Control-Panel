/* preview/catalog.js - which preview an item gets (lanes B3-W5, F5). Pure: no DOM, no three.
 *
 * assets/preview/index.json (made from Astra's out/ tree) names one spec per item id; paths are under assets/:
 *   rain    the light Rain avatar (index.rain.file) with library groups shown / default groups hidden, or a wearable;
 *           coverage.hideSlots hides each covered slot's default groups (index.rain.slots), hideBodyBelow cuts the body
 *   kart    EMI's rigged kart base (index.kart.base) wearing outfits / the umbrella on its own bones, or a paint on
 *           its shell (paints walk in turn; fallback = the flat swatch when there is no 3D)
 *   model   one GLB on a turntable (kart bodies, podium trail, Back Room prizes)
 *   chess   the approved knight with one attachment under its root
 *   card    a profile frame drawn on Astra's card;  avatar  a portrait in a round frame
 *   desk    EMI's idle desk body with the item's idle overlay (same 859 x 869 canvas)
 *   swatch  a flat paint (srcs walk in turn, still = the one picture when nothing moves)
 * An id the index does not name falls back to its shelf GLB from assets/manifest.json (Back Room prizes); a retired
 * id with no stand-in art gets no preview. */
import { coveredGroups, PATTERN_ID } from './fit.js';

export const KINDS = ['rain', 'kart', 'model', 'chess', 'card', 'avatar', 'desk', 'swatch'];
/** Kinds drawn by the three.js stage; the rest are plain pictures. */
export const STAGE_KINDS = new Set(['rain', 'kart', 'model', 'chess']);

/** A kart paint's layers: { id, pattern, mask, primary, secondary } per pattern it walks. */
function paintLayers(ids, kart) {
  return (ids || []).filter((id) => PATTERN_ID[id] && kart.paints && kart.paints[id]).map((id) => {
    const p = kart.paints[id];
    return { id, pattern: PATTERN_ID[id], mask: p.mask || null, primary: p.primary || kart.primary, secondary: p.secondary || kart.secondary };
  });
}

/** The spec for one item id, or null when it has no preview. */
export function previewSpec(id, index, manifest) {
  if (!id) return null;
  const own = index && index.items && index.items[id];
  if (own && own.kind === 'rain') {
    return { ...own, hide: coveredGroups(own, index.rain.slots), body: (index.rain.slots && index.rain.slots.body) || [], base: index.rain.file, root: index.rain.root, library: index.rain.library };
  }
  if (own && own.kind === 'kart') {
    const kart = index.kart || {};
    return { ...own, base: kart.base, wear: own.wear || [], layers: paintLayers(own.paints, kart) };
  }
  if (own) return own;
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
    case 'kart': return [...new Set([spec.base, ...(spec.wear || []), ...(spec.layers || []).map((l) => l.mask)])].filter(Boolean);
    case 'chess': return [spec.piece, spec.file];
    case 'model': return [spec.file];
    case 'card': case 'avatar': return [spec.src];
    case 'desk': return [spec.base, spec.over];
    case 'swatch': return [...new Set([spec.still, ...(spec.srcs || [])])].filter(Boolean);
    default: return [];
  }
}

/** The file a kind keeps loaded while the focus stays in that kind (dropped when it leaves). */
export const sharedFile = (spec) => (spec && (spec.kind === 'rain' || spec.kind === 'kart') ? spec.base : spec && spec.kind === 'chess' ? spec.piece : null);
