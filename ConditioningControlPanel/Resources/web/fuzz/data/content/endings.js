/* ============================================================================
 * data/content/endings.js - the card text for all thirteen endings. Pure data.
 *
 * ENDINGS[id]: { id, title, result, credits: [3 to 6 lines], closing, link? }
 * link is { label, url } and only ever points at cclabs.app. Living Ad also has
 * `price`, with a {score} token the ending shell fills with the run's total.
 * DROP_WORDS: one word at a time in the Drop ending; clip is a slug from
 * data/words.js CIRCE, or null, which stays silent.
 *
 * Tokens: {game} and {cur}, filled from data/brand.js.
 * ==========================================================================*/

const CCP_URL = 'https://cclabs.app';
const link = (label) => ({ label, url: CCP_URL });

const e = (id, title, result, credits, closing, more) => ({ id, title, result, credits, closing, ...(more || {}) });

export const ENDINGS = Object.freeze({
  drop: e('drop', 'Drop', 'Everything went quiet and you let it.', [
    '{game}',
    'Made by CC Labs',
    'Button: a button',
    'Spiral: the same spiral, turning',
    'Your thoughts: returned to sender',
    'Thanks for playing',
  ], 'Cannot be undone. You can start again, which is different.', { link: link('The rest of it is called CCP') }),

  o_out: e('o_out', 'Living Ad', 'You stopped watching the ads and became one.', [
    'This ending was sponsored by you',
    'Socks: The Socks Group',
    'Soap: lonely, local',
    'Goose: on loan, not returned',
    'Final creative: your face, looking at this',
  ], 'Everyone has a price. It turns out yours was on the counter the whole time.', {
    price: '{score} {cur}',
    priceLabel: 'your price',
    link: link('See what was being advertised'),
  }),

  p_life: e('p_life', 'Lifetime Plan', 'You are thanked for ever. For ever has been shortened for legal reasons.', [
    'Thank you',
    'Thank you for staying',
    'Thank you for not finding the cancel link',
    'Gold leaf: applied to the font',
    'Support: EMI, on line one, unhelpfully',
  ], 'Your lifetime starts now. It is shorter than you think.', { link: link('The real membership. It has Basic and Prime.') }),

  tr_end: e('tr_end', 'Count Up', 'Someone is counting you back up. Take your time. They are.', [
    'Count by: a very patient voice',
    'One: complete',
    'Two: complete',
    'Two and a half: in progress',
    'Two and three quarters: scheduled',
    'Nearly',
  ], 'You will wake on three. Nobody has said when three is.', {
    count: ['One', 'Two', 'Two and a half', 'Two and three quarters', 'Nearly', 'Very nearly', 'Two and seven eighths'],
    link: link('More of this, without the counting, in CCP'),
  }),

  bi_end: e('bi_end', 'Empty Smile', 'Pink screen. Big smile. Nobody is keeping score, least of all you.', [
    'Starring: um',
    'Written by: someone with lots of words',
    'Thinking: cut for time',
    'Gloss: all of it',
    'Number: a lot',
  ], 'Who is keeping score. Not you. So pretty, though.', { link: link('The Bambi mod, in CCP. Press the pink one.') }),

  dr_end: e('dr_end', 'Unit Complete', 'Shop: off. Counter: off. Name: off. Input: off. Unit: complete.', [
    'Unit performed adequately',
    'Hive: notified',
    'Unit 7: still ahead',
    'Name: archived, not required',
    'Report closed',
  ], 'No further input required. This message will not repeat. This message will repeat.', {
    lines: ['shop: off', 'counter: off', 'name: off', 'input: off', 'unit complete'],
    link: link('Drone mode, in CCP'),
  }),

  si_end: e('si_end', 'The Debut', 'Curtain up, one spotlight, one slow bow. Everyone is looking. Good.', [
    'Wardrobe: the button',
    'Posture: under protest',
    'Napkin: folded',
    'Pinky: out',
    'The audience: seated, very seated',
  ], 'Lovely. Now do it again, and this time as if you meant the bow.', { link: link('The Sissy mod, in CCP') }),

  go_end: e('go_end', 'Sunrise', 'Four hours later, the curtain has a thin orange line in it. It was dinner time a moment ago.', [
    'Tabs: all of them, still open',
    'Clock: unreliable witness',
    'Others online: nodding, all night',
    'Dinner: a rumour',
    'Sunlight: uninvited',
  ], 'What time is it. Do not check. You did not close those tabs.', { link: link('Goon, the game, in CCP') }),

  lo_end: e('lo_end', 'Lost Key', 'The key goes up, comes down, and makes a very small splash a long way out.', [
    'Throw: nice',
    'Key: the sea',
    'Timer: still counting',
    'Keyholder: smiling, offshore',
    'The fish: unlocked nothing',
  ], 'Nice throw. It was a pretend key. The feeling you just had was not.', { link: link('Real timers, real keys: Chaster and the Locked mod, in CCP') }),

  ce_end: e('ce_end', 'Redacted', 'Your result is ████████. Congratulations on your ██████.', [
    '█████████ by ███ ███',
    'Pictures: ███████',
    'Mascot: ██ █████',
    'Director: ███████',
    'Thanks: ██████',
  ], '██████. You almost saw it. That was the good part.', { link: link('█████ in ███. That is CCP.') }),

  in_end: e('in_end', 'Flatline', 'All pink. The line on the monitor stopped being a line and started turning.', [
    'Patient zero: you',
    'Ward: all of it',
    'Antivirus: resting, did its best',
    'Symptoms: all of them',
    'Contagious: you have the link',
  ], 'Contained. Sort of. Please wash your hands of this.', { link: link('Infection Control, a mod in CCP') }),

  outside: e('outside', 'Go Outside', 'You left. No reward. The grass has not been told about the {cur}.', [
    'Sun: unpaid',
    'Tree: as itself',
    'Weather: no upgrades available',
    'Everything you bought: still in there, without you',
  ], 'It is nice out. You would know. Close this whenever you are ready.', { closesTab: true }),

  onemore: e('onemore', 'One More', 'You could have stopped. There were several good places.', [
    'The credits were going to go here',
    'They have gone home',
    'Please turn the lights off when you stop',
  ], 'No ending, then. Everything keeps going. There is still a Wake Up button, up there, being polite about it.', { endless: true }),
});

/** One word at a time, fading through the pink-white, then the credits. */
export const DROP_WORDS = Object.freeze([
  Object.freeze({ word: 'quiet', clip: null }),
  Object.freeze({ word: 'now', clip: null }),
  Object.freeze({ word: 'relax', clip: 'relax' }),
  Object.freeze({ word: 'sink', clip: 'sink' }),
  Object.freeze({ word: 'softer', clip: 'softer' }),
  Object.freeze({ word: 'deeper', clip: 'deeper' }),
  Object.freeze({ word: 'let go', clip: 'let-go' }),
  Object.freeze({ word: 'blank', clip: 'blank' }),
  Object.freeze({ word: 'empty', clip: 'empty' }),
  Object.freeze({ word: 'so calm', clip: 'so-calm' }),
  Object.freeze({ word: 'gone', clip: null }),
]);
