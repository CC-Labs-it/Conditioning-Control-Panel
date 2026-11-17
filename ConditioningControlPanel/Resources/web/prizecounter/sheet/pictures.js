/* sheet/pictures.js - item pictures for the things that only existed as 3D shelf models (karts, chess, desk toys, Prime
 * and Back Room prizes). Each is a small transparent sticker rendered offline from that item's approved model by
 * Scripts/render-shop-item-pictures.mjs into sheet/art/items/<id>.webp. An id not listed keeps today's art
 * (model.js artOf: a flat picture where the tree has one, else the family glyph). */

import { artOf } from './model.js';

export const PICTURED = Object.freeze([
  'kart.body.petal', 'kart.body.trinket', 'kart.body.pendulum', 'kart.body.checkmate', 'kart.body.lovebomb',
  'kart.body.undertow', 'kart.body.feedback', 'kart.body.velvet-lock', 'kart.body.reverie', 'kart.body.house-edge',
  'kart.outfit.pit-crew', 'kart.outfit.ringmaster', 'kart.outfit.ballerina', 'kart.umbrella.lace',
  'chess.set.royal-capes', 'chess.set.velvet-court', 'chess.banner.house-pink', 'chess.banner.crest',
  'chess.insignia.heart', 'chess.insignia.star',
  'desk.hat.top-hat', 'desk.hat.party', 'desk.toy.rubber-duck', 'desk.toy.snow-globe',
  'prime.trail.podium', 'prime.banner.arena',
  'jackpot_remix', 'rt_demo', 'high_roller', 'flashes_v2', 'bubbles_v2', 'rt_bundle_1', 'rt_bundle_2', 'rt_bundle_3',
]);
const HAS = new Set(PICTURED);

/** The picture file for an item id (relative to the prizecounter root), or null. */
export const pictureOf = (id) => (HAS.has(id) ? `sheet/art/items/${id}.webp` : null);

/** artOf with the rendered pictures first: { kind: 'pic', src } for a pictured id, else exactly artOf's answer. */
export function itemArt(id, index, family, base = './') {
  const p = pictureOf(id);
  return p ? { kind: 'pic', src: base + p } : artOf(id, index, family, base);
}
