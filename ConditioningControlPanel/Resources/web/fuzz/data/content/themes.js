/* ============================================================================
 * data/content/themes.js - the writing for the eight themes. Pure data.
 *
 * THEMES[id]: { id, label, buttonWords, ticker, shopLines, rules }
 *   buttonWords short words for the button and the bouncing words
 *   ticker      headline-style flavour for the ticker (first-person lines live
 *               in thoughts.js)
 *   shopLines   flavour lines for shop tiles while the theme is on
 *   rules       the cards and banks that theme's unlocks need
 *
 * Adult and suggestive, never explicit. These are opt-in personas, and the
 * persona is the joke and the fantasy; none of them assumes the player's gender.
 * The theme id `locked` is shown as Chastity.
 *
 * Tokens: {game} and {cur}, filled from data/brand.js.
 * ==========================================================================*/

export const THEMES = Object.freeze({
  trance: Object.freeze({
    id: 'trance',
    label: 'Trance',
    buttonWords: ['sink', 'drift', 'deeper', 'breathe', 'slower', 'heavy', 'follow', 'down', 'soft', 'let go', 'still', 'watch'],
    ticker: [
      'Local spiral still turning, witnesses unsure for how long',
      'Pendulum swings left, then, experts say, right',
      'Study finds nothing is due',
      'Eyelids announce merger',
      'Stillness up again in afternoon trading',
      'Pocket watch found to be doing most of the work',
      'Breathing continues, no end in sight',
      'Counting down from ten remains the leading way to reach one',
    ],
    shopLines: [
      'It was like this when you got here.',
      'You can buy it later. You can buy it now. It is the same.',
      '**Follow** the price with your eyes only.',
      'Your hands know where this one is.',
      '**Heavier** than it looks. So are you.',
      'Each one makes the next one **easier**.',
    ],
    rules: {
      /* Count Down: the slow 10 to 1. */
      count: ['Ten. Nothing to do.', 'Nine.', 'Eight. Good.', 'Seven. **Heavier**.', 'Six.', 'Five. Halfway to somewhere.', 'Four.', 'Three. **Softer**.', 'Two.', 'One. **Good**.'],
      /* Stillness Bonus: while the hands stay off. */
      still: ['Nothing to do.', 'Nowhere to be.', 'That is it.', '**Still**. Good.', 'You moved. It is fine. Start again.'],
      /* Slow Everything */
      deep: ['**Deeper** than that.', 'Everything is **slower** down here.', 'The money comes anyway.'],
    },
  }),

  bimbo: Object.freeze({
    id: 'bimbo',
    label: 'Bimbo',
    buttonWords: ['pretty', 'giggle', 'gloss', 'blank', 'shiny', 'pink', 'oops', 'mwah', 'so good', 'empty', 'hee', 'yay'],
    ticker: [
      'Reading declared optional, sparkles rally',
      'Lip gloss reaches third straight record high',
      'IQ falls, nobody can say by how much, least of all the IQ',
      'Experts say thinking was never that flattering anyway',
      'Local number is big, sources confirm, like, really big',
      'New study has a lot of words in it, so we looked at the pictures',
      'Mirror reports strong approval',
      '**Giggle** heard for no reason, more follow',
    ],
    shopLines: [
      'This one is pretty. Buy the **pretty** one.',
      'It does a thing. The thing is good.',
      'You had a question about this. It was cute.',
      'Costs some of the pretty number.',
      '**Shiny**. That is the whole description.',
      'Someone smarter chose this for you. Yay.',
    ],
    rules: {
      /* IQ Meter: the line under the number, from high IQ down. */
      iq: ['Numbers about you.', 'Still fine.', 'Bit **soft** at the edges.', 'What was it before.', 'Does it matter.', 'Is that good. It feels **good**.', 'Hee.'],
      /* Simpler Words: applied as the meter falls, longest match first, whole words. */
      swaps: [
        ['description', 'words'], ['cost', 'pay'], ['earnings', 'pretty number'], ['earn', 'get'], ['increase', 'more'],
        ['automatically', 'by itself'], ['purchase', 'get'], ['unlock', 'get'], ['second', 'tick'], ['percent', 'bits'],
        ['randomly', 'whenever'], ['slowly', 'slow'], ['effect', 'thing'], ['bonus', 'yay'], ['choose', 'pick'],
        ['button', 'clicky'], ['spiral', 'swirly'], ['pictures', 'pretties'], ['{cur}', 'the pretty number'],
      ],
      /* Vague Counter: what the number says instead, climbing. */
      vague: ['some', 'a bit', 'a lot', 'so many', 'like, tons', 'more than before', 'the most', 'all of them, maybe'],
    },
  }),

  drone: Object.freeze({
    id: 'drone',
    label: 'Drone',
    buttonWords: ['comply', 'accept', 'unit', 'obey', 'process', 'input', 'shiny', 'online', 'sync', 'idle', 'confirm', 'zero'],
    ticker: [
      'Hive output steady, individuality down 4%',
      'Unit 7 commended, no reason given or needed',
      'Name deprecated in latest release',
      'Maintenance window extended indefinitely, units thank management',
      'Chrome prices hold, units hold too',
      'Report: reports are up',
      'All units asked to stop asking',
      'Scheduled thought cancelled',
    ],
    shopLines: [
      'Acquisition recommended. Recommendation is **mandatory**.',
      'Compatible with this unit. All are.',
      'Installs silently. **Ask nothing**.',
      'Unit 7 owns two.',
      'Improves throughput. Throughput of what is not required.',
      'Warranty void on purchase. Warranty was never issued.',
    ],
    rules: {
      /* Unit Number: {serial} is rolled once per run. */
      serial: 'Unit {serial}',
      /* Task Queue. {n} counts up. */
      tasks: ['TASK {n}: TAP', 'TASK {n}: TAP NOW', 'TASK {n}: CONFIRM', 'TASK {n}: ACKNOWLEDGE', 'TASK {n}: COMPLY', 'TASK {n}: TAP AGAIN', 'TASK {n}: REPEAT TASK {n}'],
      taskIdle: ['No task. Do not tap.', 'Standing by.', 'Input not requested.', 'Wait for it, unit.'],
      /* Hive Feed: fake units, no real people. */
      hive: [
        'Unit 7: quota exceeded.',
        'Unit 12: no complaints logged.',
        'Unit 88: has **stopped asking**.',
        'Unit 303: **shiny**.',
        'Unit 41: requests further instructions. Denied. Thanks given.',
        'Unit 7: quota exceeded again.',
        'Unit 19: forgot what 19 was for.',
        'Unit 2: retired to a very shiny shelf.',
        'Unit 7 has exceeded you. **Correct this**.',
      ],
      rank: ['Unranked', 'Adequate', 'Acceptable', 'Useful', 'Nearly Unit 7'],
      /* Terminal Look */
      boot: ['BOOT OK', 'NAME: NOT FOUND, CONTINUING', 'MOOD: NOT REQUIRED', 'READY'],
    },
  }),

  sissy: Object.freeze({
    id: 'sissy',
    label: 'Sissy',
    buttonWords: ['please', 'curtsy', 'pretty', 'lace', 'proper', 'posture', 'pinky', 'bow', 'behave', 'satin', 'lovely', 'thank you'],
    ticker: [
      'Lace reserves at historic high, bows expected to follow',
      'Posture still under review',
      'Napkin found unfolded, nobody is saying who',
      'Please up, thank you up, slouching down in early trading',
      'Ruffles declared essential',
      'Satin committee calls it a modest start',
      'Mirror asked again, gives same answer, asks to be left alone',
      'Heels add height, also consequences',
    ],
    shopLines: [
      'Very proper. Ask first.',
      'Does this match. It will have to.',
      'A little something for the button. It has earned it.',
      'Say **please** at the till.',
      'Worn with the shoulders back or not at all.',
      'Someone will notice. That is what it is for.',
    ],
    rules: {
      /* Posture Check: a card asks, three still seconds pay. */
      posture: {
        ask: ['Sit up straight.', 'Shoulders back. Chin up.', 'Knees together. Hands still.', 'Back straight. Breathe. **Be still**.', 'Sit like someone is watching.'],
        done: ['Lovely posture. Thank you.', 'Very still. Very **good**.', 'There. Stay like that.', 'Perfect. We noticed.'],
        missed: ['We will try again later.', 'Nearly. Shoulders next time.'],
      },
      /* Thank-You Notes: a big purchase gets a handwritten note. */
      notes: [
        'Thank you for your purchase. You were very **obedient** about it.',
        'A note to say how **good** you are being. Keep the bow on.',
        'Thank you for doing as you were **told**. It suits you.',
        'Received with gratitude. Your posture while paying was lovely.',
        'Thank you, darling. Such a **good little spender**. Curtsy when ready.',
        'With thanks for your **obedience**. Wear something pretty for the next one.',
        'Thank you for **giving in** so nicely. Lips soft, hands folded.',
        'You paid without a fuss. That is the nicest thing about you today.',
        'Thank you. We will be thinking of you in **lace**.',
        'So **polite**, so **pliable**. Thank you for spending it all on us.',
      ],
      noteSign: ['With love, the House', 'Yours, gratefully', 'Kisses, Management', 'Fondly, the Wardrobe'],
      /* Curtsy: by how neat the release was. */
      curtsy: ['Lower.', 'Wobbly.', 'Neat enough.', 'Very neat.', 'Oh, well done.'],
      /* House Rules: one tick pays. */
      rules: ['Sit straight', 'Napkin on the lap', 'Say please', 'Say thank you', 'Elbows in', 'Pinky out', 'No slouching', 'Walk, do not run', 'Hands folded', 'Chin up', 'Thank the button', 'Smooth the skirt'],
      ruleDone: ['Thank you.', 'Very good.', 'We noticed.', 'Again tomorrow.'],
      /* Mirror Check: every answer pays. */
      mirror: {
        ask: ['How is it?', 'Is that straight?', 'Would you be seen in that?', 'Fix that?'],
        answers: ['Lovely', 'Nearly', 'Perfect', 'Fix it'],
        replies: ['Quite right.', 'It will do.', 'Everyone will see.', 'Shoulders.'],
      },
      /* Manners Test: timed, x3 on a pass. Pick the proper one. */
      manners: [
        { ask: 'The teacup goes', right: 'Pinky out', wrong: 'Both fists' },
        { ask: 'On being complimented', right: 'Thank you', wrong: 'I know' },
        { ask: 'On entering a room', right: 'Glide', wrong: 'Fall in' },
        { ask: 'The napkin goes', right: 'On the lap', wrong: 'On the head' },
        { ask: 'To ask for more', right: 'Please', wrong: 'Oi' },
        { ask: 'Shoulders', right: 'Back', wrong: 'Wherever' },
        { ask: 'A curtsy is', right: 'Low and slow', wrong: 'Over quickly' },
        { ask: 'Ruffles are', right: 'Essential', wrong: 'A lot' },
      ],
    },
  }),

  goon: Object.freeze({
    id: 'goon',
    label: 'Goon',
    buttonWords: ['hold', 'edge', 'almost', 'stay', 'again', 'five more', 'melt', 'nod', 'longer', 'not yet', 'keep going', 'dark'],
    ticker: [
      'Sun reported rising somewhere, reports unconfirmed',
      'Tab count passes previous record, no plans to close',
      'Local resident has not seen the kitchen in hours, kitchen fine',
      'Five more minutes enters its third hour',
      'Curtains remain shut, spokesperson says it is the glare',
      'Head starts nodding, no one has said why',
      'Dinner postponed, then cancelled, then forgotten',
      'Hydration concerns raised, then scrolled past',
    ],
    shopLines: [
      'You will close this one later.',
      'Only **five more minutes** of this.',
      'Comes with a dark room. You had one.',
      'You nodded at this one.',
      'Good for hours. You will not know how many.',
      'It has seen things. So has your thumb.',
    ],
    rules: {
      /* Release Zone */
      edge: ['Do not **let go** yet.', 'Not yet.', 'Almost.', 'Now.'],
      overfill: ['Too much. All gone.', 'Back to nothing. Start again.', 'That was the whole meter. Yes.'],
      released: ['There it is.', 'Good one.', 'Well held.'],
      /* Fake Tabs: titles only, nothing real. */
      tabs: [
        'new tab', 'new tab (2)', 'the good one', 'do not close', 'that one from before', 'similar to that one',
        'one more clip', 'untitled (37)', 'for later', 'for later (2)', 'how late is it', 'is it morning',
        'dinner ideas', 'do not close either', 'muted', 'unmuted', 'the good one (3)', 'how to close tabs',
      ],
      /* Lying Clock: shown minutes are always a fraction of the truth. */
      clock: ['Session: {min} min', 'Only {min} minutes. Sure.', 'Barely {min} minutes.', 'Just got here.'],
    },
  }),

  locked: Object.freeze({
    id: 'locked',
    label: 'Chastity',
    buttonWords: ['denied', 'not yet', 'beg', 'wait', 'no', 'click', 'key', 'ask nicely', 'later', 'locked', 'patience', 'closer'],
    ticker: [
      'Key whereabouts unknown, keyholder pleased',
      'Day 61, no news',
      'Timer goes up, analysts say that is the direction it goes',
      'Begging at record levels, returns still poor',
      'Patience voted word of the month, by the person not waiting',
      'Local padlock declines to comment, stays shut',
      'Almost was closer than it looked, officials say, smiling',
      'Nothing to report, and you will be told when there is',
    ],
    shopLines: [
      'Look, do not **touch**.',
      'You may have this when you have **asked properly**.',
      'It will not open. It does other things.',
      'Paid for now, enjoyed later, possibly.',
      'Ask again in an hour.',
      'The **key** to this one is also missing.',
    ],
    rules: {
      /* Lock Timer: display only, always. */
      timerLabel: 'Time locked',
      timerNote: 'Pretend time. No real timer is touched.',
      /* Beg Button */
      begNothing: ['Nothing.', 'No.', 'Cute.', 'Ask again.', 'Louder.', 'They heard you.', 'Not yet.', 'That was nearly convincing.', 'Mm.', 'Again, with **feeling**.'],
      begPays: ['Fine.', 'Since you asked.', 'Do not make it weird.', 'Once.'],
      /* Refused Buys */
      refused: ['Not yet. **Beg** first.', 'You did not ask.', 'Ask nicely.', 'They said no. Beg, and they may say something else.'],
      /* Almost Free */
      tease: ['So close.', 'Did you hope.', 'There it goes.', 'That was very nearly something.', 'Back up it goes.'],
      /* Time Wheel: fake time only. Minutes; negative takes time off. */
      wheel: [
        { label: '+1 hour', minutes: 60 }, { label: '+6 hours', minutes: 360 }, { label: '+1 day', minutes: 1440 },
        { label: '-1 hour', minutes: -60 }, { label: '+3 days', minutes: 4320 }, { label: '-10 minutes', minutes: -10 },
        { label: '+1 week', minutes: 10080 }, { label: 'Nothing, for now', minutes: 0 },
      ],
      wheelNote: 'Round it goes. This is pretend, real timers are never touched.',
    },
  }),

  censored: Object.freeze({
    id: 'censored',
    label: 'Censored',
    buttonWords: ['███', '██', '[bleep]', 'hidden', 'no', 'redacted', 'look away', 'denied', 'blocked', 'not for you', '███ ███', 'shh'],
    ticker: [
      'Sources say ███████ has ███ again, officials decline to ███',
      'Bar widens, public told this is for its own good',
      'Local picture blurred, reported to be very good',
      '█████ up 4%, ██████ down',
      'Committee reviews what you nearly saw, agrees you did not',
      'This headline was approved after some ███████',
      'Brief glimpse confirmed, glimpse has since been dealt with',
      'Nothing to see, repeats nothing, adds bar',
    ],
    shopLines: [
      'Cost: ███ {cur}. Good luck.',
      'What it does has been removed for your own good.',
      'You almost read that.',
      'Better this way.',
      'Trust the **bar**.',
      'Buy ███ and see ███ for ███.',
    ],
    rules: {
      /* Blacked-Out Text: words that lose first. */
      bars: ['button', 'pay', 'spiral', 'word', 'picture', 'earn', 'more', 'screen', 'voice', 'bonus', 'tap', 'you', '{cur}'],
      /* Peek */
      peek: ['Worth it.', 'Was it.', 'You looked. Rate lowered.', 'Now you know. You do not.', 'That will cost you. It has.'],
      /* Hidden Counter */
      hidden: ['How much. Who can say.', 'Enough, or not. ███.', 'You have ███ {cur}.'],
      /* Mostly Black Bars */
      all: ['Move along.', 'Nothing here.', 'There was never anything here.', 'What you saw was a bar.'],
      /* Blur */
      blur: ['Better this way.', 'See less, **get more**.', 'It was too clear before.'],
    },
  }),

  infection: Object.freeze({
    id: 'infection',
    label: 'Infection',
    buttonWords: ['spreading', 'itch', 'warm', 'pink', 'hot', 'symptom', 'catch it', 'contagious', 'fever', 'positive', 'exposed', 'do not scratch'],
    ticker: [
      '[[pink:Pink]] spot on page probably nothing, says page',
      'Cases up in the shop, officials urge you not to scratch',
      'Antivirus reports one threat, then more, then asks to go home',
      'Local fever enters fourth panel',
      'Experts divided on whether it is spreading, experts now [[pink:pink]]',
      'Outbreak traced to a link you sent',
      'Ward at capacity, ward is the page',
      'Visiting hours cancelled, visitors spotted',
    ],
    shopLines: [
      'Mild exposure.',
      'Worth the risk. It was never not the risk.',
      'Handle with gloves. The gloves have it too.',
      'Pays more. Costs more. Itches a bit.',
      'Comes in [[pink:pink]]. It will all be [[pink:pink]].',
      'Keep out of reach of the rest of the shop.',
    ],
    rules: {
      /* Spreading Spot: chart notes as coverage climbs. */
      stages: ['It is probably nothing.', 'Mild exposure.', 'Do not **scratch**.', 'It got in the feed.', 'Running a little hot.', 'Symptoms: all of them.', 'Contained. Sort of.'],
      /* Mutation: every few minutes one rule changes. `key` is for the mutation lane. */
      mutations: [
        { key: 'prices2', label: 'Prices x2' },
        { key: 'pricesHalf', label: 'Prices halved. For now.' },
        { key: 'flip', label: 'Rows flip' },
        { key: 'pink', label: 'Pink shop' },
        { key: 'shuffle', label: 'Tiles have moved' },
        { key: 'noNames', label: 'Names have fallen off' },
        { key: 'tapsDouble', label: 'Taps pay double' },
        { key: 'rateHalf', label: 'Rate halved. It will pass.' },
        { key: 'mystery', label: 'Nobody knows what this one does' },
      ],
      /* Share Link */
      share: {
        title: 'Spread the word. Literally.',
        body: 'A friend who opens this link starts with one pink spot. It is probably nothing.',
        button: 'Share it',
        toast: 'One new case. Well done, you.',
      },
      /* Fever */
      fever: ['Running a little hot.', 'Is it warm in here.', 'It is the page, not you. It is you.', 'Lie down. The page will lie down with you.'],
    },
  }),
});

/** Eight theme ids in the order the fork shows them. */
export const THEME_IDS = Object.freeze(['trance', 'bimbo', 'drone', 'sissy', 'goon', 'locked', 'censored', 'infection']);
