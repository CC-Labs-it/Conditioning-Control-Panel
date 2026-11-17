/* sheet/strings.js - the few words the 2D counter sheet adds over state/strings.js (English fallbacks only).
 * They live here, not in LEX, until the ten language files carry them (tests/hud-loc.test.mjs pins LEX to en.json).
 * The host's lexicon still wins: sheetT() = makeT over these, then the host's words. */

import { makeT } from '../state/strings.js';

export const SHEET_LEX = {
  shop_sheet_search: 'Search items',
  shop_sheet_no_match: 'Nothing on the shelves matches that.',
  shop_sheet_none_owned: 'Nothing owned yet.',
  shop_sheet_pick: 'Pick an item to try it on.',
  shop_sheet_wearing: 'Wearing',
  shop_sheet_prime_only: 'Prime only',
  shop_sheet_ask: 'Spend {0} Sparkles on this?',
  shop_sheet_yes_buy: 'Yes, buy for {0}',
  shop_sheet_cancel: 'Cancel',
};

export const sheetT = (lexicon, companion) => makeT({ ...SHEET_LEX, ...(lexicon && typeof lexicon === 'object' ? lexicon : {}) }, companion);
