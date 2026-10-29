// Small, public vocabulary for the ten road-word materials. These are ordinary lexical forms,
// not song text or timings. Keep ids stable: the renderer stores them in vertex attributes.
// The first form is the approved display word, preferred when several forms share a phrase.
const FORMS = [
  [],
  ['slide', 'slides', 'sliding', 'slid'],
  ['spark', 'sparks', 'sparked', 'sparking'],
  ['pink'],
  ['sink', 'sinks', 'sinking', 'sank', 'sunk'],
  ['glass', 'glassy'],
  ['rolling', 'roll', 'rolls', 'rolled'],
  ['reset', 'resets', 'resetting'],
  ['empty', 'empties', 'emptied', 'emptying'],
  ['locked', 'lock', 'locks', 'locking'],
  ['burn', 'burns', 'burned', 'burnt', 'burning'],
];
const IDENTITY = new Map(FORMS.flatMap((forms, id) => forms.map(form => [form, id])));

function wordToken(text) {
  if (typeof text !== 'string') return '';
  const match = text.trim().match(/^[\s("'\u2018\u201c\[]*([a-z]+)[\s)"'\u2019\u201d\].,!?;:]*$/i);
  return match ? match[1].toLowerCase() : '';
}

/** Whole words only, allowing ordinary surrounding speech punctuation. Unknown text stays plain. */
export function wordIdentity(text) {
  return IDENTITY.get(wordToken(text)) || 0;
}

/** The same stable id for an approved display spelling; inflections and unknown words return zero. */
export function wordIdentityExact(text) {
  const token = wordToken(text), id = IDENTITY.get(token) || 0;
  return id && FORMS[id][0] === token ? id : 0;
}
