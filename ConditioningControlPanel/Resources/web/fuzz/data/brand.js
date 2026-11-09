/* data/brand.js - the ONE place that names the game and the currency. The game is Terms & Conditioning, T&C for short
 * (owner, 2026-11-02). The currency is still a placeholder. Every player-facing string uses the tokens {game}, {short}
 * and {cur} and passes through brandText(). A test fails if the currency's name appears anywhere else. */

import { stripMarks } from '../ui/rich-text.js';

export const BRAND = Object.freeze({
  game: 'Terms & Conditioning',
  short: 'T&C',
  currency: 'Fuzz',
  currencyPlural: 'Fuzz',
});

/** The tokens filled, markups kept: ui/rich-text.js draws them. */
export function brandTokens(s) {
  return String(s ?? '').replace(/\{game\}/g, BRAND.game).replace(/\{short\}/g, BRAND.short).replace(/\{cur\}/g, BRAND.currencyPlural);
}

/** Plain player-facing words: the tokens filled and the copy markups (**keyword**, [[pink:word]]) removed. Drawn copy
 * goes through ui/rich-text.js instead, which keeps them as coloured spans. */
export function brandText(s) {
  return stripMarks(brandTokens(s));
}
