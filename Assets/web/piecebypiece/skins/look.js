/* ============================================================================
 * skins/look.js - what a player's men wear, as plain data (no three.js here,
 * so node can test it).
 *
 * A look is three slots, each one Prize Counter item id or null:
 *
 *   { set: 'chess.set.royal-capes', banner: 'chess.banner.crest', insignia: null }
 *
 * The art for every id is an attachment per piece type (assets/skins/, from
 * Astra's preview/chess tree): it hangs off the approved sculpted piece at its
 * socket and never replaces it. The pink/white silicone stays the side colour.
 *
 * Ownership is the account's grants, `cos.<itemId>` (Prize Counter contract 2).
 * Prime items stay usable after Prime ends: only buying needs Prime, so a
 * grant is the whole test here. The server clamps a look again on the wire
 * (contract 4); this side only refuses ids it has no art for.
 * ==========================================================================*/

export const SLOTS = Object.freeze(['set', 'banner', 'insignia']);

/** Every id this page can draw, by slot. Order is the picker's order. */
export const CATALOG = Object.freeze({
  set: Object.freeze(['chess.set.royal-capes', 'chess.set.velvet-court']),
  banner: Object.freeze(['chess.banner.house-pink', 'chess.banner.crest', 'prime.banner.arena']),
  insignia: Object.freeze(['chess.insignia.heart', 'chess.insignia.star']),
});

/** Shelf names, the Prize Counter's own words (prizecounter/state/strings.js). */
export const NAMES = Object.freeze({
  'chess.set.royal-capes': 'The Spoiled Royals',
  'chess.set.velvet-court': 'The Obedient Court',
  'chess.banner.house-pink': 'House Banner',
  'chess.banner.crest': 'Crest Banner',
  'prime.banner.arena': 'Arena Banner',
  'chess.insignia.heart': 'Heart Insignia',
  'chess.insignia.star': 'Star Insignia',
});

export const SLOT_WORDS = Object.freeze({ set: 'Clothes', banner: 'Banner', insignia: 'Insignia' });

/** Items the counter sells only to Prime. Owned ones stay usable after Prime ends. */
export const PRIME_ONLY = Object.freeze(['prime.banner.arena']);

export const EMPTY = Object.freeze({ set: null, banner: null, insignia: null });

export const grantOf = (id) => 'cos.' + id;

/** The slot an id belongs to, or null for an id with no art here. */
export function slotOf(id) {
  for (const s of SLOTS) if (CATALOG[s].includes(id)) return s;
  return null;
}

/** Any input to a whole look: unknown ids, wrong slots and junk become null. */
export function normalLook(raw) {
  const out = { set: null, banner: null, insignia: null };
  if (!raw || typeof raw !== 'object') return out;
  for (const s of SLOTS) {
    const id = raw[s];
    if (typeof id === 'string' && CATALOG[s].includes(id)) out[s] = id;
  }
  return out;
}

/** The item ids the grants own, catalog ids only. */
export function ownedIds(grants) {
  const have = new Set();
  if (!grants || typeof grants[Symbol.iterator] !== 'function') return have;
  const g = new Set();
  for (const x of grants) if (typeof x === 'string') g.add(x);
  for (const s of SLOTS) for (const id of CATALOG[s]) if (g.has(grantOf(id))) have.add(id);
  return have;
}

/** The look with every unowned slot taken off. */
export function clampLook(look, grants) {
  const have = ownedIds(grants);
  const n = normalLook(look);
  for (const s of SLOTS) if (n[s] && !have.has(n[s])) n[s] = null;
  return n;
}

export const isEmpty = (look) => SLOTS.every((s) => !look || !look[s]);

export function sameLook(a, b) {
  const x = normalLook(a), y = normalLook(b);
  return SLOTS.every((s) => x[s] === y[s]);
}

/** The ids a look draws, set first (it sits under the banner and insignia). */
export const lookIds = (look) => SLOTS.map((s) => normalLook(look)[s]).filter(Boolean);

/** The seat/join body's `look`, or nothing for a bare look (an old server never sees the field). */
export function wireLook(look) {
  const n = normalLook(look);
  return isEmpty(n) ? null : n;
}

/** Page -> host: open the Prize Counter on the chess tab (contract 1). */
export const counterFrame = () => ({ type: 'open-prize-counter', tab: 'chess', from: 'piecebypiece' });
