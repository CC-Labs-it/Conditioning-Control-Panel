/* ui/nudges.js - the small edition's quiet doors to the full game (lane S7a, CONTRACT.md 6.2): one on the ending card,
 * one line on the pause card. Each sends sendNudge(where) from the player's own press, never opens by itself, never
 * covers the button, and shows once per card. Whether a page shows them is the caller's question to ui/edition.js
 * (isSmall); nothing here reads the edition.
 *
 * locked-tile: one greyed tile at the end of the small shop list (ui/shop-locked.js), and the seal notice's "See plans".
 *
 * COPY: APPROVED AND FROZEN (owner 2026-11-22, fourth dialog): character for character, pinned in
 * tests/editions-placeholders.test.mjs. */

import { sendNudge, nudgeLink, editionState } from './edition.js';
import { NUDGE_WHERE, NUDGE_WANT } from '../data/editions.js';
import { openAllowedLink } from './links.js';

export const NUDGE_COPY = Object.freeze({
  endingButton: "More endings in the full game",
  pauseLine: "This is the small edition.",
  pauseButton: "The full game",
});

/** A real press, the ONE rule for every door (lane S7b): the press event when the caller has one (`isTrusted`, which a
 *  script-made click never carries), else the browser's own transient activation. A browser without userActivation
 *  still works through the event, so its buttons are never dead. Answers a function, for sendNudge's inPress. */
export const realPress = (win, ev = null) => () => {
  if (ev && typeof ev.isTrusted === 'boolean') return ev.isTrusted;
  return !!win?.navigator?.userActivation?.isActive;
};

/** The ending card's door: a hook for ui/recap/recap.js (hooks.nudge). Answers whether a nudge went. */
export const endingNudge = ({ win = globalThis.window, send = () => {}, ed } = {}) => Object.assign(
  (ev, want) => (want
    ? sendNudgeAs('ending', want, { inPress: realPress(win, ev), send, win, ed: ed || editionState() })
    : sendNudge('ending', { inPress: realPress(win, ev), send, win, ed })),
  /* false inside the desktop app, where a "get CCP" button has nowhere to go */
  { offersDownload: (ed || editionState()).platform !== 'desktop' });

/** One nudge whose `want` the door itself names (the funnel, owner 2026-11-24: a locked ending card and the recap
 *  wall's main button ask for the plan, its second button for the app). Both ids must be on the fixed lists
 *  (CONTRACT.md 6.2), so the frame is the same `nudge {where, want}`; unhosted, the fixed link for that want. Only from
 *  a real press. Answers whether it went. */
export function sendNudgeAs(where, want, { inPress = () => false, send = () => {}, win = globalThis.window, ed = editionState() } = {}) {
  if (!NUDGE_WHERE.includes(where) || !NUDGE_WANT.includes(want) || !inPress()) return false;
  if (ed.hosted) { send('nudge', { where, want }); return true; }
  return openAllowedLink(nudgeLink(want), { inPress, hosted: false, win });
}

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
  go.addEventListener('click', (ev) => sendNudge('pause', { inPress: realPress(win, ev), send, win, ed }));
  line.appendChild(go);
  card.appendChild(line);
  return line;
}
