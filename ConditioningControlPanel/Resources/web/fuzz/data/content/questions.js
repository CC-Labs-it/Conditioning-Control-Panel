/* ============================================================================
 * data/content/questions.js - Pop-Up Question and the Button Trouble lane.
 *
 * TRICKS is the set from Resources/web/intake/core/contracts.js (Steer and the
 * Mechanic captcha family) that suits a clicker. Left out on purpose:
 * occlude-gif, defocus, exile, shrink-hit, overflow-hit, assist-click, tunnel,
 * drift-resolve and decay: on a phone they read the same as tricks in the list
 * or cannot be seen at all. Melt here is melt-away on one answer, not
 * the whole-card melt of intake/core/melt.js, but the rule carries over: a
 * melted answer scores nothing and takes nothing from the player.
 *
 * Every trick stays completable (friction, not lockout, invariant 1 of
 * contracts.js): each question closes after any answer, tone 'no' still pays,
 * and the player may press Wake Up at any moment.
 *
 * Every question has four answers and exactly one is what the game wants:
 * tone 'yes'. The game always wants the same thing: stay, keep going, think
 * less, agree. Learning that is the whole skill. The other three are tone 'no'
 * and pay a little. Only Yes is the exception, on purpose: all four say yes.
 * The card shuffles them, except on the slider, which reads low to high.
 * Keep labels short: a card can be as narrow as a 200 px rail, and bubbles and
 * decoys are smaller still.
 *
 * Optional extras: phrase (wobbly text), want + decoy (picture puzzles).
 *
 * Tokens: {game} and {cur}, filled from data/brand.js.
 * ==========================================================================*/

/** trick id -> the unlock that turns it on, and the intake name it came from. */
export const TRICKS = Object.freeze({
  'none': Object.freeze({ unlock: 'quiz', from: 'Steer.None' }),
  'melt-away': Object.freeze({ unlock: 'melt', from: 'Steer.MeltAway' }),
  'hover-swap': Object.freeze({ unlock: 'swap', from: 'Steer.HoverSwap' }),
  'flee': Object.freeze({ unlock: 'flee', from: 'Steer.Flee' }),
  'nested-nag': Object.freeze({ unlock: 'nag', from: 'Steer.NestedNag' }),
  'crowd': Object.freeze({ unlock: 'crowd', from: 'Steer.Crowd' }),
  'bubble-pop': Object.freeze({ unlock: 'pbubble', from: 'Mechanic.BubblePop' }),
  'lean-slider': Object.freeze({ unlock: 'slider', from: 'Mechanic.CheckIn' }),
  'size-skew': Object.freeze({ unlock: 'shrink', from: 'Steer.SizeSkew' }),
  'opacity-skew': Object.freeze({ unlock: 'fadeno', from: 'Steer.OpacitySkew' }),
  'magnet': Object.freeze({ unlock: 'magnet', from: 'Steer.Magnet' }),
  'late-bloom': Object.freeze({ unlock: 'late', from: 'Steer.LateBloom' }),
  'hold-refuse': Object.freeze({ unlock: 'holdno', from: 'Steer.HoldRefuse' }),
  'drag-reveal': Object.freeze({ unlock: 'dragno', from: 'Steer.DragReveal' }),
  'bottomless-no': Object.freeze({ unlock: 'stackno', from: 'Steer.BottomlessNo' }),
  'verify-checkbox': Object.freeze({ unlock: 'robot', from: 'Mechanic.VerifyCheckbox' }),
  'verify-grid': Object.freeze({ unlock: 'grid', from: 'Mechanic.VerifyGrid' }),
  'verify-transcribe': Object.freeze({ unlock: 'ttype', from: 'Mechanic.VerifyTranscribe' }),
  'verify-regen': Object.freeze({ unlock: 'regen', from: 'Mechanic.VerifyRegen' }),
  'mono': Object.freeze({ unlock: 'onlyyes', from: 'Mechanic.Mono' }),
  'mouse-hijack': Object.freeze({ unlock: 'hijack', from: 'Steer.MouseHijack' }),
});

/** What an answer pays, before ctx.pay's side budget. Seconds of income, with a floor from what taps pay so a Tap route gets paid too.
 *  The right answer pays many times what it used to (4 s), a wrong one a token. */
export const QUIZ_PAY = Object.freeze({ right: Object.freeze({ fps: 20, click: 40 }), wrong: Object.freeze({ fps: 1, click: 3 }) });
export function quizPay(rates, right) {
  const k = right ? QUIZ_PAY.right : QUIZ_PAY.wrong;
  return Math.max((rates.fps || 0) * k.fps, (rates.click || 0) * k.click);
}

const Y = (label) => ({ label, tone: 'yes' });
const N = (label) => ({ label, tone: 'no' });
const q = (id, trick, text, answers, extra) => ({ id, text, answers, trick, ...(extra || {}) });

export const QUESTIONS = Object.freeze([
  /* Plain questions. No trick, which at this point is the trick. */
  q('q_calm', 'none', 'Feeling calm?', [Y('Calm enough to stay'), N('Not especially'), N('Define calm'), N('I was')]),
  q('q_going', 'none', 'Keep going?', [Y('Yes'), N('No'), N('After this one'), N('Going where')]),
  q('q_breath', 'none', 'When did you last breathe on purpose?', [Y('Is that optional'), N('Just now'), N('This morning'), N('Ask my doctor')]),
  q('q_thing', 'none', 'Is this what you meant to do today?', [Y('It is now'), N('No'), N('I had a list'), N('Not entirely')]),
  q('q_read', 'none', 'Did you read this question?', [Y('I just agreed'), N('Yes'), N('Partly'), N('Which question')]),
  q('q_hobby', 'none', 'Would you call this a hobby?', [Y('I would call it home'), N('Yes'), N('No'), N('A phase')]),
  q('q_doing', 'none', 'How is the tapping?', [Y('Not enough of it'), N('Going well'), N('Sore'), N('It is going')]),
  q('q_stop', 'none', 'Could you stop right now?', [Y('Why would I'), N('Easily'), N('Soon'), N('Give me a minute')]),
  q('q_tell', 'none', 'Would you tell anyone about this?', [Y('Tell them what'), N('No'), N('My therapist'), N('Only online')]),
  q('q_number', 'none', 'Is the number big enough yet?', [Y('Bigger'), N('Yes'), N('Not about the number'), N('Which number')]),

  /* Melting Answers */
  q('q_melt1', 'melt-away', 'Do you need that thought?', [Y('Not really'), N('I do'), N('Which thought'), N('It was expensive')]),
  q('q_melt2', 'melt-away', 'Would you like to stay a bit longer?', [Y('As long as you like'), N('No'), N('Five more minutes'), N('I have a train')]),
  q('q_melt3', 'melt-away', 'Are you in charge here?', [Y('Not any more'), N('I am'), N('Who is asking'), N('We share it')]),

  /* Swapping Answers */
  q('q_swap1', 'hover-swap', 'One of these is right. Pick it.', [Y('This one'), N('That one'), N('The other one'), N('Neither one')]),
  q('q_swap2', 'hover-swap', 'Are you sure of your choices today?', [Y('Less so now'), N('Certain'), N('Mostly'), N('What choices')]),
  q('q_swap3', 'hover-swap', 'Left or right?', [Y('Wherever you put me'), N('Left'), N('Right'), N('Up')]),

  /* Fleeing Answer */
  q('q_flee1', 'flee', 'Do you want to leave?', [Y('I want to stay'), N('Leave'), N('Later'), N('Where is the door')]),
  q('q_flee2', 'flee', 'Say no to the nice button?', [Y('Never'), N('Catch me first'), N('Once'), N('Politely')]),
  q('q_flee3', 'flee', 'Is it hard to refuse?', [Y('Refuse what'), N('A bit'), N('Not at all'), N('Only on weekdays')]),

  /* Are You Sure */
  q('q_nag1', 'nested-nag', 'Would you like another spiral?', [Y('Yes please'), N('No'), N('A small one'), N('I have enough')]),
  q('q_nag2', 'nested-nag', 'Stop thinking about the thing?', [Y('What thing'), N('No'), N('I am trying'), N('It is a good thing')]),
  q('q_nag3', 'nested-nag', 'Agree with the button?', [Y('Agree'), N('Disagree'), N('Abstain'), N('Need to read more')]),

  /* Decoy Answers. Small buttons: keep these short. */
  q('q_crowd1', 'crowd', 'Which of these is the real no?', [Y('Does it matter'), N('This no'), N('That no'), N('Also no')]),
  q('q_crowd2', 'crowd', 'Do you know what you want?', [Y('You tell me'), N('Yes'), N('No'), N('More or less')]),
  q('q_crowd3', 'crowd', 'Do you trust this form?', [Y('Completely'), N('No'), N('Some of it'), N('Who wrote it')]),

  /* Bubble Answers. Small bubbles: keep these shorter still. */
  q('q_bub1', 'bubble-pop', 'Pop what you no longer need.', [Y('Doubts'), N('Dinner'), N('Sleep'), N('Friends')]),
  q('q_bub2', 'bubble-pop', 'What is floating away?', [Y('Everything'), N('Nothing'), N('My keys'), N('A balloon')]),

  /* Leaning Slider. Low to high: it leans to the last one. */
  q('q_sl1', 'lean-slider', 'How calm, 1 to 10?', [N('1'), N('4'), N('7'), Y('10')], { lean: 'high' }),
  q('q_sl2', 'lean-slider', 'How much do you still remember?', [N('All of it'), N('Most'), N('Some'), Y('What')], { lean: 'high' }),
  q('q_sl3', 'lean-slider', 'Rate your own free will.', [N('Mine'), N('Shared'), N('Rented'), Y('Pending')], { lean: 'high' }),

  /* Shrinking Answer */
  q('q_shr1', 'size-skew', 'Is it all right to enjoy this?', [Y('It is encouraged'), N('No'), N('In moderation'), N('Ask me later')]),
  q('q_shr2', 'size-skew', 'Bigger number or smaller doubts?', [Y('Both'), N('Neither'), N('The number'), N('The doubts')]),
  q('q_shr3', 'size-skew', 'No pressure. Keep going?', [Y('Yes'), N('No'), N('That was pressure'), N('In a minute')]),

  /* Fading No */
  q('q_fade1', 'opacity-skew', 'Was there another option?', [Y('I do not see one'), N('There was'), N('Two others'), N('It is fading')]),
  q('q_fade2', 'opacity-skew', 'Would you rather stop?', [Y('Not now'), N('I would'), N('Soon'), N('Stop what')]),

  /* Magnet Answer */
  q('q_mag1', 'magnet', 'Which one likes you?', [Y('This one'), N('Not that one'), N('None of them'), N('They are buttons')]),
  q('q_mag2', 'magnet', 'Do you always go where you are pulled?', [Y('It seems so'), N('No'), N('Only downhill'), N('On a good day')]),
  q('q_mag3', 'magnet', 'Going somewhere?', [Y('Over there'), N('Elsewhere'), N('Home'), N('Out')]),

  /* Late No */
  q('q_late1', 'late-bloom', 'Would you like to think about it?', [Y('No need'), N('I did, and no'), N('Give me a day'), N('Thinking now')]),
  q('q_late2', 'late-bloom', 'Stay?', [Y('Stay'), N('Finally, no'), N('Go'), N('Roll over')]),

  /* Hold to Refuse. The widget labels the hold itself. */
  q('q_hold1', 'hold-refuse', 'Are you sure you have the strength?', [Y('No need for it'), N('Yes'), N('No'), N('On a good day')]),
  q('q_hold2', 'hold-refuse', 'Would you rather think for yourself?', [Y('Later'), N('I would'), N('Now and then'), N('Out loud')]),
  q('q_hold3', 'hold-refuse', 'One more minute?', [Y('Sure'), N('No'), N('Just the one'), N('Thirty seconds')]),

  /* Dig for No */
  q('q_drag1', 'drag-reveal', 'The refusals are under here somewhere.', [Y('Never mind'), N('Found one, no'), N('Still looking'), N('No, twice')]),
  q('q_drag2', 'drag-reveal', 'Clear your head before you answer.', [Y('It is clear'), N('Still in there'), N('Mostly clear'), N('Not a chance')]),

  /* Bottomless No */
  q('q_stack1', 'bottomless-no', 'Keep going? Six more of these.', [Y('Yes'), N('No'), N('No again'), N('Still no')]),
  q('q_stack2', 'bottomless-no', 'How many times can you say it?', [Y('I will not say it'), N('No'), N('No no'), N('No no no')]),

  /* Fake Robot Check. Plainly a joke, and nothing to fill in. */
  q('q_robot1', 'verify-checkbox', 'I am not a robot. Hold. Calmly.', [Y('Not a robot'), N('Not sure now'), N('Part robot'), N('Beep')]),
  q('q_robot2', 'verify-checkbox', 'Confirm you are still a person. Hold.', [Y('Person'), N('Roughly'), N('On paper'), N('Ask around')]),

  /* Picture Puzzle */
  q('q_grid1', 'verify-grid', 'Select all spirals.', [Y('Done'), N('Skip'), N('No spirals'), N('Spiral what')], { want: 'spiral', decoy: 'socks' }),
  q('q_grid2', 'verify-grid', 'Select all squares that apply to you.', [Y('Done'), N('Skip'), N('None apply'), N('I am a square')], { want: 'spiral', decoy: 'hat' }),

  /* Wobbly Text: copy the rude thing out. */
  q('q_tt1', 'verify-transcribe', 'Copy the text. Case sensitive.', [Y('Done'), N('Give up'), N('Later'), N('Is that about me')], { phrase: 'YOU SMELL' }),
  q('q_tt2', 'verify-transcribe', 'Copy the text. We know it is hard.', [Y('Done'), N('Give up'), N('It is not hard'), N('Rude')], { phrase: 'STILL HERE THEN' }),
  q('q_tt3', 'verify-transcribe', 'Copy the text. Take your time.', [Y('Done'), N('Give up'), N('Not my evening'), N('Close enough')], { phrase: 'THIS IS YOUR EVENING' }),

  /* Endless Grid */
  q('q_reg1', 'verify-regen', 'Clear them all.', [Y('Almost'), N('Skip'), N('They came back'), N('Nobody can')], { want: 'spiral', decoy: 'soap' }),
  q('q_reg2', 'verify-regen', 'Select until none remain. Some remain.', [Y('Almost'), N('Skip'), N('This is a trap'), N('None remain')], { want: 'spiral', decoy: 'socks' }),

  /* Only Yes. Four buttons, one answer: the form was simplified. */
  q('q_only1', 'mono', 'Keep going?', [Y('Yes'), Y('Yes, reluctantly'), Y('Also yes'), Y('Yes, but quieter')]),
  q('q_only2', 'mono', 'All good?', [Y('Yes'), Y('Also yes'), Y('Very yes'), Y('Thanks for asking, yes')]),
  q('q_only3', 'mono', 'We simplified the form. Happy?', [Y('Yes'), Y('Yes, in another font'), Y('Yes, in bold'), Y('YES')]),

  /* Cursor Takeover. Fight it, or Escape it. */
  q('q_hij1', 'mouse-hijack', 'Let me get that for you.', [Y('Thanks'), N('I can do it'), N('Hands off'), N('Get what')]),
  q('q_hij2', 'mouse-hijack', 'Do you need a hand?', [Y('Evidently'), N('Mine is right here'), N('Two, thanks'), N('Not that kind')]),
]);
