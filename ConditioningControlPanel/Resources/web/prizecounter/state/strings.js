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

export const descKey = (id) => itemKey(id).replace(/_name$/, '_desc');
/* One short factual line per item (Astra's item-info.json, trimmed), shown on the preview strip and the confirm card.
 * A family + slot key (`shop_item_kart_body_desc`) covers every row of that slot without a key of its own. */
const ITEM_DESCS = {
  "shop_item_kart_body_desc": "A kart body for EMI in Racing Thoughts. Outfits and paint equip separately.",
  "shop_item_kart_outfit_pit_crew_desc": "Pit crew uniform with a cap for EMI in Racing Thoughts. The kart is separate.",
  "shop_item_kart_outfit_ringmaster_desc": "Ringmaster outfit with a top hat for EMI in Racing Thoughts. The kart is separate.",
  "shop_item_kart_outfit_ballerina_desc": "Ballet outfit with a tutu for EMI in Racing Thoughts. The kart is separate.",
  "shop_item_kart_paint_pattern_bundle_desc": "Five paint patterns for EMI in one item: stripes, polka dots, checks, hearts and spirals.",
  "shop_item_kart_umbrella_lace_desc": "Lace parasol for the kart in Racing Thoughts.",
  "shop_item_rain_dress_midnight_cocktail_desc": "Blue cocktail dress for Rain.",
  "shop_item_rain_dress_rose_flare_desc": "Pink flared dress for Rain.",
  "shop_item_rain_dress_ivory_midi_desc": "Ivory midi dress for Rain.",
  "shop_item_rain_dress_plum_asymmetric_desc": "Plum asymmetric dress for Rain.",
  "shop_item_rain_dress_scarlet_bell_desc": "Scarlet bell dress for Rain.",
  "shop_item_rain_dress_teal_tulip_desc": "Teal tulip dress for Rain.",
  "shop_item_rain_dress_black_highlow_desc": "Black dress with a high-low hem for Rain.",
  "shop_item_rain_suit_hazmat_yellow_desc": "Yellow hazmat suit for Rain. The respirator is a separate item.",
  "shop_item_rain_suit_action_yellow_desc": "Yellow suit with black side stripes for Rain. The katana is a separate item.",
  "shop_item_rain_acc_back_katana_desc": "Katana and scabbard worn on Rain's back. Equips separately from clothing.",
  "shop_item_rain_acc_hazmat_mask_desc": "Respirator mask for Rain. Equips separately from the hazmat suit.",
  "shop_item_rain_acc_cat_eye_frames_desc": "Cat-eye glasses for Rain.",
  "shop_item_rain_acc_silver_drops_desc": "Silver drop earrings for Rain.",
  "shop_item_rain_hair_chestnut_bob_desc": "Chestnut bob for Rain. Shown on a display head.",
  "shop_item_rain_hair_copper_lob_desc": "Copper long bob for Rain. Shown on a display head.",
  "shop_item_rain_hair_ink_sleek_desc": "Sleek black hairstyle for Rain. Shown on a display head.",
  "shop_item_rain_hair_rose_waves_desc": "Pink wavy hairstyle for Rain. Shown on a display head.",
  "shop_item_rain_hair_platinum_long_desc": "Long platinum hairstyle for Rain. Shown on a display head.",
  "shop_item_rain_hair_auburn_volume_desc": "Full auburn hairstyle for Rain. Shown on a display head.",
};
Object.assign(LEX, ITEM_DESCS);

/** A translator over the host lexicon: t(key, ...args) fills {0}, {1}. Unknown keys answer the key itself. */
export function makeT(lexicon) {
  const lex = lexicon && typeof lexicon === 'object' ? lexicon : {};
  return (key, ...args) => String(lex[key] ?? LEX[key] ?? key).replace(/\{(\d)\}/g, (m, i) => (args[i] ?? m));
}

export const tabKey = (id) => `shop_tab_${String(id).replace(/-/g, '_')}`;
/** An item name: `shop_item_<id>_name` when the lexicon has it, else the last part of the id in plain words. */
/** An item's description, or null: `shop_item_<id>_desc`, else the family + slot key. */
export function itemDesc(t, id) {
  for (const key of [descKey(id), descKey(String(id).split('.').slice(0, 2).join('.'))]) {
    const v = t(key);
    if (v !== key) return v;
  }
  return null;
}

export function itemName(t, id) {
  const key = itemKey(id);
  const v = t(key);
  return v !== key ? v : String(id).split('.').pop().replace(/[-_]/g, ' ');
}
