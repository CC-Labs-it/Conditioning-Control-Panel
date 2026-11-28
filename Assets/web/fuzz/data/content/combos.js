/* ============================================================================
 * data/content/combos.js - Combos, Run Moods, and After the Run. Pure data.
 *
 * x_story, x_audio and x_induct have no recordings yet: these lines show as
 * captions and stay silent (no synthetic speech, ever). They double as the
 * script for whoever records them later.
 * Tokens: {game}, {cur}; the unlock fills {n}, {route}, {ending}, {min}, {theme}.
 * ==========================================================================*/

export const COMBO_LINES = Object.freeze({
  /* Soft Focus: Watch + Rest */
  x_dream: ['Nothing has edges.', 'Is that the button or a memory of one.', 'It is all going to be fine, and also a bit blurry.', '**Slower**. **Softer**. Everything is a cushion now.'],
  /* Watching Words: Watch + Tap */
  x_front: ['Eyes up here.', 'I saw that.', 'Looking at the word. The word is looking back.', 'You blinked first.', 'Keep your hands where the word can see them.'],
  /* Counting Voice: Listen + Tap. Captions only until recorded. */
  x_drill: ['ONE', 'TWO', 'THREE', 'FOUR', 'FIVE', 'AGAIN'],
  /* Gallery Rope: Slideshow fewer ads */
  x_gallery: ['Please do not touch the art.', 'The gift shop is also you.', 'Members may stand slightly closer.', 'The gallery closes when you do.', 'Do not lean on the frame. Lean near it.'],
  /* Infinite Ads: Swipe Feed + More Ads */
  x_infinite: ['Sponsored', 'Sponsored', 'Sponsored, sorry', 'Suggested for you, sponsored', 'Buy now', 'A word from this post'],
  /* Nothing at All: No Pictures + Censored */
  x_nothing: ['Peak.', 'An empty frame with a bar over it. You paid for this.', 'They have censored the nothing. Good. It was getting out of hand.', 'Whatever was under there, there was not.'],
  /* Refusal Bonus: Tap + Chastity */
  x_later: ['Ask again in an hour.', 'Refused, at twice the usual rate.', 'No pays better than yes. You will **come to like** that.', 'Thank you for asking. No.'],
  /* Podcast Cohost: Listen + Bimbo. Pairs; first voice, then second voice. */
  x_listener: [['So true.', 'What did she say.'], ['Exactly.', 'Exactly what.'], ['Great point.', 'Was it a point.'], ['Say more.', 'I forgot it.'], ['Wow.', 'I know, wow.']],
  /* Task Bonus: Tap + Drone */
  x_compl: ['Rating: A. Unit does not care.', 'Task: tap 3 times.', 'Pays more. Unit was not told why.', 'Well done, unit. That was a noise.'],
  /* Endless Streak: Swipe Feed + Goon */
  x_fourhr: ['You blinked at some point.', 'The streak does not drop. Nor do you, apparently.', 'Still counting. It has stopped telling you what.', 'This streak is older than your dinner.'],
  /* Gold Rules: Fewer Ads + Sissy */
  x_manners: ['We do not run here.', 'Elbows off the {cur}.', 'Please. Thank you. On [[gold:gold]] paper.', 'Gloves at all times in the shop.', 'The [[gold:gold]] is for the rules. The rules are for you.'],
  /* Viral Ads: More Ads + Infection */
  x_viral: ['Share this', 'Buy now', 'You next', 'Tell a pal', 'Click', 'You did not see this from us.'],
});

/* x_story Sleep Story: Listen + Rest. Once upon a time, you stopped. In order, looping. */
export const SLEEP_STORY = Object.freeze([
  'Once upon a time, you stopped.',
  'There was a small house at the end of a long afternoon.',
  'Nobody in it had anywhere to be.',
  'There was a kettle, but it had given up.',
  'There was a bed, and it was already warm.',
  'Someone had left the lamp low, and that was kind of them.',
  'You took off your shoes. You did not remember putting them on.',
  'Outside it started to rain, on someone else.',
  'You sat down, and sitting down went very well.',
  'You lay down, and that went even better.',
  'All the things you had to do came to the door, and knocked, and left.',
  'The house got quieter. The quiet got **softer**.',
  'Your hands went first. They had worked so hard.',
  'Then your shoulders. Nobody had told them they could.',
  'And you, in the middle, a long way **down**, doing nothing, doing it so well.',
  'And then the story went on for a while without you.',
  'It was not much of a story, and you were not there for the end.',
  'That was the end.',
]);

/* x_audio Audio Drama: Listen + No Pictures. There is no episode two. */
export const AUDIO_DRAMA = Object.freeze({
  title: 'Episode one',
  lines: [
    'Previously: nothing. This is episode one.',
    'A room. Dark. You can tell it is a nice one by the sound.',
    'A door opens. Nobody comes in. You feel better about that than you should.',
    'Somebody is breathing. You check. It is you.',
    'A voice says: did you bring it. You did not. You nod anyway.',
    'A voice says: **good**. Then nothing, for a long while.',
    'Something is placed on the table. It sounds expensive.',
    'You reach out for it. You cannot find your arm. It is fine, it is resting.',
    'The voice comes closer and says your name. Then a different name. You **answer to both**.',
    'A click.',
    'Next time, on this programme: there is no next time.',
  ],
  end: 'Episode one. There is no episode two.',
});

/* x_induct Full Induction: Listen + Trance. About five minutes, one line each 10 s. It always brings the player back up. */
export const INDUCTION = Object.freeze([
  'Let your eyes close. Keep tapping.',
  'Or keep them open. Nobody is checking. Some of us are.',
  'Take a breath in, slowly, all the way.',
  'And let it go, as if it was somebody else who needed it.',
  'Another one. This one is all yours.',
  'Let your shoulders **drop**. They have been holding up something that is not there.',
  'Let your jaw go. Your tongue can sit wherever it likes.',
  'Your hands can go on tapping without you. They know the way.',
  'Notice how **heavy** your arms are getting. Or do not. They will get there.',
  'There is nothing you need to work out here.',
  'If a thought comes, let it finish without you.',
  'In a moment I will count down from five.',
  'Each number takes you a little further from the day you were having.',
  'Five. **Heavier**, and it is no trouble at all.',
  'Four. **Softer**. The room is getting further off.',
  'Three. Everything that needed doing can wait.',
  'Two. Nearly nothing. Nearly no one.',
  'One. **Good**.',
  'Stay there. There is no hurry to be anywhere else.',
  'It is quiet in here, and it pays well.',
  'Every time you hear a tap, you go a little **deeper**.',
  'Every time you hear a tap, it is you. How convenient.',
  'You are doing this very well. There was never a way to do it badly.',
  'Stay as long as you like. A little longer than that.',
  'Now, whenever you are ready, we will come back up.',
  'One. Feel your hands. They are yours again, more or less.',
  'Two. Your feet, the chair, the room. All still there.',
  'Three. A good, slow breath, and a little stretch if you want one.',
  'Four. Eyes open, if they were not. Nothing is wrong. It is the same game.',
  'Five. Awake, and well, and still tapping. Welcome back.',
]);

/* m_* Run Moods: the first card of a run. Names from copy.json. */
const mood = (id, name, rule, line) => Object.freeze({ id, name, rule, line });
export const MOODS = Object.freeze([
  mood('m_static', 'Busy', 'Bouncing buttons move twice as fast. Words appear half as often.', 'Busy day.'),
  mood('m_pink', 'Pink Day', 'Colour is free. Looks are half price. Everything else costs a bit more.', 'It is one of those days.'),
  mood('m_quiet', 'Quiet', 'No sound until Headphones or Rain. Rest pays more.', 'Shh.'),
  mood('m_rush', 'Rush', 'Everything is cheaper. The shop shows three items at once.', 'Hurry. For no reason.'),
  mood('m_lucky', 'Lucky', 'The golden button comes twice as often. Lucky taps are weaker.', 'Could be worse.'),
  mood('m_broke', 'Broke', 'Ads pay double. Everything else pays a little less.', 'Sponsored by need.'),
  mood('m_watch', 'Watched', 'EMI shows up far too early and talks about everything.', 'Hi. I am early.'),
]);
export const MOOD_CARD = Object.freeze({ title: 'Today you feel like this', button: 'Fine' });

/* z_* After the Run */
export const AFTER_RUN = Object.freeze({
  /* z_map Route Map */
  map: { title: 'You could have gone the other way', unseen: '?', you: 'You' },
  /* z_ends Endings Gallery: {n} of 13 */
  ends: { title: 'Endings: {n} of 13', locked: '?', nudge: ['Twelve more. No pressure.', 'You have seen one way out. There are others.', 'Some of these you would have liked.', 'All of them. Look at you. Go outside. That is one of them.'] },
  /* z_pct Player Percentages */
  pct: { you: 'You', lines: ['Most people went the other way.', 'You are with the majority. Do not get comfortable.', 'Only a few people picked that. They are also fine.'] },
  /* z_share Share Card */
  share: { diagnosis: 'Diagnosis: {route}', ending: 'Ending: {ending}', minutes: '{min} minutes', play: 'Play {game}', button: 'Share', copied: 'Copied' },
  /* z_seen Seen Counter */
  seen: { label: '{n} of {total}', lines: ['Keep going. Or do not.', 'There is more. There is always more. There are {total}.', 'You have seen more of this than anyone needs to.'] },
  /* z_perk Keep One Look */
  perk: { title: 'Keep one Look for next run', line: 'For the road.', button: 'Keep it' },
  /* z_daily Daily Route */
  daily: { title: 'Today everyone is a {theme}', board: 'Minutes today', you: 'You', player: 'Player', start: 'Start this route' },
  /* z_shuffle Shuffled Shop */
  shuffle: { line: 'Not the same twice.', label: 'Run {n}' },
  /* z_save Save String */
  save: { title: 'Your blankness, backed up', copy: 'Copy', paste: 'Paste a save', load: 'Load it', done: 'Run restored', bad: 'That is not a save. That is just some letters.' },
});
