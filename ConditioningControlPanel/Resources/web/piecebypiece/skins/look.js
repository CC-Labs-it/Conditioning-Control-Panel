/* ============================================================================
 * skins/look.js - what a player's men wear, as plain data (no three.js here,
 * so node can test it).
 *
 * A look carries three attachment slots and an optional piece finish:
 *
 *   { set: 'chess.set.royal-capes', banner: 'chess.banner.crest', insignia: null }
 *
 * Clothes and insignias hang off each approved sculpt at its socket. Banners
 * stand on their side's two outer board corners. Art lives in assets/skins/;
 * finishes color the sculpted body independently of those attachments.
 *
 * Ownership is the account's grants, `cos.<itemId>` (Prize Counter contract 2).
 * Prime items stay usable after Prime ends: only buying needs Prime, so a
 * grant is the whole test here. The server clamps a look again on the wire
 * (contract 4); this side only refuses ids it has no art for.
 * ==========================================================================*/

import { FINISH_IDS, FINISH_NAMES } from './finish-ids.js';
import { BOARD_THEME_IDS, BOARD_THEME_NAMES } from '../board/theme-ids.js';

const ATTACHMENT_SLOTS = Object.freeze(['set', 'banner', 'insignia']);
export const SLOTS = Object.freeze([...ATTACHMENT_SLOTS, 'finish', 'board', 'companion']);

/** Every id this page can draw, by slot. Order is the picker's order. */
export const CATALOG = Object.freeze({
  set: Object.freeze(['chess.set.royal-capes', 'chess.set.velvet-court']),
  banner: Object.freeze(['chess.banner.house-pink', 'chess.banner.crest', 'prime.banner.arena']),
  insignia: Object.freeze(['chess.insignia.heart', 'chess.insignia.star']),
  finish: FINISH_IDS,
  board: BOARD_THEME_IDS,
  companion: Object.freeze(['chess.companion.emi']),
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
  ...FINISH_NAMES,
  ...BOARD_THEME_NAMES,
  'chess.companion.emi': 'Emi cheerleader',
});

export const SLOT_WORDS = Object.freeze({
  set: 'Clothes', banner: 'Banner', insignia: 'Insignia',
  finish: 'Piece finish', board: 'Board', companion: 'Cheerleader',
});

/** Items the counter sells only to Prime. Owned ones stay usable after Prime ends. */
export const PRIME_ONLY = Object.freeze(['prime.banner.arena']);

export const EMPTY = Object.freeze({ set: null, banner: null, insignia: null });

export const grantOf = (id) => 'cos.' + id;

/** The slot an id belongs to, or null for an id with no art here. */
export function slotOf(id) {
  for (const s of SLOTS) if (CATALOG[s].includes(id)) return s;
  return null;
}

/** Missing or invalid finishes preserve the original three-slot shape on disk and on the wire. */
function readLook(raw, owned) {
  const out = { ...EMPTY };
  if (!raw || typeof raw !== 'object') return out;
  for (const s of SLOTS) {
    const id = raw[s];
    if (CATALOG[s].includes(id) && (!owned || owned.has(id))) out[s] = id;
  }
  return out;
}

export const normalLook = (raw) => readLook(raw, null);

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
  return readLook(look, ownedIds(grants));
}

export const isEmpty = (look) => SLOTS.every((s) => !look || !look[s]);

export function sameLook(a, b) {
  const x = normalLook(a), y = normalLook(b);
  return SLOTS.every((s) => x[s] === y[s]);
}

/** Only these ids have attachment GLBs. Finishes are painted on the body. */
export function lookIds(look) {
  const n = normalLook(look);
  return ATTACHMENT_SLOTS.map((s) => n[s]).filter(Boolean);
}

/** The seat/join body's `look`, or nothing for a bare look (an old server never sees the field). */
export function wireLook(look) {
  const n = normalLook(look);
  return isEmpty(n) ? null : n;
}

/** Page -> host: open the Prize Counter on the chess tab (contract 1). */
export const counterFrame = () => ({ type: 'open-prize-counter', tab: 'chess', from: 'piecebypiece' });
