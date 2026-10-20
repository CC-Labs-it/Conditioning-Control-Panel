/* ============================================================================
 * annex/docs-rm202.js - the Room 202 protocol paper, on its own so the store
 * build can swap it.
 *
 * docs.js keeps every other paper. This one names the room's real machinery,
 * and the phone's Google Play and App Store builds may not carry that wording,
 * so it lives here with a docs-rm202.store.js sibling the vendoring script
 * copies over it in the store tree (core/storesafe.js). The desktop and every
 * full tree read this file exactly as the paper always read.
 *
 * docs.js's WRITING LAW applies here word for word.
 * ==========================================================================*/

export const RM202 = Object.freeze({
  name: 'RM202.TXT',
  head: 'PROTOCOL, ROOM 202. Own triggers.\nFiled term two, week 3.',
  body:
'**Evaluative conditioning** transfers feeling between paired items (De Houwer, Thomas and ' +
'Baeyens, 2001), and **mere exposure** does the rest (Zajonc, 1968). The pads in this room ' +
'therefore wear the subject\'s **own trigger phrases**, and one note in four carries the ' +
'subject\'s **own recording** under it, low enough to sit **beneath attention**.\n\n' +
'Cohort two\'s average session length rose **eleven minutes** after the pads began carrying ' +
'their own material. **No subject has mentioned it.** Tuning is on Thursdays.',
  tldr: Object.freeze(['evaluative conditioning', 'own trigger phrases', 'own recording',
    'beneath attention', 'eleven minutes', 'no subject has mentioned it']),
  attachments: Object.freeze([
    Object.freeze({
      kind: 'chart',
      chart: Object.freeze({
        type: 'line',
        x: Object.freeze([1, 2, 3, 4, 5, 6, 7, 8]),
        y: Object.freeze([18, 19, 19, 30, 30, 31, 31, 32]),
        mark: 3,
        markLabel: 'wk 4',
      }),
      caption: 'cohort two, minutes a night. the step is week 4, when the pads went on their ' +
        'own material. archive figure.',
    }),
    Object.freeze({
      kind: 'audio', sfx: 'pad', ms: 1600,
      caption: 'one note from the room. the one under it did not survive the copy.',
    }),
  ]),
});
