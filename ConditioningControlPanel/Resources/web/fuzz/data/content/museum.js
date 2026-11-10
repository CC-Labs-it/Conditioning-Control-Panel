/* data/content/museum.js - copy for the Gooning Museum fate cards (handoff-1110/TC-FATE-CARDS-BRIEF.md): the ticket stub,
 * the brass placards and the gift shop sign. Dry catalogue voice, short, plain words, no exclamation marks, no dashes.
 * Placard templates fill {taps} {fuzz} {buys} {minutes} {cur} {name}; numbers arrive already formatted (ctx.fmt). A line
 * with a field the run cannot fill is skipped. Lines about objects use {name}, the object's own title. */

export const TICKET = Object.freeze({
  admit: 'ADMIT ONE',
  house: 'MUSEUM',
  serial: 'No.',
  keep: 'Keep this stub',
  rule: 'Valid for one visit',
});

export const PLACARDS = Object.freeze({
  button: [
    'Button. Pressed {taps} times.',
    'Button. Original. Still in use.',
    'Button. Please do not touch. {taps} visitors did.',
    'Button. Worn smooth by hand.',
  ],
  counter: [
    'Counter. Still counting.',
    'Counter. Running for {minutes} minutes.',
    'Counter. Do not tap the glass.',
    'Counter. Wound by hand, {buys} times.',
  ],
  object: [
    '{name}. Donated by the visitor.',
    '{name}. On loan from the shop.',
    '{name}. Acquired this evening.',
    '{name}. Restored, mostly.',
    '{name}. Do not feed.',
    'Unknown object. Donated by the visitor.',
  ],
});

export const GIFT = Object.freeze({ sign: 'GIFT SHOP', label: 'Gift Shop' });
