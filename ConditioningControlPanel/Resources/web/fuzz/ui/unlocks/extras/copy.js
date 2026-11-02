/* ui/unlocks/extras/copy.js - the few lines the extras lane needs that data/content does not have. Same rules: plain,
 * dry, no em-dashes, no exclamation marks, tokens {game} and {cur}. */

export const QUIZ_COPY = Object.freeze({
  title: 'Quick question',
  giveUp: ['Never mind.', 'It can wait.', 'We will ask again.'],
  melted: 'That one was not an option.',
  fake: 'fake',
  stillThere: 'still there',
  nag: ['Are you sure?', 'Really?', 'Really really?'],
  more: '{n} more',
  lateIn: 'No in {n} s',
  oneTap: '1 tap',
  holdFor: 'hold {n} s',
  phew: 'phew',
  whereIsNo: 'Where is No?',
  stickies: ['not here', 'also not here', 'keep looking', 'warmer'],
  robotHold: 'Hold. Calmly.',
  robotTwitch: 'Less twitchy.',
  robotDone: 'Calm enough.',
  gridMissed: 'You missed one. Probably.',
  regenAll: 'Nobody does that.',
  regenCount: 'Cleared {n}',
  wrongLetter: 'Case sensitive.',
  hijackDone: 'Got that for you',
  hijackLost: 'Fine. Do it yourself.',
  sliderDone: 'That is my answer',
  alsoYes: ['Also yes', 'Yes, reluctantly', 'Yes, in a different font', 'Yes, but quieter'],
});

export const EMI_COPY = Object.freeze({
  ignored: 'Ignored {n}',
  hatButton: 'Hat',
  game: 'Side game',
  nap: 'Tap to wake',
});

export const COMBO_COPY = Object.freeze({
  letMeOut: 'Let me out?',
  streak: 'Streak {n}',
  taskDone: 'Pays {n}',
  gold: 'House rules',
  episodeEnd: 'Episode one',
  cohostA: 'Host',
  cohostB: 'Cohost',
  inductDone: 'That was the induction. You may keep tapping.',
  sponsored: 'Sponsored',
  post: 'post',
});

export const MOOD_COPY = Object.freeze({
  soundOn: 'Sound is on. Rest still pays more.',
  soundOff: 'No sound yet.',
});

export const AFTER_COPY = Object.freeze({
  tabs: { map: 'Map', ends: 'Endings', pct: 'Choices', share: 'Share', perk: 'Keep', daily: 'Daily', shuffle: 'Shop', save: 'Save' },
  pctNote: 'From your own runs. Nobody else is counted yet.',
  pctNone: 'One run so far. It is a very small survey.',
  dailyMatch: 'You matched {n} of 5 today.',
  dailyNote: 'Pick these on your next run. Nobody is checking except this board.',
  dailyEmpty: 'No runs today yet.',
  perkNone: 'No Looks bought yet. Buy one next run and it can come back.',
  perkKept: 'Kept. It will be there next run.',
  perkDrop: 'Keep nothing',
  shuffleNote: 'Every run after the first, the side unlocks come in a different order.',
  closeTab: 'Close this',
  closeTabHard: 'This tab will not close itself. Politeness has limits.',
  bye: 'Bye.',
  theEnd: 'The End',
});
