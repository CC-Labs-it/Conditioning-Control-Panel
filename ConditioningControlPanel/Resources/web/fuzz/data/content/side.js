/* ============================================================================
 * data/content/side.js - Side Toys, plus on-screen words for trunk, eyes, ears,
 * and the feed. Pure data.
 *
 * Spoken words are recorded clips only. `clip` names a slug in data/words.js
 * CIRCE; null is silent, the caption still shows.
 * Tokens: {game}, {cur}; the unlock fills {n}, {word} and {min}.
 * ==========================================================================*/

const heard = (clip, word, decoys) => Object.freeze({ clip, word, decoys: Object.freeze(decoys) });

/* listen Listening Test: EMI asks which word was just heard. Right pays 12 s of rate. */
export const LISTENING_TEST = Object.freeze({
  ask: ['Which word?', 'What did you just hear?', 'Which one was it?', 'Pay attention. Which word?', 'Were you listening. Which word?'],
  right: ['Good ears.', 'You were listening. I am surprised.', 'That is the one. Have some {cur}.', 'Correct. Do not let it go to your head. Let everything go to your head.'],
  wrong: ['No. It was {word}.', 'It was {word}. It is on the recording and everything.', 'Close. It was not close. It was {word}.', 'You heard what you wanted. It was {word}.'],
  skipped: ['Too slow. I will ask you again when you are not expecting it.', 'Fine. Nothing lost. Except that word.'],
  words: Object.freeze([
    heard('relax', 'relax', ['release', 'reset']),
    heard('release', 'release', ['relax', 'freeze']),
    heard('reset', 'reset', ['relax', 'release']),
    heard('sink', 'sink', ['soft', 'drift']),
    heard('drift', 'drift', ['sink', 'drop']),
    heard('drop', 'drop', ['deeper', 'drift']),
    heard('deeper', 'deeper', ['softer', 'drop']),
    heard('softer', 'softer', ['deeper', 'slower']),
    heard('breathe', 'breathe', ['freeze', 'blank']),
    heard('freeze', 'freeze', ['breathe', 'focus']),
    heard('focus', 'focus', ['freeze', 'listen']),
    heard('listen', 'listen', ['focus', 'let go']),
    heard('let-go', 'let go', ['listen', 'empty']),
    heard('empty', 'empty', ['blank', 'let go']),
    heard('blank', 'blank', ['empty', 'trance']),
    heard('trance', 'trance', ['blank', 'obey']),
    heard('obey', 'obey', ['submit', 'trance']),
    heard('submit', 'submit', ['obey', 'denied']),
    heard('denied', 'denied', ['submit', 'edge']),
    heard('edge', 'edge', ['denied', 'mindless']),
    heard('mindless', 'mindless', ['so calm', 'edge']),
    heard('so-calm', 'so calm', ['quiet mind', 'mindless']),
    heard('quiet-mind', 'quiet mind', ['so calm', 'empty']),
  ]),
});

/* opinions Voice Reactions: caption + clip. No clip stays silent, never synthesised. */
const say = (caption, clip = null) => Object.freeze({ caption, clip });
export const OPINIONS = Object.freeze({
  crit: Object.freeze([say('Deeper.', 'deeper'), say('Drop.', 'drop'), say('Sink deeper.', 'sink-deeper'), say('Good.')]),
  buy: Object.freeze([say('Obey.', 'obey'), say('Obedient.', 'obedient'), say('Submit.', 'submit'), say('Again.')]),
  corner: Object.freeze([say('Drop deeper.', 'drop-deeper'), say('Focus.', 'focus'), say('Finally.')]),
  refused: Object.freeze([say('Denied.', 'denied'), say('Do not touch.', 'don-t-touch')]),
  golden: Object.freeze([say('Listen.', 'listen'), say('Edge.', 'edge')]),
});

/* softw Lucky Taps, e_bounce, e_flash, e_sublim, e_blink: general words, in two sizes. */
export const LUCKY_WORDS = Object.freeze(['soft', 'easy', 'sink', 'slow', 'warm', 'calm', 'nice', 'more', 'good', 'yes', 'down', 'float']);
/* The little bouncing copies (Bouncing Button): plain triggers first, then the picked theme's own (owner 2026-11-08). */
export const STRAY_WORDS = Object.freeze(['DROP', 'SINK', 'RELAX', 'DEEP', 'SOFT', 'CALM', 'BLANK', 'FLOAT', 'LET GO', 'GOOD', 'STILL', 'DOWN']);
export const THEME_STRAY_WORDS = Object.freeze({
  trance: Object.freeze(['SLEEP', 'DEEPER', 'DRIFT', 'SWAY', 'HEAVY', 'FADE', 'UNDER', 'FOCUS']),
  bimbo: Object.freeze(['GIGGLE', 'PRETTY', 'DUMB', 'POUT', 'GLOSS', 'SHINY', 'EMPTY', 'PINK']),
  drone: Object.freeze(['OBEY', 'COMPLY', 'UNIT', 'SYNC', 'SERVE', 'IDLE', 'UPLOAD', 'HIVE']),
  sissy: Object.freeze(['CURTSY', 'PRETTY', 'LACE', 'POLITE', 'KNEEL', 'SWEET', 'PROPER', 'BOW']),
  goon: Object.freeze(['STROKE', 'EDGE', 'MORE', 'PUMP', 'GOON', 'AGAIN', 'STARE', 'LEAK']),
  locked: Object.freeze(['LOCKED', 'DENIED', 'CAGED', 'WAIT', 'BEG', 'KEY', 'NO', 'PATIENT']),
  censored: Object.freeze(['CENSOR', 'BLUR', 'BEEP', 'HIDDEN', 'BARS', 'SHH', 'NOPE', 'BLOCKED']),
  infection: Object.freeze(['SPREAD', 'INFECT', 'FEVER', 'ITCH', 'SHARE', 'CATCH', 'VIRAL', 'DRIP']),
});
export const BOUNCE_WORDS = Object.freeze(['DROP', 'SINK', 'SOFT', 'DEEP', 'CALM', 'BLANK', 'OBEY', 'MORE', 'YES', 'PINK', 'FLOAT', 'GOOD']);
export const BIG_WORDS = Object.freeze(['RELAX', 'DEEPER', 'LET GO', 'BLANK', 'BREATHE', 'EMPTY', 'SOFTER', 'GOOD', 'SO CALM', 'DOWN', 'MORE', 'STAY']);
export const EDGE_WORDS = Object.freeze(['soft', 'sink', 'deep', 'calm', 'stay', 'yes', 'drift', 'still', 'good', 'blank', 'obey', 'more', 'pink', 'nothing']);
export const STILL_WORDS = Object.freeze(['there you are', 'keep still', 'good', 'stay', 'it only comes when you stop', 'nobody else can see this', 'nearly', 'yes, like that']);

/* e_stare Stare Break */
export const STARE_LINES = Object.freeze({
  start: ['Just look.', 'Stop tapping. Look at it.', 'Eyes on the spiral. That is the whole job.', 'Look. Nothing else is due.'],
  done: ['Thirty seconds of pay, for that.', 'Good looking.', 'You can blink now. You already did.'],
  broken: ['You looked away. It is fine. It noticed.', 'Hands. We said eyes.'],
});

/* a_phones Headphones */
export const PHONES_PROMPT = Object.freeze({
  ask: 'Put your headphones on. It matters later.',
  yes: 'They are on',
  no: 'I do not have any',
  thanks: ['Good. It is about to get wider.', 'All right. It will be narrower for you. You will cope.'],
});

/* title Disguised Tab: the row's title first. */
export const TAB_TITLES = Object.freeze(['Quarterly Report.xlsx', 'Budget FINAL v3.xlsx', 'Meeting Notes (2).docx', 'Q3 Vendor Audit.pdf', 'Inbox zero strategy.pptx', 'Printer drivers']);

/* gold Golden Button: four prizes, weighted in ui/unlocks/basics/side.js. Frenzy is x7 on the button, Deep x5 per-second pay. */
export const GOLDEN = Object.freeze({
  pickLine: 'Keep the change.',
  prizes: Object.freeze([
    Object.freeze({ key: 'lump', label: '+{n}', line: 'That is real pretend money.' }),
    Object.freeze({ key: 'frenzy', label: 'Frenzy x7', line: 'Every tap counts seven times. For a little while. Tap, then.' }),
    Object.freeze({ key: 'free', label: 'Free buy', line: 'The next one is on us. We will regret it.' }),
    Object.freeze({ key: 'deep', label: 'Deep x5', line: 'Everything pays five times over while you stay. So stay.' }),
  ]),
  missed: ['It left. It is not hurt. It is a little hurt.', 'That was a [[gold:gold]] button. Was it. Who can say now.'],
});

/* screen Screen Time: a wellness feature. */
export const SCREEN_TIME = Object.freeze({
  title: 'Screen Time',
  today: 'Today: {min} min',
  sources: Object.freeze({ taps: 'Taps', strays: 'Bouncing', ads: 'Ads', idle: 'Doing nothing', other: 'Unclear' }),
  tips: [
    'A wellness feature.',
    'Consider taking a break. Consider it. That was the break.',
    'You are in the top percent of something.',
    'You have looked at this more than you have looked at anyone.',
    'Most of it came from doing the same thing.',
    'That is a lot higher than yesterday. You were not here yesterday.',
    'Your eyes have been informed.',
    'Experts recommend. They did not finish.',
  ],
});

/* levels Levels */
export const LEVELS = Object.freeze({
  label: 'Level {n}',
  collect: 'Collect',
  up: ['Numbers about numbers.', 'You went up. Nothing else did.', 'Level up. It came with this message.', 'That is a very high level of something.', 'Everyone at your level is also you.'],
});

/* wheel Prize Wheel: no real money, ever. */
export const WHEEL = Object.freeze({
  spin: 'Spin',
  wait: 'Next spin in {n} s',
  note: 'Free. Pays only {cur}.',
  results: ['Spun. Paid. Nobody has learned anything.', 'The house would like you to know it is fine.', 'Daily Daze, hourly.', 'A bit. A bit is still some.', 'A big one. Do not tell the wheel.'],
});

/* boxes Mystery Boxes */
export const BOXES = Object.freeze({
  labels: ['Box', 'Another box', 'Not a trap', 'Fragile', 'This way up'],
  opened: ['It was {cur}. It is always {cur}.', 'Surprise mechanics.', 'Inside was a smaller amount of box.', 'You opened it. Well done. It was shut.', 'Contains: some.'],
  expired: ['That box has gone home.', 'Too slow. It was probably great.'],
});

/* sparkle, toy, buzz: only say what they really do, and where. */
export const APP_ONLY = Object.freeze({
  sparkle: { tag: 'app only', web: 'In the CCP app, boxes can drop a real Sparkle, its reward currency. Here they cannot.', app: 'A real Sparkle. Cleverly not a microtransaction.' },
  toy: { tag: 'app only', web: 'In the CCP app, a connected toy follows how fast you earn. Here it does nothing.', app: 'It heard you. It follows your rate, up to your cap.' },
  buzz: { app: 'Your toy buzzes on lucky taps.', web: 'Your phone buzzes on lucky taps. Android browsers only.', off: 'This browser cannot buzz. It felt that anyway.' },
});

/* triggers Falling Words: rows name these three. */
export const FALLING_WORDS = Object.freeze([
  Object.freeze({ word: 'FOCUS', effect: 'crit', label: 'Crit', line: 'Caught.' }),
  Object.freeze({ word: 'BLANK', effect: 'mute', label: 'mute 6 s', line: 'Six seconds of nothing. You are welcome.' }),
  Object.freeze({ word: 'DEEPER', effect: 'speed', label: 'faster', line: 'They are all going faster now. Good.' }),
]);

/* remote Friend Remote: preset buttons only, never panic, never free text. */
export const REMOTE = Object.freeze({
  title: 'Someone else has the button now',
  share: 'Copy link for a friend',
  presets: Object.freeze([
    Object.freeze({ key: 'poke', label: 'Poke', toast: 'A friend poked you.' }),
    Object.freeze({ key: 'spin', label: 'Spin', toast: 'A friend spun the spiral.' }),
    Object.freeze({ key: 'pink', label: 'Pink', toast: 'A friend made it [[pink:pinker]].' }),
    Object.freeze({ key: 'tap', label: 'Tap', toast: 'A friend tapped for you. Say thanks. You cannot.' }),
    Object.freeze({ key: 'hush', label: 'Shh', toast: 'A friend told you to be quiet.' }),
  ]),
  note: 'They get buttons, not words. Pause is always yours.',
});

/* drain Screen Blur, flash Flash Picture: arrival toasts. */
export const DRIFT_LINES = Object.freeze(['Where were you.', 'It will clear. Stay there.', 'You were looking at something.', 'Blink and you will see it.']);

/* d_* Swipe Feed words. */
export const FEED_LINES = Object.freeze({
  /* d_end Fake Progress Bar */
  endTag: 'bottom of the feed',
  endNear: ['Almost.', 'Nearly there.', 'One more screen.', 'You can see the end from here.', 'It is just down there.'],
  /* d_refresh Pull to Refresh */
  pull: 'pull down',
  refreshMiss: ['Nothing new.', 'Same as before, refreshed.', 'Maybe this time.', 'Not this time.', 'It did something. It did not say what.'],
  refreshHit: ['There it is.', 'That was worth all the others.'],
  /* d_streak Swipe Streak */
  streak: ['Streak {n}', 'Do not break it.', 'Your thumb is on fire. It is a figure of speech. Check.'],
  streakLost: ['It broke. There is another one starting. There always is.'],
  /* d_foryou For You Feed */
  forYou: ['more of this', 'because you lingered', 'you stopped on one like this', 'people like you also', 'it noticed'],
  /* d_like Double-Tap Like */
  liked: ['It noticed.', 'Saved to nowhere.', 'Liked. Nobody will see that. Somebody saw.'],
  /* v_video Mandatory Video */
  video: 'no X',
  /* v_fav Favourite Pictures */
  fav: ['It learns. So do you.', 'More like that, then.', 'Noted. It will be held against you, gently.'],
});

/* depth Achievements: each fills the meter. `when` is the spine's to wire, or to ignore. */
/* hidden: never listed before it is earned; it still lands as a normal card and fills the meter. */
const a = (id, name, line, when, hidden = false) => Object.freeze({ id, name, line, when, hidden });
export const ACHIEVEMENTS = Object.freeze([
  a('ac_first', 'Button Pressed', 'You did it. It was very easy.', 'taps:1'),
  a('ac_100', 'One Hundred', 'You were not going to stop at ninety nine.', 'taps:100'),
  a('ac_1k', 'A Thousand Taps', 'That is a habit now.', 'taps:1000'),
  a('ac_5k', 'Five Thousand', 'Please check on your finger.', 'taps:5000'),
  a('ac_buy1', 'First Purchase', 'Now you have a thing.', 'buys:1'),
  a('ac_buy10', 'Ten Things', 'Each one was necessary.', 'buys:10'),
  a('ac_buy25', 'Shopping Problem', 'It is not a problem. It is a page.', 'buys:25'),
  a('ac_buy50', 'Fifty Things', 'Nobody can see the button.', 'buys:50'),
  a('ac_buy100', 'A Joyful Mess', 'We are very sorry. No we are not.', 'buys:100'),
  a('ac_cur1k', 'Four Figures', 'Some {cur}. You could buy a little something.', 'total:1000'),
  a('ac_cur100k', 'Six Figures', 'You are doing very well in a currency that is not real.', 'total:100000'),
  a('ac_cur1m', 'A Million {cur}', 'Your family would be so confused.', 'total:1000000'),
  a('ac_corner', 'The Corner', 'It finally hit the corner. Nobody else saw.', 'corner:1'),
  a('ac_corner5', 'Corner Regular', 'You have started waiting for it.', 'corner:5'),
  a('ac_gold', 'Gold', 'Keep the change.', 'golden:1'),
  a('ac_gold5', 'Edge Watcher', 'You watch the edges now.', 'golden:5'),
  a('ac_fork', 'A Decision', 'You locked some things out. Do not think about them.', 'forks:1'),
  a('ac_fork5', 'All Decided', 'That is everyone you could have been, gone.', 'forks:5'),
  a('ac_rest', 'Hands Off', 'Thirty seconds of nothing. A personal best.', 'rest:30'),
  a('ac_rest2', 'Away Message', 'Two minutes without a single tap. You are a natural.', 'rest:120'),
  a('ac_ten', 'Ten Minutes', 'That went fast. It always does.', 'minutes:10'),
  a('ac_twenty', 'Twenty Minutes', 'You said five.', 'minutes:20'),
  a('ac_thirty', 'Half an Hour', 'Somewhere a kettle has gone cold.', 'minutes:30'),
  a('ac_hour', 'An Hour', 'We were going to stop you. We did not.', 'minutes:60'),
  a('ac_ad', 'Sponsored', 'You looked at an ad on purpose.', 'adclick:1'),
  a('ac_ccp', 'The Real One', 'You pressed the honest ad. It was a bit awkward for everyone.', 'ccpclick:1'),
  a('ac_q', 'Quick Question', 'You answered. That is how it starts.', 'answers:1'),
  a('ac_q10', 'Good Respondent', 'Ten answers. Your responses are important to us.', 'answers:10'),
  a('ac_no', 'A Refusal', 'You found the No. Well done. It is lonely there.', 'refusals:1'),
  a('ac_pause', 'Back Again', 'You paused and came back. Nobody said you had to.', 'resume:1'),
  /* Speed, hidden: trusted manual presses on the main button, held for 3 seconds (sim/tap-rate.js TAP_BURSTS). */
  a('ac_tap8', 'Quick Fingers', 'Eight a second, for three whole seconds.', 'tapburst8:1', true),
  a('ac_tap12', 'Blur', 'Twelve a second. The button felt that.', 'tapburst12:1', true),
  a('ac_tap15', 'Too Fast', 'Fifteen a second. Please put the finger down.', 'tapburst15:1', true),
]);
