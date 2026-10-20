/* ============================================================================
 * annex/docs-rm202.store.js - the Room 202 paper for the phone's STORE TREE.
 *
 * The vendoring script copies this over docs-rm202.js in the Google Play and
 * App Store builds (core/storesafe.js). Same id, same chart, same sound, same
 * beat: the pads wear the subject's own material and nobody notices. What
 * changes is the machinery the paper names. The store wording rule keeps
 * "conditioning" and trigger phrases off every screen, and the store tree has
 * no clips under the pad notes (store-build.json drops assets/sublim/), so the
 * paper says what that build's room actually does.
 *
 * The citations stay real. De Houwer, Thomas and Baeyens (2001) is titled
 * "Associative learning of likes and dislikes", which is the plain-words name
 * this paper uses. Every bolded phrase is in tldr, lowercased, as in docs.js.
 * ==========================================================================*/

export const RM202 = Object.freeze({
  name: 'RM202.TXT',
  head: 'PROTOCOL, ROOM 202. Own words.\nFiled term two, week 3.',
  body:
'**Associative learning** carries a liking from one item to the item it is paired with (De ' +
'Houwer, Thomas and Baeyens, 2001), and **mere exposure** does the rest (Zajonc, 1968). The pads ' +
'in this room therefore wear the subject\'s **own favourite words**, and one note in four is paired ' +
'with a **familiar chime**, quiet enough to sit **beneath attention**.\n\n' +
'Cohort two\'s average session length rose **eleven minutes** after the pads began carrying ' +
'their own material. **No subject has mentioned it.** Tuning is on Thursdays.',
  tldr: Object.freeze(['associative learning', 'own favourite words', 'familiar chime',
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
      caption: 'one note from the room, chime and all. archive copy.',
    }),
  ]),
});
