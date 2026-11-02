/* ============================================================================
 * data/content/sweet.js - the Rest route. Pure data.
 *
 * Tokens: {game} and {cur} from data/brand.js. The unlock fills {item} (the
 * copy.json name of the last buy) and {pay} (a formatted number).
 * ==========================================================================*/

/* s_good Praise: soft words every so often. Any order. */
export const PRAISE = Object.freeze([
  'Good job.',
  'Well done.',
  'So good.',
  'You are doing so well.',
  'You are doing so well. At what, who knows.',
  'That was a lovely tap.',
  'Nobody taps like that. Nobody has checked.',
  'Very nice hands.',
  'That is exactly how it is done.',
  'You are a natural at this, whatever this is.',
  'We are all very proud. There is no we.',
  'Take a moment. You earned it. You earned several.',
  'Good. Just like that.',
  'Perfect. Do not change a thing. Do not do a thing.',
  'You make this look easy. It is a button.',
  'Everyone is saying how well you are sitting.',
  'Lovely stillness.',
  'That was some of your best nothing.',
  'Your nothing has really come along.',
  'You have a gift. It is in the post.',
  'So calm. So soft. So nearly asleep.',
  'Good. That is all. Good.',
  'You needed this. Someone should have told you sooner.',
  'There you are.',
  'You can let that go. Whatever you were holding. Yes, that.',
  'Not a single thing is expected of you.',
  'Such a quiet mind. We mean that nicely.',
  'You are very easy to please. That is not a criticism.',
  'Soft hands. Soft head. Good.',
  'Best one yet.',
  'Better than the last one.',
  'The last one was also very good.',
  'You do not have to earn this. You are earning it anyway.',
  'All of this, and you did not even try.',
  'Keep being like this.',
  'You are exactly the right amount of here.',
  'If you were any more relaxed we would have to call someone.',
  'Your mother would be very, well, let us not.',
  'What a good, soft, empty afternoon.',
  'There is nothing left to improve. We removed it.',
  'That is a very good face you are making.',
  'Yes. Good. Yes.',
  'Mm. That is it.',
  'You are the best person currently looking at this.',
  'Whoever told you to try harder was wrong.',
]);

/* s_good2 Personal Praise: names what was just bought. */
export const PRAISE_NAMED = Object.freeze([
  '{item}. Wow. Look at you.',
  '{item}. What a choice. What a person.',
  'You bought {item}. Everyone is so proud.',
  'Nobody else would have thought of {item}.',
  '{item}, at a time like this. Brave.',
  'We heard about {item}. We are telling people.',
  '{item} looks lovely on you.',
  'You and {item}. It was always going to be you and {item}.',
  'Such taste. {item}. Such taste.',
  'A person who owns {item} can rest now.',
  'Good. {item}. Good.',
  'That was the right {item}.',
]);

/* s_rest Rest Bonus */
export const REST_LINES = Object.freeze({
  label: 'no taps',
  resting: ['Stop. That is it.', 'Hands off. Nicely.', 'Nothing. Yes. More of that.', 'No taps. Lovely.', 'Your best work so far.', 'Ten seconds of nothing. Well held.'],
  broken: ['You tapped. It is fine. Start again.', 'Oh, hello, hands.', 'That is all right. Back to nothing.'],
});

/* s_tuck Quiet Taps */
export const TUCK_LINES = Object.freeze(['Hands where they can relax.', 'Shh.', 'Softer.', 'Even the click is tired.', 'You can barely hear that one.', 'That one was mostly a rumour.']);

/* s_glow Warm Glow */
export const GLOW_LINES = Object.freeze(['Doing nothing has never paid better.', 'The number is growing on its own. It gets that from you.', 'Still growing. Still you, not moving.', 'Nothing is what got you here.', 'This is the most you have ever achieved by sitting.']);

/* s_blanket Warm Colours and s_lull Slow Music: small toasts as they arrive. */
export const WARM_LINES = Object.freeze(['It was cold before. You just did not know.', 'Someone turned the edges down.', 'Nothing is due.', 'Nothing was ever due.', 'This one has no chorus. It has given up on choruses.']);

/* s_nap Away Earnings: capped at 10 min by the row. */
export const NAP_LINES = Object.freeze({
  tabTitles: ['Still earning', 'Go on, then', 'Back whenever', 'It is fine without you'],
  welcome: [
    'You were gone. It went fine.',
    'Welcome back. It missed you. It did not stop, though.',
    '{pay} {cur} while you were out. Imagine if you never came back.',
    'You were away and got paid. There is a word for that, and it is a very nice word.',
    'See. Nothing happened, and it cost you nothing, and here is {pay} {cur}.',
  ],
  capped: 'It only saves ten minutes. Then it stopped and waited for you, which is sweet.',
});
