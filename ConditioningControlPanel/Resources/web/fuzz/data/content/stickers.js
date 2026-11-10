/* ============================================================================
 * data/content/stickers.js - the Sticker Book: every sticker, its page and its art. Pure data.
 *
 * Owner calls 2026-11-10 (mockup artifact WL9qAeCn8oLHSPVDcKE1Sd): the book keeps every sticker across runs,
 * the ten-cell meter still fills per run, the on-screen name is "Sticker Book", all 44 new stickers go in,
 * the Living Ad sticker is the player's own printed card, a full page earns a foil tab edge (looks only).
 *
 * STICKERS[i]: { id, page, name, line, how, when, scope, hidden, art }
 *   when   "key:n" read by sim/stickers.js. Run keys: taps buys total minutes forks theme owns, or a run tally
 *          (corner golden resume ...). Life keys: life.<tally> ending endings themes depth core llamas.
 *   scope  'run' fills this run's meter and can be earned again next run; 'life' only goes in the book.
 *   art    { k: 'img', src } | { k: 'gold', v } (a Golden Button crayon star) | { k: 'portrait', theme }
 *          | { k: 'card' } (the player's own Living Ad card, else that theme's portrait).
 *   legacy true: a retired ending (Drop, One More, and the endings without a fate set: off the shelf since 2026-11-10,
 *   sim/economy.js OFF_SHELF). Shown and counted only once earned.
 * Tokens: {cur} and {game}, filled by data/brand.js.
 * ==========================================================================*/

import { ACHIEVEMENTS } from './side.js';

export const STICKER_PAGES = Object.freeze([
  { id: 'press', name: 'Pressing', tab: '#ffd3e6', note: 'Taps on the button. The fast ones stay sealed until found.' },
  { id: 'shop', name: 'Shopping', tab: '#fff1b8', note: 'Buying things, and the money you had to buy them with.' },
  { id: 'watch', name: 'Watching', tab: '#d4ecff', note: 'Things that happen at the edges of the page, if you are looking.' },
  { id: 'choose', name: 'Choosing', tab: '#e2f5d8', note: 'Forks, questions and the occasional no.' },
  { id: 'time', name: 'Time', tab: '#efe2ff', note: 'Staying, stopping and coming back.' },
  { id: 'play', name: 'Games', tab: '#ffe0cc', note: 'One for each game inside the game.' },
  { id: 'themes', name: 'Themes', tab: '#ffd9ef', note: 'One for each theme you have ever picked.', big: true },
  { id: 'endings', name: 'Endings', tab: '#fff6d6', note: 'One run, one ending. They stay here for good.', big: true },
  { id: 'sky', name: 'Renewal', tab: '#dfe6ff', note: 'The Renewal Sky. These stay too.' },
]);

const clip = (shape) => ({ k: 'img', src: `assets/clipart/${shape}.webp` });
const drawn = (id) => ({ k: 'img', src: `assets/stickers/${id}.webp` });
const img = (src) => ({ k: 'img', src });
const LIFE_KEYS = ['life.', 'ending', 'endings', 'shelfEndings', 'themes', 'depth', 'core', 'llamas'];
const scopeOf = (when) => (LIFE_KEYS.some((k) => when.startsWith(k)) ? 'life' : 'run');

/* The 33 achievements the game already had keep their ids, names, lines and conditions (data/content/side.js). */
const OLD = Object.fromEntries(ACHIEVEMENTS.map((a) => [a.id, a]));
const old = (id, page, how, art) => {
  const a = OLD[id];
  return { id, page, name: a.name, line: a.line, how, when: a.when, hidden: a.hidden, art };
};
const add = (id, page, name, line, how, when, art, hidden = false) => ({ id, page, name, line, how, when, hidden, art });
const RETIRED = new Set(['e_drop', 'e_onemore', 'e_tr_end', 'e_si_end', 'e_lo_end', 'e_ce_end', 'e_in_end', 'e_p_life']);

const RAW = [
  old('ac_first', 'press', 'Press the button once.', clip('button')),
  old('ac_100', 'press', 'Press 100 times in a run.', clip('numbers')),
  old('ac_1k', 'press', 'Press 1,000 times in a run.', clip('hand')),
  old('ac_5k', 'press', 'Press 5,000 times in a run.', drawn('ac_5k')),
  add('n_sweet', 'press', 'Sweet Spot', 'Steady. She noticed.', 'Keep the EMI button in its sweet spot for 10 seconds.', 'sweet10:1', img('assets/img/emi/body-idle.png')),
  old('ac_tap8', 'press', 'Eight presses a second, held for 3 seconds.', clip('cursor')),
  old('ac_tap12', 'press', 'Twelve presses a second, held for 3 seconds.', clip('cursors')),
  old('ac_tap15', 'press', 'Fifteen presses a second, held for 3 seconds.', clip('burst')),

  old('ac_buy1', 'shop', 'Buy one thing.', clip('tag')),
  old('ac_buy10', 'shop', 'Buy 10 things in a run.', clip('box')),
  old('ac_buy25', 'shop', 'Buy 25 things in a run.', clip('wallet')),
  old('ac_buy50', 'shop', 'Buy 50 things in a run.', clip('papers')),
  old('ac_buy100', 'shop', 'Buy 100 things in a run.', clip('confetti')),
  old('ac_cur1k', 'shop', 'Earn 1,000 {cur} in a run.', clip('ring')),
  old('ac_cur100k', 'shop', 'Earn 100,000 {cur} in a run.', clip('chart')),
  old('ac_cur1m', 'shop', 'Earn 1,000,000 {cur} in a run.', clip('crown')),
  add('n_clause3', 'shop', 'Read the Whole Thing', 'All three. Very thorough. Too late, though.', 'Accept a terms card with three clauses.', 'clause3:1', clip('scroll')),
  add('n_terms50', 'shop', 'Signed, Sealed', 'Nobody reads them. You signed them anyway.', 'Accept 50 terms cards, over every run.', 'life.terms:50', drawn('n_terms50')),

  old('ac_corner', 'watch', 'A bouncing copy hits a corner.', clip('frame')),
  old('ac_corner5', 'watch', 'Five corner hits in a run.', clip('eye')),
  old('ac_gold', 'watch', 'Catch a Golden Button.', { k: 'gold', v: 0 }),
  old('ac_gold5', 'watch', 'Catch 5 Golden Buttons in a run.', { k: 'gold', v: 1 }),
  add('n_gold25', 'watch', 'Star Collector', 'You have a jar for them now.', 'Catch 25 Golden Buttons, over every run.', 'life.golden:25', { k: 'gold', v: 2 }),
  old('ac_ad', 'watch', 'Click an ad.', clip('billboard')),
  old('ac_ccp', 'watch', 'Click a real CCP ad.', clip('megaphone')),

  old('ac_fork', 'choose', 'Pick a side at a fork.', clip('sign')),
  old('ac_fork5', 'choose', 'Pick at all five forks in a run.', clip('stairs')),
  old('ac_q', 'choose', 'Answer a pop-up question.', clip('speech')),
  old('ac_q10', 'choose', 'Answer 10 questions in a run.', clip('checklist')),
  old('ac_no', 'choose', 'Refuse a question.', clip('bin')),
  add('n_decline', 'choose', 'Polite No', 'Five times. The terms remain available.', 'Decline a terms card 5 times in a run.', 'decline:5', drawn('n_decline')),

  old('ac_ten', 'time', 'Play 10 minutes in a run.', clip('clock')),
  old('ac_twenty', 'time', 'Play 20 minutes in a run.', clip('metronome')),
  old('ac_thirty', 'time', 'Play 30 minutes in a run.', drawn('ac_thirty')),
  old('ac_hour', 'time', 'Play 60 minutes in a run.', clip('lamp')),
  old('ac_rest', 'time', 'Leave the button alone for 30 seconds.', clip('cloud')),
  old('ac_rest2', 'time', 'Leave the button alone for 2 minutes.', clip('envelope')),
  old('ac_pause', 'time', 'Pause, then come back.', clip('remote')),
  add('n_wake', 'time', 'Awake', 'That is what it is for. Welcome back.', 'Pause and come back in 3 different runs.', 'life.wakeRuns:3', clip('sunrise')),
  add('n_away', 'time', 'While You Were Away', 'It kept going. So did you, somewhere else.', 'Come back to an hour or more of away pay.', 'away1h:1', clip('moon')),

  add('n_wheel', 'play', 'Round and Round', 'It always lands somewhere.', 'Spin the Prize Wheel 10 times in a run.', 'wheel:10', clip('wheel')),
  add('n_slots', 'play', 'Three of a Kind', 'The machine is happy for you.', 'Line up three on the Slots.', 'slots3:1', clip('slotmachine')),
  add('n_toybox', 'play', 'Checkmate', 'The king was never in danger. You were.', 'Finish 5 Toy Boxes in a run.', 'toybox:5', img('assets/toy-box/king.png')),
  add('n_disp', 'play', 'Caught It', 'Nice hands.', 'Catch a Prize Dispenser prize.', 'catch:1', clip('dispenser')),
  add('n_deep', 'play', 'Out of Your Depth', 'It is quieter down here.', 'Reach depth 10 in The Deep End.', 'deep10:1', clip('tunnel')),
  add('n_brk', 'play', 'Clean Sweep', 'Every brick. Even the shy one.', 'Clear a Breakout board.', 'brkclear:1', clip('brickgame')),
  add('n_radio', 'play', 'Tuned In', 'Side A, all the way through.', 'Hear a whole Lo-fi Radio track.', 'track:1', clip('radio')),
  add('n_pod', 'play', 'Regular Listener', 'You know their names now.', 'Hear a whole podcast episode.', 'episode:1', clip('headphones')),

  add('t_sissy', 'themes', 'First Fitting', 'Picked Sissy. It fits.', 'Pick the Sissy theme.', 'theme:sissy', { k: 'portrait', theme: 'sissy' }),
  add('t_bimbo', 'themes', 'Bubbly', 'Picked Bimbo. Thinking went optional.', 'Pick the Bimbo theme.', 'theme:bimbo', { k: 'portrait', theme: 'bimbo' }),
  add('t_goon', 'themes', 'Edge Case', 'Picked Goon. You stayed for the edges.', 'Pick the Goon theme.', 'theme:goon', { k: 'portrait', theme: 'goon' }),
  add('t_drone', 'themes', 'Unit Online', 'Picked Drone. Serial number assigned.', 'Pick the Drone theme.', 'theme:drone', { k: 'portrait', theme: 'drone' }),
  add('t_trance', 'themes', 'Under', 'Picked Trance. Eyes heavy, page bright.', 'Pick the Trance theme.', 'theme:trance', { k: 'portrait', theme: 'trance' }),
  add('t_locked', 'themes', 'Locked In', 'Picked Chastity. The key is somewhere.', 'Pick the Chastity theme.', 'theme:locked', { k: 'portrait', theme: 'locked' }),
  add('t_censored', 'themes', '[Redacted]', 'Picked Censored. You saw ███.', 'Pick the Censored theme.', 'theme:censored', { k: 'portrait', theme: 'censored' }),
  add('t_infection', 'themes', 'Patient Zero', 'Picked Infection. It spreads from here.', 'Pick the Infection theme.', 'theme:infection', { k: 'portrait', theme: 'infection' }),
  add('t_two', 'themes', 'Two Tone', 'Two themes at once. The page is confused too.', 'Buy Second Theme.', 'owns:taste2', clip('palette')),
  add('t_all', 'themes', 'Full Box', 'Every colour in the box.', 'Pick all eight themes, over every run.', 'themes:8', drawn('t_all')),

  add('e_drop', 'endings', 'Drop', 'Everything went quiet and you let it.', 'Reach the Drop ending.', 'ending:drop', drawn('e_drop')),
  add('e_o_out', 'endings', 'Living Button', 'Someone clicked. You felt it.', 'Reach the Living Button ending.', 'ending:o_out', { k: 'card' }),
  add('e_p_life', 'endings', 'Lifetime Plan', 'You are thanked for ever. For ever has been shortened.', 'Reach the Lifetime Plan ending.', 'ending:p_life', drawn('e_p_life')),
  add('e_tr_end', 'endings', 'Count Up', 'Someone is counting you back up. Take your time.', 'Reach the Count Up ending.', 'ending:tr_end', drawn('e_tr_end')),
  add('e_bi_end', 'endings', 'Bimbodoll', 'One pink bubble. It takes everything, then pops.', 'Reach the Bimbodoll ending.', 'ending:bi_end', drawn('e_bi_end')),
  add('e_dr_end', 'endings', 'Unit Complete', 'Shop: off. Name: off. Unit: complete.', 'Reach the Unit Complete ending.', 'ending:dr_end', drawn('e_dr_end')),
  add('e_si_end', 'endings', 'The Debut', 'Curtain up, one spotlight, one slow bow.', 'Reach The Debut ending.', 'ending:si_end', drawn('e_si_end')),
  add('e_go_end', 'endings', 'EMI Takeover', 'She bought the last tile herself.', 'Reach the EMI Takeover ending.', 'ending:go_end', img('assets/img/emi/body-smug.png')),
  add('e_lo_end', 'endings', 'Lost Key', 'A very small splash, a long way out.', 'Reach the Lost Key ending.', 'ending:lo_end', clip('key')),
  add('e_ce_end', 'endings', 'Redacted', 'Your result is ████. Congratulations.', 'Reach the Redacted ending.', 'ending:ce_end', drawn('e_ce_end')),
  add('e_in_end', 'endings', 'Flatline', 'The line stopped being a line and started turning.', 'Reach the Flatline ending.', 'ending:in_end', clip('pulse')),
  add('e_outside', 'endings', 'Go Outside', 'You went outside. It looked lovely on the monitor.', 'Reach the Go Outside ending.', 'ending:outside', { k: 'scene', src: 'assets/outside/meadow.jpg' }),
  add('e_go_mus', 'endings', 'Gooning Museum', 'You took the tour. You were on it.', 'Reach the Gooning Museum ending.', 'ending:go_mus', clip('gallery')),
  add('e_onemore', 'endings', 'One More', 'You could have stopped. There were several good places.', 'Reach the One More ending.', 'ending:onemore', drawn('e_onemore')),
  add('e_three', 'endings', 'Three Ways Out', 'Endless is open now.', 'See three different endings.', 'endings:3', clip('infinity')),
  add('e_all', 'endings', 'Every Ending', 'You have seen how it ends. All of the ways.', 'See every ending still on offer.', 'shelfEndings:6', clip('book')),

  add('p_renew', 'sky', 'Renewed', 'Same terms. Fresh signature.', 'Renew a run once.', 'depth:1', drawn('p_renew')),
  add('p_depth5', 'sky', 'Depth Five', 'You know the way down by now.', 'Reach Depth 5.', 'depth:5', clip('spiral')),
  add('p_core', 'sky', 'Fully Conditioned', 'The middle of the sky. It was you all along.', 'Light the core of the Renewal Sky.', 'core:1', clip('jewel')),
  add('p_llama', 'sky', 'Full Herd', 'Twelve llamas. Nobody asked how.', 'Shelter twelve llamas.', 'llamas:12', drawn('p_llama')),
];

export const STICKERS = Object.freeze(RAW.map((s) => Object.freeze({ ...s, hidden: !!s.hidden, legacy: RETIRED.has(s.id), scope: scopeOf(s.when) })));
export const STICKER_BY_ID = Object.freeze(Object.fromEntries(STICKERS.map((s) => [s.id, s])));
export const STICKER_IDS = Object.freeze(STICKERS.map((s) => s.id));
/* Lifetime counters the book reads (life.tally). Anything else in a save is dropped. */
export const LIFE_TALLIES = Object.freeze(['golden', 'terms', 'wakeRuns']);
