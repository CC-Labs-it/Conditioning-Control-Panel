/* ============================================================================
 * data/content/strict.js - the Tap route. Pure data.
 *
 * Failing here costs nothing, by the row. Tokens: {game}, {cur}, and {n} from
 * the unlock.
 * ==========================================================================*/

/* t_orders Tap Orders: the word, and what it says after. */
export const ORDERS = Object.freeze({
  words: ['TAP NOW', 'NOW', 'TAP', 'AGAIN', 'TAP IT', 'YES, NOW', 'NOT LATER', 'THIS ONE', 'GO', 'NOW, PLEASE'],
  obeyed: ['Good.', 'Fine.', 'That will do.', 'Quicker next time.', 'Acceptable.', 'You may breathe.'],
  missed: ['Too slow.', 'That was the moment. It left.', 'We will pretend that did not happen.', 'No.'],
  early: ['Nobody said tap.', 'Wait to be told.', 'Eager. Noted.'],
});

/* t_denied Refused Taps */
export const DENIED_LINES = Object.freeze({
  refused: ['No.', 'Refused.', 'Not that one.', 'Try that again.', 'We did not like how you did that.', 'Denied, for no reason.', 'Not with that attitude.'],
  next: ['Now yes.', 'Double. Do not ask why.', 'That one you may have.', 'Better. Twice as better.'],
});

/* t_count Tap Countdown: tap {n} before it hits zero. */
export const COUNT_LINES = Object.freeze({
  start: ['Tap {n}. Go.', '{n} taps. Now.', 'You have until zero. {n}.'],
  won: ['Enough.', 'In time. Barely.', 'Good. You may stop. You **will not**.'],
  lost: ['Time. It cost you nothing. Think about that.', 'Zero. Nothing happens. It is very disappointed.', 'Out of time. Again when you are ready. Now.'],
});

/* t_faster Beat Tapping */
export const BEAT_LINES = Object.freeze({
  on: ['On it.', 'Yes.', 'There.', 'Keep up.'],
  off: ['Off.', 'Listen.', 'Other beat.', 'That was your own idea.'],
  faster: ['Faster.', 'Keep up.', 'It is only speeding up.', 'This is the slow part.'],
});

/* t_still Hands-Off Test */
export const STILL_TEST = Object.freeze({
  words: ['DO NOT', 'HANDS OFF', 'NOT NOW', 'LEAVE IT', 'DO NOT EVEN'],
  passed: ['Passed.', 'Fine. You can **be told**.', 'Well, look who has hands and is not using them.', 'Good. That was the whole test.'],
  failed: ['You tapped. It costs nothing. You should know that.', 'We said do not. You did.', 'It was five seconds.'],
});

/* t_type Type the Phrase: lower case, plain letters, easy on a phone. */
export const PHRASES = Object.freeze([
  'i will tap',
  'i will tap when told',
  'i like the button',
  'the button knows best',
  'thank you for the orders',
  'i have nothing else on',
  'this is a normal thing to type',
  'nobody is watching me type this',
  'i could stop',
  'i will not stop',
  'i meant that',
  'again with feeling',
  'yes this is my evening',
  'i was told to and i did',
]);

export const PHRASE_LINES = Object.freeze({
  prompt: 'Type this. Exactly this.',
  right: ['Again. With **feeling**.', 'Good. You can read.', 'Very convincing.', 'Did you mean it. It **does not matter**.'],
  wrong: ['That is not what it says.', 'Spelling is a kindness. Again.', 'Close. Close does not pay.'],
});

/* t_corner Cornered Button */
export const CORNER_LINES = Object.freeze({
  sent: ['Think about what you did.', 'In the corner.', 'You know why.', 'Face the wall. Well, it does.'],
  back: ['You may come out.', 'Have you thought about it. Good.', 'Back you come. Do not do it again, whatever it was.'],
});

/* t_grade Performance Review: every 2 min, a stamp and a note. */
export const REVIEWS = Object.freeze({
  title: 'Performance Review',
  A: ['Exceeds expectations. Expectations were low.', 'A rating this good is suspicious. Paid anyway.', 'Keep this up, and we will find something else.', 'Double pay for a while. Do not get used to it.'],
  B: ['Fine. Fine is not a compliment.', 'Solid. A solid what is not for us to say.', 'You could do better, and we have told your hands.'],
  C: ['Present. That is the whole note.', 'Average, in a very crowded room.', 'You tapped. Let us leave it at that.'],
  D: ['See me.', 'We have seen the taps. We have concerns about the taps.', 'Someone has been resting. We do not name names. It was you.'],
  F: ['Nope.', 'See me. Bring your hands.', 'Nothing extra this time. Nothing taken either. We are not monsters.', 'Your file has been moved to a lower drawer.'],
});
