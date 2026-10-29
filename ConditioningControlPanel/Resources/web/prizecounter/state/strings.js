/* strings.js - every user-facing string of the Prize Counter, as `shop_*` loc keys with the English fallback.
 * The host's `init.lexicon` wins. The ten language files carry the same keys; tests/hud-loc.test.mjs keeps them in step. */

export const LEX = {
  shop_title: 'Prize Counter',
  shop_loading: 'Opening the counter.',
  shop_offline: 'The counter did not answer. Try again in a moment.',
  shop_signed_out: 'Sign in to shop at the counter.',
  shop_sign_in: 'Sign in to shop',
  shop_wallet: '{0} Sparkles',
  shop_price: '{0} Sparkles',
  shop_after: 'Balance after: {0}',
  shop_buy: 'Buy',
  shop_keep_looking: 'Keep looking',
  shop_wear: 'Wear',
  shop_owned: 'Owned',
  shop_prime: 'Prime',
  shop_prime_reason: 'Prime members can buy this one.',
  shop_short: 'Short by {0} Sparkles',
  shop_soon: 'On the shelf soon.',
  shop_gone: 'Gone from this week\'s shelf.',
  shop_pending: 'Ringing it up.',
  shop_note_retry: 'The counter did not answer. Confirm again to retry.',
  shop_note_signed_out: 'Signed out. Sign in and confirm again.',
  shop_note_changed: 'The shelf changed. Check the price and confirm again.',
  shop_note_try_again: 'That did not go through. Confirm again.',
  shop_week: 'wk {0}',
  shop_back: 'Back',
  shop_tabs: 'Shelves',
  shop_tab_rain_clothes: 'Rain: clothes',
  shop_tab_rain_hair: 'Rain: hair',
  shop_tab_kart: 'Kart',
  shop_tab_chess: 'Chess',
  shop_tab_profile: 'Profile',
  shop_tab_desk: 'Desk',
  shop_tab_prime: 'Prime',
  shop_tab_backroom: 'Back Room',
};

export const itemKey = (id) => `shop_item_${String(id).replace(/[.-]/g, '_')}_name`;
const ITEM_NAMES = {
  'rain.dress.midnight-cocktail': 'Midnight cocktail', 'rain.dress.rose-flare': 'Rose flare', 'rain.dress.ivory-midi': 'Ivory midi',
  'rain.dress.plum-asymmetric': 'Plum asymmetric', 'rain.dress.scarlet-bell': 'Scarlet bell', 'rain.dress.teal-tulip': 'Teal tulip',
  'rain.dress.black-highlow': 'Black high-low', 'rain.suit.hazmat-yellow': 'Yellow hazmat suit', 'rain.hair.chestnut-bob': 'Chestnut bob',
  'rain.hair.copper-lob': 'Copper lob', 'rain.hair.ink-sleek': 'Sleek ink', 'rain.hair.rose-waves': 'Rose waves',
  'rain.hair.platinum-long': 'Long platinum', 'rain.hair.auburn-volume': 'Auburn volume', 'rain.acc.silver-drops': 'Silver drops',
  'rain.acc.cat-eye-frames': 'Cat-eye frames',
  'kart.outfit.pit-crew': 'Pit crew outfit', 'kart.outfit.ringmaster': 'Ringmaster outfit', 'kart.outfit.ballerina': 'Ballerina outfit',
  'kart.umbrella.lace': 'Lace umbrella', 'chess.set.royal-capes': 'Royal capes set', 'chess.set.velvet-court': 'Velvet court set',
  'chess.banner.house-pink': 'House pink banner', 'chess.banner.crest': 'Crest banner', 'chess.insignia.heart': 'Heart insignia',
  'chess.insignia.star': 'Star insignia', 'profile.frame.lace': 'Lace frame', 'profile.frame.ink-stamp': 'Ink stamp frame',
  'profile.frame.gilt': 'Gilt frame', 'profile.frame.polaroid': 'Polaroid frame', 'profile.avatar.emi-pixel': 'Pixel EMI',
  'profile.avatar.rain-portrait': 'Rain portrait', 'desk.hat.top-hat': 'Top hat', 'desk.hat.party': 'Party hat',
  'desk.toy.rubber-duck': 'Rubber duck', 'desk.toy.snow-globe': 'Snow globe', 'prime.paint.host-gold': 'Host gold paint',
  'prime.trail.podium': 'Podium trail', 'prime.frame.room-host': 'Room host frame', 'prime.banner.arena': 'Arena banner',
  jackpot_remix: 'Jackpot Remix', rt_demo: 'Racing Thoughts Demo', high_roller: 'High Roller', flashes_v2: 'Flashes v2',
  bubbles_v2: 'Bubbles v2', rt_bundle_1: 'Racing Thoughts Bundle 1', rt_bundle_2: 'Racing Thoughts Bundle 2',
  rt_bundle_3: 'Racing Thoughts Bundle 3',
  // Catalog revision 2 (Astra's art pass 2, names as on her tags).
  'kart.body.petal': 'The Blushing Petal', 'kart.body.trinket': 'The Precious Trinket',
  'kart.body.pendulum': 'The Dizzy Pendulum', 'kart.body.checkmate': 'The Cheeky Checkmate',
  'kart.body.lovebomb': 'The Smitten Lovebomb', 'kart.body.undertow': 'The Dreamy Undertow',
  'kart.body.feedback': 'The Rowdy Feedback', 'kart.body.velvet-lock': 'The Velvet Lock',
  'kart.body.reverie': 'The Drowsy Reverie', 'kart.body.house-edge': 'The Lucky House Edge',
  'kart.paint.pattern-bundle': 'The Pattern Parade', 'rain.suit.action-yellow': 'The Vengeful Bride',
  'rain.acc.hazmat-mask': 'The Lab Partner', 'rain.acc.back-katana': 'The Last Word',
};
for (const [id, name] of Object.entries(ITEM_NAMES)) LEX[itemKey(id)] = name;

/** A translator over the host lexicon: t(key, ...args) fills {0}, {1}. Unknown keys answer the key itself. */
export function makeT(lexicon) {
  const lex = lexicon && typeof lexicon === 'object' ? lexicon : {};
  return (key, ...args) => String(lex[key] ?? LEX[key] ?? key).replace(/\{(\d)\}/g, (m, i) => (args[i] ?? m));
}

export const tabKey = (id) => `shop_tab_${String(id).replace(/-/g, '_')}`;
/** An item name: `shop_item_<id>_name` when the lexicon has it, else the last part of the id in plain words. */
export function itemName(t, id) {
  const key = itemKey(id);
  const v = t(key);
  return v !== key ? v : String(id).split('.').pop().replace(/[-_]/g, ' ');
}
