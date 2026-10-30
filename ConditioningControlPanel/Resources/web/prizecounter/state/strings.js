/* strings.js - every user-facing string of the Prize Counter, as `shop_*` loc keys with the English fallback.
 * The host's `init.lexicon` wins. The ten language files carry the same keys; tests/hud-loc.test.mjs keeps them in step. */

export const LEX = {
  shop_title: 'Prize Counter',
  shop_loading: 'Opening the counter.',
  shop_offline: 'The counter did not answer. Try again in a moment.',
  shop_signed_out: 'Sign in to shop at the counter.',
  shop_sign_in: 'Sign in to shop',
  shop_locked_in_counter: 'In the Prize Counter',
  shop_wallet: '{0} Sparkles',
  shop_price: '{0} Sparkles',
  shop_after: 'Balance after: {0}',
  shop_buy: 'Buy',
  shop_keep_looking: 'Keep looking',
  shop_wear: 'Wear',
  shop_wear_in_garage: 'Wear it in the garage',
  shop_worn: 'Wearing it now.',
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
  shop_tab_rain_clothes: '{companion}: clothes',
  shop_tab_rain_hair: '{companion}: hair',
  shop_tab_kart: 'Kart',
  shop_tab_chess: 'Chess',
  shop_tab_profile: 'Profile',
  shop_tab_desk: 'Desk',
  shop_tab_prime: 'Prime',
  shop_tab_backroom: 'Back Room',
};

export const itemKey = (id) => `shop_item_${String(id).replace(/[.-]/g, '_')}_name`;
const ITEM_NAMES = {
  'rain.dress.midnight-cocktail': 'The Dreamy Dress', 'rain.dress.rose-flare': 'The Blushing Belle', 'rain.dress.ivory-midi': 'The Innocent Alibi',
  'rain.dress.plum-asymmetric': 'The Tipsy Tulip', 'rain.dress.scarlet-bell': 'The Flustered Frock', 'rain.dress.teal-tulip': 'The Dazed Darling',
  'rain.dress.black-highlow': 'Black High-Low Dress', 'rain.suit.hazmat-yellow': 'The Chemistry Teacher', 'rain.hair.chestnut-bob': 'The Bashful Bob',
  'rain.hair.copper-lob': 'Copper Lob', 'rain.hair.ink-sleek': 'Ink Sleek', 'rain.hair.rose-waves': 'Rose Waves',
  'rain.hair.platinum-long': 'Platinum Long', 'rain.hair.auburn-volume': 'Auburn Volume', 'rain.acc.silver-drops': 'The Dizzy Drops',
  'rain.acc.cat-eye-frames': 'The Knowing Glance',
  'kart.outfit.pit-crew': 'The Dutiful Mechanic', 'kart.outfit.ringmaster': 'The Bossy Ringmaster', 'kart.outfit.ballerina': 'The Ditzy Dancer',
  'kart.umbrella.lace': 'The Bashful Parasol', 'chess.set.royal-capes': 'The Spoiled Royals', 'chess.set.velvet-court': 'The Obedient Court',
  'chess.banner.house-pink': 'House Banner', 'chess.banner.crest': 'Crest Banner', 'chess.insignia.heart': 'Heart Insignia',
  'chess.insignia.star': 'Star Insignia', 'profile.frame.lace': 'Lace Frame', 'profile.frame.ink-stamp': 'Ink Stamp Frame',
  'profile.frame.gilt': 'Gilt Frame', 'profile.frame.polaroid': 'Polaroid Frame', 'profile.avatar.emi-pixel': 'Pixel EMI',
  'profile.avatar.rain-portrait': '{companion} Portrait', 'desk.hat.top-hat': 'The Pompous Topper', 'desk.hat.party': 'The Giddy Cone',
  'desk.toy.rubber-duck': 'The Suggestible Duck', 'desk.toy.snow-globe': 'The Foggy Forecast', 'prime.paint.host-gold': 'Host Gold Paint',
  'prime.trail.podium': 'Podium Ribbons', 'prime.frame.room-host': 'Room Host Frame', 'prime.banner.arena': 'Arena Banner',
  jackpot_remix: 'Jackpot Remix', rt_demo: 'Racing Thoughts Demo', high_roller: 'High Roller', flashes_v2: 'Flashes V2',
  bubbles_v2: 'Bubbles V2', rt_bundle_1: 'Racing Thoughts Vol. 1', rt_bundle_2: 'Racing Thoughts Vol. 2',
  rt_bundle_3: 'Racing Thoughts Vol. 3',
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
  "shop_item_rain_dress_midnight_cocktail_desc": "Blue cocktail dress for {companion}.",
  "shop_item_rain_dress_rose_flare_desc": "Pink flared dress for {companion}.",
  "shop_item_rain_dress_ivory_midi_desc": "Ivory midi dress for {companion}.",
  "shop_item_rain_dress_plum_asymmetric_desc": "Plum asymmetric dress for {companion}.",
  "shop_item_rain_dress_scarlet_bell_desc": "Scarlet bell dress for {companion}.",
  "shop_item_rain_dress_teal_tulip_desc": "Teal tulip dress for {companion}.",
  "shop_item_rain_dress_black_highlow_desc": "Black dress with a high-low hem for {companion}.",
  "shop_item_rain_suit_hazmat_yellow_desc": "Yellow hazmat suit for {companion}. The respirator is a separate item.",
  "shop_item_rain_suit_action_yellow_desc": "Yellow suit with black side stripes for {companion}. The katana is a separate item.",
  "shop_item_rain_acc_back_katana_desc": "Katana and scabbard worn on {companion}'s back. Equips separately from clothing.",
  "shop_item_rain_acc_hazmat_mask_desc": "Respirator mask for {companion}. Equips separately from the hazmat suit.",
  "shop_item_rain_acc_cat_eye_frames_desc": "Cat-eye glasses for {companion}.",
  "shop_item_rain_acc_silver_drops_desc": "Silver drop earrings for {companion}.",
  "shop_item_rain_hair_chestnut_bob_desc": "Chestnut bob for {companion}. Shown on a display head.",
  "shop_item_rain_hair_copper_lob_desc": "Copper long bob for {companion}. Shown on a display head.",
  "shop_item_rain_hair_ink_sleek_desc": "Sleek black hairstyle for {companion}. Shown on a display head.",
  "shop_item_rain_hair_rose_waves_desc": "Pink wavy hairstyle for {companion}. Shown on a display head.",
  "shop_item_rain_hair_platinum_long_desc": "Long platinum hairstyle for {companion}. Shown on a display head.",
  "shop_item_rain_hair_auburn_volume_desc": "Full auburn hairstyle for {companion}. Shown on a display head.",
  "shop_item_chess_set_royal_capes_desc": "Cobalt and gold capes for all six chess pieces. Shown on the knight.",
  "shop_item_chess_set_velvet_court_desc": "Emerald and copper clothing for all six chess pieces. Shown on the knight.",
  "shop_item_chess_banner_house_pink_desc": "Jade and copper banner with a heart crest for your chess pieces.",
  "shop_item_chess_banner_crest_desc": "Cobalt and gold banner with a star crest for your chess pieces.",
  "shop_item_chess_insignia_heart_desc": "Heart badge for your chess pieces.",
  "shop_item_chess_insignia_star_desc": "Star badge for your chess pieces.",
  "shop_item_profile_frame_lace_desc": "Lace frame for your profile picture. The portrait is an example.",
  "shop_item_profile_frame_ink_stamp_desc": "Postage stamp frame for your profile picture. The portrait is an example.",
  "shop_item_profile_frame_gilt_desc": "Gold frame for your profile picture. The portrait is an example.",
  "shop_item_profile_frame_polaroid_desc": "Instant photo frame for your profile picture. The portrait is an example.",
  "shop_item_profile_avatar_emi_pixel_desc": "Pixel-art EMI for your profile picture.",
  "shop_item_profile_avatar_rain_portrait_desc": "{companion} portrait for your profile picture.",
  "shop_item_desk_hat_top_hat_desc": "Top hat for EMI on your desktop.",
  "shop_item_desk_hat_party_desc": "Party hat for EMI on your desktop.",
  "shop_item_desk_toy_rubber_duck_desc": "Rubber duck for EMI on your desktop.",
  "shop_item_desk_toy_snow_globe_desc": "Snow globe with a tiny EMI, for EMI on your desktop.",
  "shop_item_prime_paint_host_gold_desc": "Gold paint with a diamond pattern for EMI in Racing Thoughts.",
  "shop_item_prime_trail_podium_desc": "Ribbon trail for your kart in Racing Thoughts.",
  "shop_item_prime_frame_room_host_desc": "Host profile frame with gold trim. The portrait is an example.",
  "shop_item_prime_banner_arena_desc": "Gold banner with a crown crest for your chess pieces.",
  "shop_item_jackpot_remix_desc": "Jackpot Remix kaleidoscope from the Back Room.",
  "shop_item_high_roller_desc": "High Roller badge from the Back Room.",
  "shop_item_flashes_v2_desc": "Flash projector from the Back Room.",
  "shop_item_bubbles_v2_desc": "Spiral bubble globe from the Back Room.",
  "shop_item_rt_demo_desc": "Racing Thoughts demo cartridge, level 00.",
  "shop_item_rt_bundle_1_desc": "Racing Thoughts cartridges: levels 01, 02 and 03.",
  "shop_item_rt_bundle_2_desc": "Racing Thoughts cartridges: levels 00, 04, 05 and 06.",
  "shop_item_rt_bundle_3_desc": "Racing Thoughts cartridges: levels 00, 07, 08, 09 and 10.",
};
Object.assign(LEX, ITEM_DESCS);

/** The V2 avatar's name when the host sends none (web, phone): "Rain" is only the 3D model's name. */
export const COMPANION_FALLBACK = 'Emi';

/** `init.companion` as a display name: a short plain string, else Emi. */
export function companionName(value) {
  const v = typeof value === 'string' ? value.trim() : '';
  return v && v.length <= 24 ? v : COMPANION_FALLBACK;
}

/** A translator over the host lexicon: t(key, ...args) fills {0}, {1} and {companion} (the active mod's name for the
 *  V2 avatar; the WPF host already fills it in its lexicon). Unknown keys answer the key itself. */
export function makeT(lexicon, companion) {
  const lex = lexicon && typeof lexicon === 'object' ? lexicon : {};
  const name = companionName(companion);
  return (key, ...args) => String(lex[key] ?? LEX[key] ?? key)
    .replace(/\{(\d)\}/g, (m, i) => (args[i] ?? m))
    .replace(/\{companion\}/g, name);
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
