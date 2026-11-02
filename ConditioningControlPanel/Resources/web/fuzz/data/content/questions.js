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
 * answer.tone: 'yes' is what the game wants, 'no' the refusal the trick makes
 * harder, 'odd' a sideways answer that counts as yes.
 * Optional extras: phrase (wobbly text), want + decoy (picture puzzles),
 * lean + low + high (the slider).
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

const Y = (label) => ({ label, tone: 'yes' });
const N = (label) => ({ label, tone: 'no' });
const O = (label) => ({ label, tone: 'odd' });
const q = (id, trick, text, answers, extra) => ({ id, text, answers, trick, ...(extra || {}) });

export const QUESTIONS = Object.freeze([
  /* Plain questions. No trick, which at this point is the trick. */
  q('q_calm', 'none', 'Feeling calm?', [Y('Yes'), N('No')]),
  q('q_going', 'none', 'Keep going?', [Y('Yes'), N('No')]),
  q('q_breath', 'none', 'When did you last breathe on purpose?', [Y('Just now'), O('Is that optional')]),
  q('q_thing', 'none', 'Is this what you meant to do today?', [O('It is now'), N('No')]),
  q('q_read', 'none', 'Did you read this question?', [Y('Yes'), O('Partly')]),
  q('q_hobby', 'none', 'Would you call this a hobby?', [Y('Yes'), N('I would not call it')]),
  q('q_doing', 'none', 'How is the tapping?', [Y('Going well'), O('It is going')]),
  q('q_stop', 'none', 'Could you stop right now?', [Y('Easily'), O('Soon')]),
  q('q_tell', 'none', 'Would you tell anyone about this?', [N('No'), O('Which part')]),
  q('q_number', 'none', 'Is the number big enough yet?', [N('No'), O('Bigger')]),

  /* Melting Answers */
  q('q_melt1', 'melt-away', 'Do you need that thought?', [N('I do'), Y('Not really')]),
  q('q_melt2', 'melt-away', 'Would you like to stay a bit longer?', [Y('Yes'), N('No')]),
  q('q_melt3', 'melt-away', 'Are you in charge here?', [N('I am'), Y('Who is asking')]),

  /* Swapping Answers */
  q('q_swap1', 'hover-swap', 'One of these is yes. Pick it.', [Y('Yes'), N('No')]),
  q('q_swap2', 'hover-swap', 'Are you sure of your choices today?', [N('Certain'), Y('Less so now')]),
  q('q_swap3', 'hover-swap', 'Left or right?', [Y('This one'), N('That one')]),

  /* Fleeing Answer */
  q('q_flee1', 'flee', 'Do you want to leave?', [Y('I want to stay'), N('Leave')]),
  q('q_flee2', 'flee', 'Say no to the nice button?', [Y('Never'), N('Catch me first')]),
  q('q_flee3', 'flee', 'Is it hard to refuse?', [Y('A bit'), N('Not at all')]),

  /* Are You Sure */
  q('q_nag1', 'nested-nag', 'Would you like another spiral?', [Y('Yes'), N('No')]),
  q('q_nag2', 'nested-nag', 'Stop thinking about the thing?', [Y('Fine'), N('No')]),
  q('q_nag3', 'nested-nag', 'Agree with the button?', [Y('Agree'), N('Disagree')]),

  /* Decoy Answers */
  q('q_crowd1', 'crowd', 'Which of these is the real no?', [Y('It does not matter'), N('This no')]),
  q('q_crowd2', 'crowd', 'Do you know what you want?', [Y('Yes'), N('No')]),
  q('q_crowd3', 'crowd', 'Do you trust this form?', [Y('Yes'), N('No')]),

  /* Bubble Answers */
  q('q_bub1', 'bubble-pop', 'Pop the one you mean.', [Y('Yes'), N('No')]),
  q('q_bub2', 'bubble-pop', 'What is floating away?', [Y('Everything'), N('Nothing')]),

  /* Leaning Slider */
  q('q_sl1', 'lean-slider', 'How calm, 1 to 10?', [N('1'), Y('10')], { lean: 'high', low: '1', high: '10' }),
  q('q_sl2', 'lean-slider', 'How much do you still remember?', [N('Most of it'), Y('Very little')], { lean: 'high', low: 'Most of it', high: 'Very little' }),
  q('q_sl3', 'lean-slider', 'Rate your own free will.', [N('Mine'), Y('Pending')], { lean: 'high', low: 'Mine', high: 'Pending' }),

  /* Shrinking Answer */
  q('q_shr1', 'size-skew', 'Is it all right to enjoy this?', [Y('Yes'), N('No')]),
  q('q_shr2', 'size-skew', 'Bigger number or smaller doubts?', [Y('Both'), N('Neither')]),
  q('q_shr3', 'size-skew', 'No pressure. Keep going?', [Y('Yes'), N('No')]),

  /* Fading No */
  q('q_fade1', 'opacity-skew', 'Was there another option?', [Y('I do not see one'), N('There was')]),
  q('q_fade2', 'opacity-skew', 'Would you rather stop?', [Y('Not now'), N('I would')]),

  /* Magnet Answer */
  q('q_mag1', 'magnet', 'Which one likes you?', [Y('This one'), N('Not that one')]),
  q('q_mag2', 'magnet', 'Do you always go where you are pulled?', [Y('It seems so'), N('No')]),
  q('q_mag3', 'magnet', 'Going somewhere?', [Y('Over there'), N('Elsewhere')]),

  /* Late No */
  q('q_late1', 'late-bloom', 'Would you like to think about it?', [Y('No need'), N('I did, and no')]),
  q('q_late2', 'late-bloom', 'Stay?', [Y('Stay'), N('Finally, no')]),

  /* Hold to Refuse. The widget labels the hold itself. */
  q('q_hold1', 'hold-refuse', 'Are you sure you have the strength?', [Y('Yes'), N('No')]),
  q('q_hold2', 'hold-refuse', 'Would you rather think for yourself?', [Y('Later'), N('I would')]),
  q('q_hold3', 'hold-refuse', 'One more minute?', [Y('Sure'), N('No')]),

  /* Dig for No */
  q('q_drag1', 'drag-reveal', 'The refusal is under here somewhere.', [Y('Never mind'), N('Found it, no')]),
  q('q_drag2', 'drag-reveal', 'Clear your head before you answer.', [Y('It is clear'), N('Still in there, no')]),

  /* Bottomless No */
  q('q_stack1', 'bottomless-no', 'Keep going? Six more of these.', [Y('Yes'), N('No')]),
  q('q_stack2', 'bottomless-no', 'How many times can you say it?', [Y('Enough'), N('No')]),

  /* Fake Robot Check. Plainly a joke, and nothing to fill in. */
  q('q_robot1', 'verify-checkbox', 'I am not a robot. Hold. Calmly.', [Y('Not a robot'), O('Not sure now')]),
  q('q_robot2', 'verify-checkbox', 'Confirm you are still a person. Hold.', [Y('Person'), O('Roughly')]),

  /* Picture Puzzle */
  q('q_grid1', 'verify-grid', 'Select all spirals.', [Y('Done'), N('Skip')], { want: 'spiral', decoy: 'socks' }),
  q('q_grid2', 'verify-grid', 'Select all squares that apply to you.', [Y('All of them'), N('Skip')], { want: 'spiral', decoy: 'hat' }),

  /* Wobbly Text: copy the rude thing out. */
  q('q_tt1', 'verify-transcribe', 'Copy the text. Case sensitive.', [Y('Done'), N('Give up')], { phrase: 'YOU SMELL' }),
  q('q_tt2', 'verify-transcribe', 'Copy the text. We know it is hard.', [Y('Done'), N('Give up')], { phrase: 'STILL HERE THEN' }),
  q('q_tt3', 'verify-transcribe', 'Copy the text. Take your time.', [Y('Done'), N('Give up')], { phrase: 'THIS IS YOUR EVENING' }),

  /* Endless Grid */
  q('q_reg1', 'verify-regen', 'Clear them all.', [Y('Almost'), N('Skip')], { want: 'spiral', decoy: 'soap' }),
  q('q_reg2', 'verify-regen', 'Select until none remain. Some remain.', [Y('Almost'), N('Skip')], { want: 'spiral', decoy: 'socks' }),

  /* Only Yes. Two buttons, one answer: the form was simplified. */
  q('q_only1', 'mono', 'Keep going?', [Y('Yes'), Y('Yes, reluctantly')]),
  q('q_only2', 'mono', 'All good?', [Y('Yes'), Y('Also yes')]),
  q('q_only3', 'mono', 'We simplified the form. Happy?', [Y('Yes'), Y('Yes, in a different font')]),

  /* Cursor Takeover. Fight it, or Escape it. */
  q('q_hij1', 'mouse-hijack', 'Let me get that for you.', [Y('Thanks'), N('I can do it')]),
  q('q_hij2', 'mouse-hijack', 'Do you need a hand?', [Y('Evidently'), N('Mine is right here')]),
]);
