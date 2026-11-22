/* ui/save-sleep.js - the small edition's two quiet cards (lane S4, owner 2026-11-22; rules in sim/save.js).
 *
 *   save-sleep   once per save (extras.sleepSeen), when a small page loads a save that holds rows or Renewal state the
 *                small edition keeps asleep. A note in the corner, never a backdrop: play goes on under it. Its button
 *                sends the `save-sleep` nudge from the player's own press (ui/edition.js sendNudge).
 *   sky-asleep   an open Renewal Sky visit loaded in small: no Sky, a plain card with the nudge and New run.
 *
 * COPY: every string below is a PLACEHOLDER until the owner words it (lane S notes, Run 6). Gentle and reduced motion:
 * the note never moves; with neither on it fades in over 200 ms and that is all. */

import { sendNudge } from './edition.js';

export const SAVE_SLEEP_COPY = Object.freeze({
  title: '[PLACEHOLDER S4-sleep-title]',
  body: '[PLACEHOLDER S4-sleep-body]',
  nudge: '[PLACEHOLDER S4-sleep-nudge-button]',
  dismiss: '[PLACEHOLDER S4-sleep-dismiss]',
});
export const SKY_ASLEEP_COPY = Object.freeze({
  title: '[PLACEHOLDER S4-sky-title]',
  body: '[PLACEHOLDER S4-ended-nudge-line]',
  nudge: '[PLACEHOLDER S4-ended-nudge-button]',
  newRun: '[PLACEHOLDER S4-sky-new-run]',
});

/** A real press: the browser's own transient activation, which a script-made click does not carry. */
const realPress = (win) => () => !!win?.navigator?.userActivation?.isActive;

const CSS = `
#save-sleep { position: fixed; right: max(12px, env(safe-area-inset-right)); bottom: max(12px, env(safe-area-inset-bottom)); z-index: 80;
  max-width: min(320px, calc(100vw - 24px)); padding: 12px 14px; border-radius: 10px; background: #fffdf6; color: #2a2430;
  box-shadow: 0 4px 18px #0003; font: 500 14px/1.35 system-ui; pointer-events: auto; }
#save-sleep h3 { margin: 0 0 4px; font-size: 15px; }
#save-sleep p { margin: 0 0 10px; }
#save-sleep .row { display: flex; gap: 8px; flex-wrap: wrap; }
#save-sleep button { font: inherit; min-height: 40px; padding: .3em .9em; border-radius: 8px; border: 1px solid #b9aec6; background: #fff; cursor: pointer; }
#save-sleep button.primary { background: #2a2430; color: #fff; border-color: #2a2430; }
#save-sleep.fade { animation: save-sleep-in .2s ease-out; }
@keyframes save-sleep-in { from { opacity: 0; } }
@media (prefers-reduced-motion: reduce) { #save-sleep.fade { animation: none; } }
`;

/** Mounts the once-per-save note. extras: the save's own extras (sleepSeen is set at once, saved with the next save).
 *  Answers a close function. send/ed are the host's frame sender and the edition reading. */
export function mountSaveSleep({ doc = document, win = doc.defaultView, extras, send = () => {}, ed, gentle = () => false, reduced = () => false, save = () => {} } = {}) {
  if (!extras || extras.sleepSeen || doc.getElementById('save-sleep')) return () => {};
  extras.sleepSeen = 1;
  save();
  if (!doc.getElementById('save-sleep-css')) {
    const st = doc.createElement('style'); st.id = 'save-sleep-css'; st.textContent = CSS; doc.head.appendChild(st);
  }
  const el = doc.createElement('aside');
  el.id = 'save-sleep';
  el.setAttribute('role', 'status');
  if (!gentle() && !reduced()) el.className = 'fade';
  const h = doc.createElement('h3'); h.textContent = SAVE_SLEEP_COPY.title;
  const p = doc.createElement('p'); p.textContent = SAVE_SLEEP_COPY.body;
  const row = doc.createElement('div'); row.className = 'row';
  const go = doc.createElement('button'); go.type = 'button'; go.className = 'primary'; go.textContent = SAVE_SLEEP_COPY.nudge;
  const shut = doc.createElement('button'); shut.type = 'button'; shut.textContent = SAVE_SLEEP_COPY.dismiss;
  row.append(go, shut);
  el.append(h, p, row);
  doc.body.appendChild(el);
  const close = () => el.remove();
  go.addEventListener('click', () => { sendNudge('save-sleep', { inPress: realPress(win), send, win, ed }); close(); });
  shut.addEventListener('click', close);
  return close;
}

/** The plain card for an open Sky visit in small (sim/save.js sleepingSky). cards: ui.cards. newRun: the player's own
 *  New run, which keeps extras.sky as it is. */
export function openSkyAsleep({ cards, win = globalThis.window, send = () => {}, ed, newRun }) {
  return cards.open({
    title: SKY_ASLEEP_COPY.title,
    body: SKY_ASLEEP_COPY.body,
    front: true,
    buttons: [
      { label: SKY_ASLEEP_COPY.newRun, onPress: () => newRun() },
      { label: SKY_ASLEEP_COPY.nudge, keepOpen: true, onPress: () => sendNudge('ending', { inPress: realPress(win), send, win, ed }) },
    ],
  });
}
