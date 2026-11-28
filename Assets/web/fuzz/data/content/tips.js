/* data/content/tips.js - the first-run tips (owner 2026-11-08: plain, short, exact). Read by ui/tips.js only, so it
 * stays out of content/index.js the way terms.js and clauses.js do. Strings pass through brandText. */

export const TIP_COPY = Object.freeze({
  move: 'Drag the edge to move it.',
  moveTouch: 'Hold the edge, then drag.',
  resize: 'Pull the corner to resize it.',
  sound: 'Tap to mute. The arrow sets each sound.',
  choices: 'Pick one. The other closes for this run.',
  active: 'Active play earns more than idle.',
  map: 'The map shows what comes next.',
  wake: 'Wake Up pauses everything. Esc too.',
  keep: 'Trying to move this? Keep pressed.',
});

/* The Guide header button that plays them all again. */
export const TIP_REPLAY = Object.freeze({ label: 'Tips', aria: 'Show the tips again' });
