/* sheet/explain.js - what each thing on the counter does, in plain words (owner 2026-11-17: "someone hovers over bubbles
 * v2 and doesnt know what it does"). Page-side and keyed by item id, with a family fallback, so it works with the real
 * server, which sends only ids and names. Three layers:
 *   tip    one short line: the hover tip on a card (sheet/tips.js), and the line under the name on a phone
 *   does   the fitting room's "What it does" line; an item with no `does` of its own keeps its old desc key
 *   at/use where it shows, and how to turn it on or wear it after buying (only where the app really has that path)
 * Every value is a `shop_sheet_*` loc key; the English is EXPLAIN_LEX below (merged into sheet/strings.js SHEET_LEX), the
 * ten language files carry the same keys. Facts checked in the code, 2026-11-17 (the lane hand-back lists the sources). */

import { itemDesc } from '../state/strings.js';

/** Families by id prefix (a state row) or effect slot (an effect row). The first match wins. */
const PREFIX = [
  ['kart.body.', 'kart_body'], ['kart.outfit.', 'kart_outfit'], ['kart.paint.', 'kart_paint'], ['prime.paint.', 'kart_paint'],
  ['kart.umbrella.', 'kart_umbrella'], ['prime.trail.', 'kart_trail'],
  ['chess.set.', 'chess_set'], ['chess.finish.', 'chess_finish'], ['chess.banner.', 'chess_banner'], ['prime.banner.', 'chess_banner'], ['chess.insignia.', 'chess_insignia'],
  ['chess.board.', 'chess_board'], ['chess.companion.', 'chess_companion'],
  ['profile.frame.', 'profile_frame'], ['prime.frame.', 'profile_frame'], ['profile.avatar.', 'profile_avatar'],
  ['desk.hat.', 'desk_hat'], ['desk.toy.', 'desk_toy'],
  ['rain.dress.', 'rain_clothes'], ['rain.suit.', 'rain_clothes'], ['rain.hair.', 'rain_hair'], ['rain.acc.', 'rain_acc'],
];

/** Family lines: tip, does (null = the item's own desc key), at (where), use (how to turn it on or wear it). */
export const FAMILY = {
  fx_bubble: { tip: 'shop_sheet_tip_bubble', does: 'shop_sheet_does_bubble', at: 'shop_sheet_at_bubble', use: 'shop_sheet_use_bubble' },
  fx_card: { tip: 'shop_sheet_tip_card', does: 'shop_sheet_does_card', at: 'shop_sheet_at_deck', use: 'shop_sheet_use_deck' },
  fx_behind: { tip: 'shop_sheet_tip_behind', does: 'shop_sheet_does_behind', at: 'shop_sheet_at_deck', use: 'shop_sheet_use_deck' },
  fx_fog: { tip: 'shop_sheet_tip_fog', does: 'shop_sheet_twotone_where', at: 'shop_sheet_at_fog', use: 'shop_sheet_use_fog' },
  fx_word: { tip: 'shop_sheet_tip_word', does: 'shop_sheet_does_word', at: 'shop_sheet_at_word', use: 'shop_sheet_use_word' },
  kart_body: { tip: 'shop_sheet_tip_kart_body', does: null, at: 'shop_sheet_at_kart', use: 'shop_sheet_use_kart' },
  kart_outfit: { tip: 'shop_sheet_tip_kart_outfit', does: null, at: 'shop_sheet_at_kart', use: 'shop_sheet_use_kart' },
  kart_paint: { tip: 'shop_sheet_tip_kart_paint', does: 'shop_sheet_does_kart_look', at: 'shop_sheet_at_kart', use: 'shop_sheet_use_kart' },
  kart_umbrella: { tip: 'shop_sheet_tip_kart_umbrella', does: null, at: 'shop_sheet_at_kart', use: 'shop_sheet_use_kart' },
  kart_trail: { tip: 'shop_sheet_tip_kart_trail', does: null, at: 'shop_sheet_at_kart', use: 'shop_sheet_use_kart' },
  chess_set: { tip: 'shop_sheet_tip_chess_set', does: null, at: 'shop_sheet_shows_chess', use: 'shop_sheet_use_chess' },
  chess_finish: { tip: 'shop_sheet_tip_chess_finish', does: null, at: 'shop_sheet_shows_chess', use: 'shop_sheet_use_chess' },
  chess_board: { tip: 'shop_sheet_tip_chess_board', does: null, at: 'shop_sheet_at_chess_desktop', use: 'shop_sheet_use_chess_board' },
  chess_companion: { tip: 'shop_sheet_tip_chess_companion', does: null, at: 'shop_sheet_at_chess_desktop', use: 'shop_sheet_use_chess_companion' },
  chess_banner: { tip: 'shop_sheet_tip_chess_banner', does: null, at: 'shop_sheet_shows_chess', use: 'shop_sheet_use_chess' },
  chess_insignia: { tip: 'shop_sheet_tip_chess_insignia', does: null, at: 'shop_sheet_shows_chess', use: 'shop_sheet_use_chess' },
  profile_frame: { tip: 'shop_sheet_tip_profile_frame', does: null, at: 'shop_sheet_at_profile', use: 'shop_sheet_use_profile' },
  profile_avatar: { tip: 'shop_sheet_tip_profile_avatar', does: null, at: 'shop_sheet_at_profile', use: 'shop_sheet_use_profile' },
  desk_hat: { tip: 'shop_sheet_tip_desk_hat', does: null, at: 'shop_sheet_at_desk', use: 'shop_sheet_use_desk' },
  desk_toy: { tip: 'shop_sheet_tip_desk_toy', does: null, at: 'shop_sheet_at_desk', use: 'shop_sheet_use_desk' },
  rain_clothes: { tip: 'shop_sheet_tip_rain_clothes', does: null, at: 'shop_sheet_at_rain', use: 'shop_sheet_use_rain' },
  rain_hair: { tip: 'shop_sheet_tip_rain_hair', does: null, at: 'shop_sheet_at_rain', use: 'shop_sheet_use_rain' },
  rain_acc: { tip: 'shop_sheet_tip_rain_acc', does: null, at: 'shop_sheet_at_rain', use: 'shop_sheet_use_rain' },
  // a Back Room prize this list does not know yet: where it was won, nothing more (never a guess)
  backroom: { tip: 'shop_sheet_tip_backroom', does: null, at: null, use: null },
};

/** Item lines over the family's. A Back Room prize is its own family of one. */
export const ITEM = {
  'fx.bubble.soap': { tip: 'shop_sheet_tip_fx_bubble_soap' },
  'fx.bubble.marble': { tip: 'shop_sheet_tip_fx_bubble_marble' },
  'fx.bubble.heart': { tip: 'shop_sheet_tip_fx_bubble_heart' },
  'fx.bubble.champagne': { tip: 'shop_sheet_tip_fx_bubble_champagne' },
  'fx.bubble.spiral': { tip: 'shop_sheet_tip_fx_bubble_spiral' },
  'fx.bubble.disco': { tip: 'shop_sheet_tip_fx_bubble_disco' },
  'fx.bubble.frost': { tip: 'shop_sheet_tip_fx_bubble_frost' },
  'fx.card.plain': { tip: 'shop_sheet_tip_fx_card_plain', does: 'shop_sheet_does_card_plain' },
  'fx.card.ink': { tip: 'shop_sheet_tip_fx_card_ink' },
  'fx.card.check': { tip: 'shop_sheet_tip_fx_card_check' },
  'fx.card.lace': { tip: 'shop_sheet_tip_fx_card_lace' },
  'fx.behind.none': { tip: 'shop_sheet_tip_fx_behind_none', does: 'shop_sheet_does_behind_none' },
  'fx.word.classic': { tip: 'shop_sheet_tip_fx_word_classic' },
  'fx.word.type': { tip: 'shop_sheet_tip_fx_word_type' },
  'fx.word.chalk': { tip: 'shop_sheet_tip_fx_word_chalk' },
  'fx.word.drift': { tip: 'shop_sheet_tip_fx_word_drift' },
  'fx.word.echo': { tip: 'shop_sheet_tip_fx_word_echo' },
  'fx.word.marquee': { tip: 'shop_sheet_tip_fx_word_marquee' },
  'chess.finish.polka': { tip: 'shop_sheet_tip_chess_finish_polka' },
  'chess.finish.marble': { tip: 'shop_sheet_tip_chess_finish_marble' },
  'chess.finish.contour': { tip: 'shop_sheet_tip_chess_finish_contour' },
  'chess.finish.opal': { tip: 'shop_sheet_tip_chess_finish_opal' },
  'chess.finish.kintsugi': { tip: 'shop_sheet_tip_chess_finish_kintsugi' },
  'chess.board.observatory': { tip: 'shop_sheet_tip_chess_board_observatory' },
  'chess.board.conservatory': { tip: 'shop_sheet_tip_chess_board_conservatory' },
  'chess.board.theatre': { tip: 'shop_sheet_tip_chess_board_theatre' },
  'chess.board.arcade': { tip: 'shop_sheet_tip_chess_board_arcade' },
  jackpot_remix: { tip: 'shop_sheet_tip_jackpot_remix', does: 'shop_sheet_does_jackpot_remix', at: 'shop_sheet_at_flashes', use: 'shop_sheet_use_jackpot_remix' },
  flashes_v2: { tip: 'shop_sheet_tip_flashes_v2', does: 'shop_sheet_does_flashes_v2', at: 'shop_sheet_at_flashes', use: 'shop_sheet_use_flashes_v2' },
  bubbles_v2: { tip: 'shop_sheet_tip_bubbles_v2', does: 'shop_sheet_does_bubbles_v2', at: 'shop_sheet_at_bubble_pop', use: 'shop_sheet_use_bubbles_v2' },
  high_roller: { tip: 'shop_sheet_tip_high_roller', does: 'shop_sheet_does_high_roller', at: 'shop_sheet_at_discord', use: 'shop_sheet_use_high_roller' },
  rt_demo: { tip: 'shop_sheet_tip_rt_demo', at: 'shop_sheet_at_race_classic' },
  rt_bundle_1: { tip: 'shop_sheet_tip_rt_bundle_1', at: 'shop_sheet_at_race_classic' },
  rt_bundle_2: { tip: 'shop_sheet_tip_rt_bundle_2', at: 'shop_sheet_at_race_classic' },
  rt_bundle_3: { tip: 'shop_sheet_tip_rt_bundle_3', at: 'shop_sheet_at_race_classic' },
};

/** The top tabs (sheet/model.js sheetTabs ids). */
export const TAB_TIPS = {
  featured: 'shop_sheet_tip_tab_featured', effects: 'shop_sheet_tip_tab_effects', games: 'shop_sheet_tip_tab_games', you: 'shop_sheet_tip_tab_you',
  prime: 'shop_sheet_tip_tab_prime', backroom: 'shop_sheet_tip_tab_backroom', owned: 'shop_sheet_tip_tab_owned',
  'rain-clothes': 'shop_sheet_tip_rain_clothes', 'rain-hair': 'shop_sheet_tip_rain_hair',
};
/** The shelf signs (a section key: an effect slot, a state tab), and the front page's own signs. */
export const SIGN_TIPS = {
  bubble: 'shop_sheet_sign_bubble', card: 'shop_sheet_sign_card', behind: 'shop_sheet_sign_behind', fog: 'shop_sheet_sign_fog', word: 'shop_sheet_sign_word',
  kart: 'shop_sheet_sign_kart', chess: 'shop_sheet_sign_chess', profile: 'shop_sheet_sign_profile', desk: 'shop_sheet_sign_desk', prime: 'shop_sheet_sign_prime',
  backroom: 'shop_sheet_sign_backroom', 'rain-clothes': 'shop_sheet_tip_rain_clothes', 'rain-hair': 'shop_sheet_tip_rain_hair',
  gift: 'shop_sheet_help_gift', save: 'shop_sheet_help_saving', rcpt: 'shop_sheet_sign_receipt', can: 'shop_sheet_help_can_buy',
};
/** The shop's own mechanics, one line each, behind a small "?" (or a hover on the thing itself). */
export const HELP = {
  wallet: 'shop_sheet_help_wallet', gift: 'shop_sheet_help_gift', stamps: 'shop_sheet_help_stamps', saving: 'shop_sheet_help_saving', can: 'shop_sheet_help_can_buy',
};
/** A card's tip adds why it cannot be bought yet. */
const STATUS_TIPS = { prime: 'shop_sheet_tip_status_prime', todo: 'shop_sheet_tip_status_todo' };

const BACKROOM = new Set(['jackpot_remix', 'flashes_v2', 'bubbles_v2', 'high_roller', 'rt_demo', 'rt_bundle_1', 'rt_bundle_2', 'rt_bundle_3']);

/** An entry's family (an entry from sheet/model.js rowsOf: { id, fx, tab }). */
export function familyOf(e) {
  if (!e) return null;
  if (e.fx) return 'fx_' + e.fx;
  const id = String(e.id || '');
  for (const [p, f] of PREFIX) if (id.startsWith(p)) return f;
  if (e.tab === 'backroom' || BACKROOM.has(id)) return 'backroom';
  return null;
}

/** The keys for one entry: { tip, does, at, use } (each a key or null). */
export function explainKeys(e) {
  const f = FAMILY[familyOf(e)] || {}, i = (e && ITEM[e.id]) || {};
  return { tip: i.tip || f.tip || null, does: i.does || f.does || null, at: i.at || f.at || null, use: i.use || f.use || null };
}

/** The hover tip's words for a card: its line, plus why it cannot be bought (Prime only, not out yet). null = none. */
export function tipText(e, t, status = null) {
  const k = explainKeys(e).tip;
  if (!k) return null;
  const s = status ? STATUS_TIPS[status.kind] : null;
  return t(k) + (s ? ' ' + t(s) : '');
}

/** The fitting room's "What it does": { does, at, use } in words (each may be null). `does` falls back to the item's own
 * desc key (state/strings.js itemDesc), then the family's line. */
export function roomExplain(e, t) {
  const k = explainKeys(e), i = (e && ITEM[e.id]) || {};
  const own = !i.does && !e.fx ? itemDesc(t, e.id) : null;
  return { does: own || (k.does ? t(k.does) : null), at: k.at ? t(k.at) : null, use: k.use ? t(k.use) : null };
}

/** The place mark's tip: where it shows, and that it can be bought from any of them. */
export const placesTip = (places, t) => t('shop_sheet_tip_places', places.map((p) => t('shop_sheet_where_' + p)).join(t('shop_sheet_and')));

/** English for every key above (sheet/strings.js merges it into SHEET_LEX). */
export const EXPLAIN_LEX = {
  shop_sheet_what_it_does: 'What it does',
  shop_sheet_lead_where: 'Where',
  shop_sheet_lead_how: 'How',
  shop_sheet_help_mark: 'What is this?',
  // effect families
  shop_sheet_tip_bubble: 'A new look for every bubble on your screen',
  shop_sheet_does_bubble: 'Bubble Pop\'s bubbles wear this look, with their own pop and sound.',
  shop_sheet_at_bubble: 'Bubble Pop, on your desktop',
  shop_sheet_use_bubble: 'Wear it here. New bubbles take the look.',
  shop_sheet_tip_card: 'A border round every Deck card',
  shop_sheet_does_card: 'Puts this border round every card Deck flips.',
  shop_sheet_tip_behind: 'A spiral on the back of each Deck card',
  shop_sheet_does_behind: 'Every other flip turns the card to a spiral back, and one of your own subliminal phrases comes at you over it.',
  shop_sheet_at_deck: 'Deck, the flashes that flip like cards',
  shop_sheet_use_deck: 'Wear it here. It shows while Deck is on.',
  shop_sheet_tip_fog: 'Your filter colour with a second one swirling through',
  shop_sheet_at_fog: 'The pink filter and Steam',
  shop_sheet_use_fog: 'Switch it on here, then pick the second colour.',
  shop_sheet_tip_word: 'A new look for your subliminal words',
  shop_sheet_does_word: 'Your subliminals change how they look and move. They stay on screen just as long.',
  shop_sheet_at_word: 'Subliminals',
  shop_sheet_use_word: 'Wear it here. The next subliminal shows it.',
  // effect rows
  shop_sheet_tip_fx_bubble_soap: 'The plain soap bubble everyone starts with',
  shop_sheet_tip_fx_bubble_marble: 'Every bubble turns to glass with colours twisting inside',
  shop_sheet_tip_fx_bubble_heart: 'Every bubble becomes a beating heart',
  shop_sheet_tip_fx_bubble_champagne: 'Every bubble fizzes with gold beads rising inside',
  shop_sheet_tip_fx_bubble_spiral: 'Every bubble carries its own turning spiral',
  shop_sheet_tip_fx_bubble_disco: 'Every bubble on your screen becomes a mirror ball',
  shop_sheet_tip_fx_bubble_frost: 'Every bubble slowly freezes round a snowflake',
  shop_sheet_tip_fx_card_plain: 'No frame: Deck cards as they are',
  shop_sheet_does_card_plain: 'Deck cards with no frame, as they come.',
  shop_sheet_tip_fx_card_ink: 'An ink hatch border round every Deck card',
  shop_sheet_tip_fx_card_check: 'A pink check border round every Deck card',
  shop_sheet_tip_fx_card_lace: 'A lace border round every Deck card',
  shop_sheet_tip_fx_behind_none: 'Nothing behind the card, as now',
  shop_sheet_does_behind_none: 'Deck cards flip with nothing behind them.',
  shop_sheet_tip_fx_word_classic: 'Subliminals fade in and out, as now',
  shop_sheet_tip_fx_word_type: 'Subliminals type themselves out',
  shop_sheet_tip_fx_word_chalk: 'Subliminals are written on in chalk',
  shop_sheet_tip_fx_word_drift: 'Each subliminal\'s letters rise into place',
  shop_sheet_tip_fx_word_echo: 'Each subliminal leaves fading ghost copies',
  shop_sheet_tip_fx_word_marquee: 'Subliminals light up like a marquee sign',
  // game, profile and desk families
  shop_sheet_tip_kart_body: 'A kart for EMI. Each one drives differently',
  shop_sheet_tip_kart_outfit: 'An outfit EMI wears in Racing Thoughts',
  shop_sheet_tip_kart_paint: 'A paint job for your kart in Racing Thoughts',
  shop_sheet_does_kart_look: 'A look for your kart. Only kart bodies change how it drives.',
  shop_sheet_tip_kart_umbrella: 'A parasol for your kart in Racing Thoughts',
  shop_sheet_tip_kart_trail: 'A ribbon trail behind your kart in Racing Thoughts',
  shop_sheet_at_kart: 'Racing Thoughts',
  shop_sheet_use_kart: 'Wear it in the garage in Racing Thoughts.',
  shop_sheet_tip_chess_set: 'Clothes for all six of your chess pieces',
  shop_sheet_tip_chess_finish: 'A finish for all six of your chess pieces',
  shop_sheet_tip_chess_board: 'A themed chessboard with its own moving details',
  shop_sheet_tip_chess_companion: 'A seated Emi who cheers your chess moves',
  shop_sheet_tip_chess_board_observatory: 'Lapis, brass and moving gold seams',
  shop_sheet_tip_chess_board_conservatory: 'Green glaze, copper and drifting pollen',
  shop_sheet_tip_chess_board_theatre: 'Velvet, lacquer and warm theatre lights',
  shop_sheet_tip_chess_board_arcade: 'Graphite tiles with moving neon traces',
  shop_sheet_at_chess_desktop: 'Piece by Piece in the desktop app',
  shop_sheet_use_chess_board: 'Wear it here or in the Wardrobe. Your piece finish stays separate.',
  shop_sheet_use_chess_companion: 'Wear her here or in the Wardrobe. Both players can see her.',
  shop_sheet_tip_chess_finish_polka: 'Dots and rings. Raspberry on white, cobalt on black',
  shop_sheet_tip_chess_finish_marble: 'Folded stone. Jade on white, amethyst on black',
  shop_sheet_tip_chess_finish_contour: 'Bright ribbons. Cyan on white, ember on black',
  shop_sheet_tip_chess_finish_opal: 'Oil slick swirls. Violet on white, copper on black',
  shop_sheet_tip_chess_finish_kintsugi: 'Gold cracks. Obsidian on white, turquoise on black',
  shop_sheet_tip_chess_banner: 'A banner for your chess pieces',
  shop_sheet_tip_chess_insignia: 'A badge for your chess pieces',
  shop_sheet_use_chess: 'Wear it here, or in the Wardrobe in Piece by Piece.',
  shop_sheet_tip_profile_frame: 'A frame round your profile picture',
  shop_sheet_tip_profile_avatar: 'A new profile picture',
  shop_sheet_at_profile: 'Your profile card',
  shop_sheet_use_profile: 'Wear it here. It shows on your profile card.',
  shop_sheet_tip_desk_hat: 'A hat for EMI on your desktop',
  shop_sheet_tip_desk_toy: 'A toy for EMI\'s desk on your desktop',
  shop_sheet_at_desk: 'EMI\'s desk, in the desktop app',
  shop_sheet_use_desk: 'Wear it here. One hat and one toy at a time.',
  shop_sheet_tip_rain_clothes: 'Clothes for {companion}',
  shop_sheet_tip_rain_hair: 'Hairstyles and accessories for {companion}',
  shop_sheet_tip_rain_acc: 'An accessory for {companion}',
  shop_sheet_at_rain: '{companion}\'s wardrobe',
  shop_sheet_use_rain: 'Wear opens {companion}\'s dressing room.',
  // Back Room prizes
  shop_sheet_tip_backroom: 'A prize from the Back Room counter',
  shop_sheet_tip_jackpot_remix: 'One flash in 100 becomes a remix of your own GIFs',
  shop_sheet_does_jackpot_remix: 'One scheduled flash in a hundred becomes a mashup of GIFs from your own flash collection.',
  shop_sheet_at_flashes: 'Flashes',
  shop_sheet_use_jackpot_remix: 'Switch on Jackpot Remix in Flashes. It stays off until you do.',
  shop_sheet_tip_flashes_v2: 'Flashes that drift, swing, get flung and shatter',
  shop_sheet_does_flashes_v2: 'Drift & Bounce and Pendulum motion, GIFs you can drag and fling, rounded corners and Shatter.',
  shop_sheet_use_flashes_v2: 'Pick the new options in Flashes. Nothing turns on by itself.',
  shop_sheet_tip_bubbles_v2: 'Bubbles that rain down or spiral in, plus two new kinds',
  shop_sheet_does_bubbles_v2: 'Rain and Spiral In motion, plus Brain Drain and Magnet bubbles.',
  shop_sheet_at_bubble_pop: 'Bubble Pop',
  shop_sheet_use_bubbles_v2: 'Pick them in Bubble Pop. Nothing turns on by itself.',
  shop_sheet_tip_high_roller: 'A Discord role that says you bought here',
  shop_sheet_does_high_roller: 'A Discord role that says you bought at the counter. Yours for good.',
  shop_sheet_at_discord: 'Discord',
  shop_sheet_use_high_roller: 'Needs Discord linked to your account.',
  shop_sheet_tip_rt_demo: 'Track 00, the demo level of Racing Thoughts Classic',
  shop_sheet_tip_rt_bundle_1: 'Tracks 01 to 03 of Racing Thoughts Classic',
  shop_sheet_tip_rt_bundle_2: 'Tracks 04 to 06 of Racing Thoughts Classic, plus the demo',
  shop_sheet_tip_rt_bundle_3: 'Tracks 07 to 10 of Racing Thoughts Classic, plus the demo',
  shop_sheet_at_race_classic: 'Racing Thoughts Classic',
  // why a card cannot be bought
  shop_sheet_tip_status_prime: 'Prime members can buy it.',
  shop_sheet_tip_status_todo: 'Not on sale yet.',
  shop_sheet_tip_places: 'Shows {0}. You can buy it from any of them.',
  // the tabs
  shop_sheet_tip_tab_featured: 'New looks, your gift, stamps and what you can afford',
  shop_sheet_tip_tab_effects: 'Looks for the effects on your desktop screen',
  shop_sheet_tip_tab_games: 'Karts for Racing Thoughts, looks for chess',
  shop_sheet_tip_tab_you: 'Profile frames, and hats and toys for EMI\'s desk',
  shop_sheet_tip_tab_prime: 'Looks only Prime members can buy',
  shop_sheet_tip_tab_backroom: 'New effect options, race tracks and a Discord role',
  shop_sheet_tip_tab_owned: 'Everything you own, to wear again',
  // the shelf signs
  shop_sheet_sign_bubble: 'One skin at a time, on every Bubble Pop bubble',
  shop_sheet_sign_card: 'One frame at a time, while Deck is on',
  shop_sheet_sign_behind: 'Needs Deck on. Uses your own subliminal words',
  shop_sheet_sign_fog: 'Needs the pink filter or Steam on',
  shop_sheet_sign_word: 'One style at a time, on every subliminal',
  shop_sheet_sign_kart: 'Worn in the Racing Thoughts garage',
  shop_sheet_sign_chess: 'Looks for your pieces, themed boards and Emi',
  shop_sheet_sign_profile: 'Frames and pictures for your profile card',
  shop_sheet_sign_desk: 'Hats and toys for EMI\'s desk, desktop app only',
  shop_sheet_sign_prime: 'Prime members can buy these. Anyone can look',
  shop_sheet_sign_backroom: 'Each prize unlocks for good. Nothing turns on by itself',
  shop_sheet_sign_receipt: 'Every Sparkle in and out, newest first',
  shop_sheet_shows_backroom: 'Extras for Flashes, Bubble Pop and the race',
  shop_sheet_shows_rain: '{companion}\'s wardrobe',
  // the shop's own mechanics
  shop_sheet_help_wallet: 'Sparkles: earned playing, levelling up and popping bubbles',
  shop_sheet_help_gift: 'Free Sparkles once a week. Open it here',
  shop_sheet_help_stamps: 'One stamp a day. Enough stamps earn a free small item',
  shop_sheet_help_saving: 'Pick something to save for and watch the bar fill',
  shop_sheet_help_can_buy: 'Things your wallet covers right now',
};
