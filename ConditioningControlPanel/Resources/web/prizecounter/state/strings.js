/* strings.js - every user-facing string of the Prize Counter, as `shop_*` loc keys with the English fallback.
 * The host's `init.lexicon` wins; the WPF lane carries these keys into the ten language files. */

export const LEX = {
  shop_title: 'Prize Counter',
  shop_loading: 'Opening the counter.',
  shop_offline: 'The counter did not answer. Try again in a moment.',
  shop_signed_out: 'Sign in to shop at the counter.',
  shop_wallet: '{0} Sparkles',
  shop_price: '{0} Sparkles',
  shop_buy: 'Buy',
  shop_keep_looking: 'Keep looking',
  shop_wear: 'Wear',
  shop_owned: 'Owned',
  shop_prime: 'Prime',
  shop_prime_reason: 'Prime members can buy this one.',
  shop_short: 'Short by {0} Sparkles',
  shop_soon: 'On the shelf soon.',
  shop_gone: 'Gone from this week\'s shelf.',
  shop_note_retry: 'The counter did not answer. Confirm again to retry.',
  shop_note_signed_out: 'Signed out. Sign in and confirm again.',
  shop_note_changed: 'The shelf changed. Check the price and confirm again.',
  shop_note_try_again: 'That did not go through. Confirm again.',
  shop_tab_rain_clothes: 'Rain: clothes',
  shop_tab_rain_hair: 'Rain: hair',
  shop_tab_kart: 'Kart',
  shop_tab_chess: 'Chess',
  shop_tab_profile: 'Profile',
  shop_tab_desk: 'Desk',
  shop_tab_prime: 'Prime',
  shop_tab_backroom: 'Back Room',
};

/** A translator over the host lexicon: t(key, ...args) fills {0}, {1}. Unknown keys answer the key itself. */
export function makeT(lexicon) {
  const lex = lexicon && typeof lexicon === 'object' ? lexicon : {};
  return (key, ...args) => String(lex[key] ?? LEX[key] ?? key).replace(/\{(\d)\}/g, (m, i) => (args[i] ?? m));
}

export const tabKey = (id) => `shop_tab_${String(id).replace(/-/g, '_')}`;
/** An item name: `shop_item_<id>` when the lexicon has it, else the last part of the id in plain words. */
export function itemName(t, id) {
  const key = `shop_item_${String(id).replace(/[.-]/g, '_')}`;
  const v = t(key);
  return v !== key ? v : String(id).split('.').pop().replace(/[-_]/g, ' ');
}
