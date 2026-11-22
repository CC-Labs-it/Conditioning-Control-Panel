/* ui/nudges.js - the small edition's quiet doors to the full game (lane S7a, CONTRACT.md 6.2): one on the ending card,
 * one line on the pause card. Each sends sendNudge(where) from the player's own press, never opens by itself, never
 * covers the button, and shows once per card. Whether a page shows them is the caller's question to ui/edition.js
 * (isSmall); nothing here reads the edition.
 *
 * locked-tile: the small edition shows nothing locked in the game today (cut rows are never offered), so its only door
 * is the seal notice's "See plans" (main.js). Proposal in the lane S note, Run 10.
 *
 * COPY: every string is a PLACEHOLDER until the owner words it (the survey list is pinned in
 * tests/editions-placeholders.test.mjs). */

import { sendNudge } from './edition.js';

export const NUDGE_COPY = Object.freeze({
  endingButton: '[PLACEHOLDER S7-ending-button] The full game has more endings',
  pauseLine: '[PLACEHOLDER S7-pause-line] This is the small edition.',
  pauseButton: '[PLACEHOLDER S7-pause-button] See the full game',
});

/** A real press: the browser's own transient activation, which a script-made click does not carry. */
export const realPress = (win) => () => !!win?.navigator?.userActivation?.isActive;

/** The ending card's door: a hook for ui/recap/recap.js (hooks.nudge). Answers whether a nudge went. */
export const endingNudge = ({ win = globalThis.window, send = () => {}, ed } = {}) => () => sendNudge('ending', { inPress: realPress(win), send, win, ed });

/** One quiet line and its button on the pause card (index.html #pause), once. Answers the line, or null. */
export function mountPauseNudge({ doc = document, win = doc.defaultView, send = () => {}, ed } = {}) {
  const card = doc.querySelector?.('#pause .card');
  if (!card || doc.getElementById('pause-nudge')) return null;
  const line = doc.createElement('p');
  line.id = 'pause-nudge';
  line.className = 'card-sub';
  line.textContent = NUDGE_COPY.pauseLine + ' ';
  const go = doc.createElement('button');
  go.type = 'button';
  go.textContent = NUDGE_COPY.pauseButton;
  go.addEventListener('click', () => sendNudge('pause', { inPress: realPress(win), send, win, ed }));
  line.appendChild(go);
  card.appendChild(line);
  return line;
}
