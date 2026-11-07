/* render/wardrobeMath.js - the three-free wardrobe law (ART5), so tests run it under node.
 *
 * Astra's eight bot outfits (art/models/emi-bot-wardrobe.json) and two vehicle variants
 * (art/models/emi-vehicle-variants.json) ride the accepted R4 kart rig: they reuse its 15 bones by exact
 * name and their own inverse binds, and own no clips. A look is { outfit, vehicle }: the json ids, or null
 * for EMI's own (no outfit, the original teacup). lookForSeat picks a stable look per seat; a remote
 * player's chosen ids come in through `want` and an unknown id falls back to the default look.
 */

/** The Prize Counter's kart pieces (lane B3-G1): worn only by a player who owns them, never picked for a bot. */
export const SHOP_PIECES = Object.freeze({
  outfits: Object.freeze(['emi-outfit-pit-crew', 'emi-outfit-ringmaster', 'emi-outfit-ballerina']),
  umbrellas: Object.freeze({ lace: 'umbrella-lace' }),
  trails: Object.freeze({ podium: 'podium-trail' }),
  paints: Object.freeze(['hearts', 'spiral', 'host-gold']),
});

/** The catalog from the two jsons (either may be null: then it has no entries). */
export function catalogFrom(wardrobe, variants) {
  const base = (wardrobe && wardrobe.baseSha256) || (variants && variants.baseSha256) || null;
  const okBase = (j) => !!j && (!base || j.baseSha256 === base);
  return {
    base,
    joints: ((wardrobe && wardrobe.jointNames) || (variants && variants.jointNames) || []).slice(),
    outfits: okBase(wardrobe) ? (wardrobe.outfits || []).map((o) => o.id) : [],
    shopOutfits: SHOP_PIECES.outfits.slice(),
    vehicles: okBase(variants) ? (variants.vehicles || []).map((v) => v.id) : [],
    removeJoints: (variants && variants.replacement && variants.replacement.removedJointNames) || [],
    removedTriangles: (variants && variants.replacement && variants.replacement.removedTriangles) || 0,
  };
}

/** A small stable hash of a seed (any number or string) -> 0..2^31. */
export function seedHash(seed) {
  const s = String(seed ?? 0);
  let h = 2166136261;
  for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619); }
  return (h >>> 0) % 2147483647;
}

export const DEFAULT_LOOK = Object.freeze({ outfit: null, vehicle: null });

/**
 * The look for a seat. want (a remote player's ids, or a menu pick): each field kept when the catalog knows
 * it, else null. No want: seat 0 is EMI herself; bots 1..n wear distinct outfits in a seed-rotated order,
 * and about a third each keep the teacup, ride the Petal or the Trinket.
 */
export function lookForSeat(seat, seed, catalog, want = undefined) {
  const C = catalog || { outfits: [], vehicles: [] };
  if (want !== undefined && want !== null) {
    const shop = C.shopOutfits || [];
    return {
      outfit: C.outfits.includes(want.outfit) || shop.includes(want.outfit) ? want.outfit : null,
      vehicle: C.vehicles.includes(want.vehicle) ? want.vehicle : null,
      ...shopExtras(want),
    };
  }
  if (!(seat > 0)) return { ...DEFAULT_LOOK };
  const h = seedHash(seed), n = C.outfits.length, v = C.vehicles.length;
  const outfit = n ? C.outfits[(seat - 1 + h) % n] : null;
  const k = v ? (seat + (h >> 3)) % (v + 1) : 0;
  return { outfit, vehicle: k ? C.vehicles[k - 1] : null };
}

/** The shop extras of a want (paint, umbrella, trail) that this build knows; unknown ones are dropped. */
export function shopExtras(want) {
  const out = {};
  if (!want || typeof want !== 'object') return out;
  if (SHOP_PIECES.paints.includes(want.paint)) out.paint = want.paint;
  if (Object.hasOwn(SHOP_PIECES.umbrellas, String(want.umbrella))) out.umbrella = want.umbrella;
  if (Object.hasOwn(SHOP_PIECES.trails, String(want.trail))) out.trail = want.trail;
  return out;
}

/** True when names is exactly the joint set (order free). */
export function sameJoints(names, joints) {
  if (!names || names.length !== joints.length) return false;
  const a = [...names].sort(), b = [...joints].sort();
  return a.every((x, i) => x === b[i]);
}

/**
 * The vehicle cut (variants json "replacement"): drop a triangle when all three vertices put weight 1 on the
 * removed joints (cup_body, saucer_body). index, joints (4 per vertex), weights (4 per vertex) are typed
 * arrays; remove = the joint indices. Returns { index (same type), removed, mixed } (mixed = triangles with
 * some vertices removable and some not; the contract says 0).
 */
export function cutIndex(index, joints, weights, remove, eps = 1e-3) {
  const rm = new Set(remove), vertexGone = (v) => {
    let w = 0;
    for (let j = 0; j < 4; j++) if (rm.has(joints[v * 4 + j])) w += weights[v * 4 + j];
    return w >= 1 - eps;
  };
  const keep = [];
  let removed = 0, mixed = 0;
  for (let t = 0; t < index.length; t += 3) {
    const g = (vertexGone(index[t]) ? 1 : 0) + (vertexGone(index[t + 1]) ? 1 : 0) + (vertexGone(index[t + 2]) ? 1 : 0);
    if (g === 3) { removed++; continue; }
    if (g) mixed++;
    keep.push(index[t], index[t + 1], index[t + 2]);
  }
  const Out = index.constructor;
  return { index: new Out(keep), removed, mixed };
}

/** Phone tier: the vehicles drop to the teacup (fewer triangles and draws); outfits stay, they name the rival. */
export function lookForTier(look, tierName) {
  return tierName === 'phone' ? { ...look, vehicle: null } : look;
}

/** The lace umbrella's IN and OUT (desk run 2026-10-30, it popped in and out): seconds to open and to fold, longer
 *  under Gentle. */
export const UMBRELLA_TWEEN = Object.freeze({ openS: 0.3, foldS: 0.2, gentleOpenS: 0.5, gentleFoldS: 0.38 });

/** One step of the umbrella's openness u (0 folded away, 1 open) toward `on`. */
export function umbrellaStep(u, on, dt, gentle = false) {
  const T = UMBRELLA_TWEEN, s = on ? (gentle ? T.gentleOpenS : T.openS) : (gentle ? T.gentleFoldS : T.foldS);
  const d = Math.max(0, dt || 0) / s;
  return on ? Math.min(1, u + d) : Math.max(0, u - d);
}

/** The canopy at openness u: { radial, height } scales about the handle's foot. Opening glides out with a small
 *  overshoot (ease-out-back; Gentle a plain ease-out, no overshoot); folding starts slow and snaps shut. u = 1 is
 *  exactly { 1, 1 } either way. */
export function umbrellaShape(u, opening, gentle = false) {
  const t = Math.min(1, Math.max(0, u)), r = t - 1;
  const e = !opening ? 1 - r * r : gentle ? 1 - Math.abs(r * r * r) : 1 + 2.6 * r * r * r + 1.6 * r * r;
  return { radial: 0.08 + 0.92 * e, height: 0.5 + 0.5 * Math.min(1, e) };
}
