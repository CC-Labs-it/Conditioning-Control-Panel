/* ui/save-sleep.js - the small edition's two quiet cards (lane S4, owner 2026-11-22; rules in sim/save.js).
 *
 *   save-sleep   once per save (extras.sleepSeen), when a small page loads a save that holds rows or Renewal state the
 *                small edition keeps asleep. A card in the corner (ui/cards.js `corner`), never a backdrop: play goes
 *                on under it. Its button sends the `save-sleep` nudge from the player's own press (sendNudge).
 *   sky-asleep   an open Renewal Sky visit loaded in small: no Sky, a plain card with the nudge and New run.
 *
 * COPY: every string below is a PLACEHOLDER until the owner words it (lane S notes, Run 6). Gentle and reduced motion:
 * the card queue's own short fade, nothing else moves. */

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

/** Mounts the once-per-save note on the game's own card queue (ui/cards.js, `corner`: no backdrop, play goes on under
 *  it, in the card's own theme). extras: the save's own extras (sleepSeen is set at once, saved with the next save).
 *  Answers a close function. send/ed are the host's frame sender and the edition reading. */
export function mountSaveSleep({ cards, win = globalThis.window, extras, send = () => {}, ed, save = () => {} } = {}) {
  if (!cards || !extras || extras.sleepSeen) return () => {};
  extras.sleepSeen = 1;
  save();
  const handle = cards.open({
    title: SAVE_SLEEP_COPY.title,
    body: SAVE_SLEEP_COPY.body,
    corner: true,
    className: 'save-sleep',
    buttons: [
      { label: SAVE_SLEEP_COPY.nudge, onPress: () => sendNudge('save-sleep', { inPress: realPress(win), send, win, ed }) },
      { label: SAVE_SLEEP_COPY.dismiss },
    ],
  });
  return () => handle?.close?.();
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
