/* ui/unlocks/themes-b/copy.js - the few lines this lane needs that data/content/ does not have. Same rules: plain, dry, short,
 * no em-dashes, no exclamation marks, tokens for the names. */

export const COPY = Object.freeze({
  goon: { notYet: 'Not yet.', later: 'Four hours later' },
  locked: {
    click: 'click', beg: 'Beg', no: 'NO', lookOnly: 'look only', spin: 'Spin', wheel: 'Wheel', soClose: 'So close.', hoped: 'Did you hope.',
    lockRate: () => 'The lock is paying',
  },
  censored: { stamp: 'CENSORED', peek: 'Peek' },
  infection: {
    coverage: (pct, stage) => `Coverage ${pct}%. ${stage}`,
    fever: (t, line) => `${t} deg. ${line}`,
    scratch: 'Do not scratch.',
  },
});
