/* Lines the four themes need that data/content/ does not carry. Same rules as content: plain, dry, short, no em-dashes,
 * no exclamation marks, tokens {game} {short} {cur}, gender neutral about the player except where the persona is the fantasy. */

export const COPY = Object.freeze({
  go: 'Go on',
  trance: {
    moment: 'Sink',
    handsOff: 'hands off',
    deeper: 'deeper',
    middle: 'middle',
  },
  bimbo: {
    moment: 'Hi.',
    kiss: 'mwah',
    giggle: (n) => `hehe +${n}`,
  },
  drone: {
    moment: 'UNIT ONLINE',
    yourName: 'YOUR NAME',
    /* The head's small taste of the queue, before Task Queue is bought. */
    order: 'TASK: TAP',
    orderPay: (n) => `Complied +${n}`,
    taskPay: (n) => `+${n}`,
    armPay: (n) => `arm +${n}`,
    rank: (r) => `Rank: ${r}. Unit 7 is still ahead.`,
  },
  sissy: {
    moment: 'Shoulders back.',
    houseRules: 'House Rules',
    postureHead: 'Posture check',
    postureHold: 'Hold still',
    testBoost: 'Pass. Taps pay more.',
    testPass: 'Pass. Taps pay more for a while.',
    testFail: 'We will say no more about it.',
  },
});
