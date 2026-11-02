/* hud/kit.js - Astra's SVG HUD kit (art/ui/, art/ART.md "Wave 2 HUD kit") as plain images.
 *
 * The kit is DOM art, not a texture, so it loads as <img> here and never through render/art.js.
 * Every piece is optional: a group switches on (a `kit-<group>` class on the HUD root and on the
 * card) only once all of its files have loaded, and until then the CSS keeps the plain DOM look.
 * Text never lives in the kit: numerals carry their suffix and labels in the DOM beside them.
 */
const BASE = 'art/ui/';
export const kitUrl = (name) => BASE + name + '.svg';

const PLACES = [];
for (let n = 1; n <= 8; n++) PLACES.push('place-' + n + '-white', 'place-' + n + '-pink');

export const KIT_GROUPS = Object.freeze({
  place: PLACES,
  slot: ['item-slot-frame'],
  pips: ['drift-pip-pink', 'drift-pip-white', 'drift-pip-gold'],
  divider: ['lap-time-divider'],
  map: ['minimap-frame'],
  flags: ['flag-start', 'flag-finish'],
  doors: ['menu-race-frame', 'menu-online-frame'],
});

/** The place numeral art for 1..8 (pink for first, white otherwise), or null past the kit. */
export function placeArt(place) {
  const n = Math.round(place);
  return n >= 1 && n <= 8 ? 'place-' + n + '-' + (n === 1 ? 'pink' : 'white') : null;
}

/** @param {{ doc: Document, onGroup(name: string): void }} o */
export function createKit({ doc = globalThis.document, onGroup = () => {} } = {}) {
  const ok = new Set(), on = new Set(), keep = [];
  const Img = doc && doc.defaultView && doc.defaultView.Image;
  for (const [group, names] of Object.entries(KIT_GROUPS)) {
    for (const name of names) {
      if (!Img) break;
      const im = new Img();
      im.decoding = 'async';
      im.onload = () => {
        ok.add(name);
        if (!on.has(group) && names.every((x) => ok.has(x))) { on.add(group); onGroup(group); }
      };
      im.onerror = () => { /* this piece stays on the DOM look */ };
      im.src = kitUrl(name);
      keep.push(im);
    }
  }
  return {
    ok: (name) => ok.has(name),
    group: (g) => on.has(g),
    url: kitUrl,
    dispose() { keep.length = 0; },
  };
}
