/* sheet/shown.js - where a bought thing shows once it is on, and the button that takes the user there (owner
 * 2026-11-17: "where do I find this profile frame? ... did I just buy nothing?"). Pure, keyed by the family of
 * sheet/explain.js. The fitting room draws the line as soon as the item is worn (a kart: as soon as it is owned, it is
 * put on in the garage); the button only when the host lists the place in init.goes (CONTRACT "Sheet additions").
 * The host's place keys: Services/PrizeCounter/ShownPlaces.cs. */

import { familyOf } from './explain.js';

const PROFILE = { line: 'shop_sheet_shown_profile', go: 'profile', btn: 'shop_sheet_go_profile' };
const DESK = { line: 'shop_sheet_shown_desk', go: 'desk', btn: 'shop_sheet_go_desk' };
const KART = { line: 'shop_sheet_shown_kart', go: 'race', btn: 'shop_sheet_go_kart', owned: true };
const CHESS = { line: 'shop_sheet_shown_chess', go: 'chess', btn: 'shop_sheet_go_chess' };
const DECK = { line: 'shop_sheet_shown_deck', go: 'flash', btn: 'shop_sheet_go_deck' };

/** Family -> { line, go, btn, owned? } (owned: the line shows once owned, not only once worn). No row = no line. */
export const SHOWN = Object.freeze({
  profile_frame: PROFILE, profile_avatar: PROFILE,
  desk_hat: DESK, desk_toy: DESK,
  kart_body: KART, kart_outfit: KART, kart_paint: KART, kart_umbrella: KART, kart_trail: KART,
  chess_set: CHESS, chess_finish: CHESS, chess_board: CHESS, chess_companion: CHESS, chess_banner: CHESS, chess_insignia: CHESS,
  fx_bubble: { line: 'shop_sheet_shown_bubble', go: 'bubbles', btn: 'shop_sheet_go_bubble' },
  fx_card: DECK, fx_behind: DECK,
  fx_word: { line: 'shop_sheet_shown_word', go: 'subliminal', btn: 'shop_sheet_go_word' },
  fx_fog: { line: 'shop_sheet_shown_fog', go: 'pinkfilter', btn: 'shop_sheet_go_fog' },
});

/** The keys for an entry (sheet/model.js rowsOf): { line, go, btn, owned } or null (Rain opens its own room, Back Room
 * prizes have no one place). */
export const shownKeys = (e) => SHOWN[familyOf(e)] || null;

/** The fitting room's slip for an entry. on = it is worn (or switched on) now. goes = init.goes (absent = no button).
 * -> { text, go?, label? } or null. */
export function shownOf(e, t, { on = false, goes = null } = {}) {
  const k = shownKeys(e);
  if (!k || !e || !e.row || !e.row.owned || !(on || k.owned)) return null;
  const out = { text: t(k.line) };
  if (Array.isArray(goes) && goes.includes(k.go)) { out.go = k.go; out.label = t(k.btn); }
  return out;
}

/** English for the keys above (sheet/strings.js merges it into SHEET_LEX). */
export const SHOWN_LEX = {
  shop_sheet_shown_profile: 'On your profile, the friends list and lobby rows.',
  shop_sheet_go_profile: 'See it on your profile',
  shop_sheet_shown_desk: 'On EMI\'s desk.',
  shop_sheet_go_desk: 'Show EMI\'s desk',
  shop_sheet_shown_kart: 'Put it on in the Racing Thoughts garage.',
  shop_sheet_go_kart: 'Open Racing Thoughts',
  shop_sheet_shown_chess: 'In Piece by Piece, on the board.',
  shop_sheet_go_chess: 'Open Piece by Piece',
  shop_sheet_shown_bubble: 'On every Bubble Pop bubble.',
  shop_sheet_go_bubble: 'Open Bubble Pop',
  shop_sheet_shown_deck: 'On Deck cards, while Deck is on in Flashes.',
  shop_sheet_go_deck: 'Open Flashes',
  shop_sheet_shown_word: 'On every subliminal.',
  shop_sheet_go_word: 'Open Subliminals',
  shop_sheet_shown_fog: 'On the pink filter and Steam.',
  shop_sheet_go_fog: 'Open the pink filter',
};
