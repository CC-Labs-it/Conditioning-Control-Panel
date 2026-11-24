/* ui/save-carry.js - carry one run between this page and the account (lane S7b, CONTRACT.md 6.3). Two buttons in the
 * pause card's quiet area, only when the host keeps a save slot (ui/edition.js saveSlotOn; main.js asks):
 *
 *   send   `save-offer` with the whole save string, only from the player's press, only when it fits both caps
 *          (ui/edition.js saveOfferFrame). Never on autosave.
 *   bring  `save-ask`. The host answers `save-incoming`, which NEVER applies by itself: a card says where it comes from
 *          and when (the player's local date and time), with accept and keep-mine. Accept goes through the caller's
 *          load path (main.js: sim/save.js parse with the edition, so in small the SLEEP rule keeps cut rows asleep and
 *          nothing is deleted; the local save is kept as the .bak first). A save that does not parse is refused and
 *          the run is untouched.
 *   `save-result {ok, reason}` says it went, or why. One call at a time: a press while one flies does nothing.
 *   In small, after a send that went, one line points at the full game: the `save-sleep` nudge (the carry's own id).
 *
 * Nothing here reads the edition. COPY: APPROVED AND FROZEN (owner 2026-11-22, fourth dialog): character for character, pinned in
 * tests/editions-placeholders.test.mjs. */

import { saveOfferFrame, readSaveIncoming, sendNudge } from './edition.js';
import { realPress } from './nudges.js';

export const SAVE_CARRY_COPY = Object.freeze({
  send: "Send this run to my account",
  ask: "Fetch my account's run",
  sent: "Sent. Your account holds this run.",
  'sign-in': "Sign in first.",
  'too-big': "This run is too big to send.",
  offline: "No signal. Try again later.",
  refused: "That did not go through.",
  none: "Your account holds no run yet.",
  broken: "That run could not be read. Yours is untouched.",
  incomingTitle: "A run from your account",
  incomingBody: "Saved on {from}, {when}. Play it here instead of this one?",
  fromWeb: "the website",
  fromDesktop: "the app",
  accept: "Play that run",
  keep: "Keep mine",
  fullLine: "The full game can pick it up.",
  fullButton: "The full game",
});
export const SAVE_REASONS = Object.freeze(['sign-in', 'too-big', 'offline', 'refused', 'none']);

/** The incoming card's sentence: where from, and when in the player's own local date and time. */
const MONTHS = Object.freeze(['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']);
/** {when} as the owner wrote it: "22 Nov 14:05", day, short month, 24-hour time, in the player's local time. */
export function whenLine(savedAt) {
  const d = new Date(savedAt), two = (n) => String(n).padStart(2, '0');
  return `${d.getDate()} ${MONTHS[d.getMonth()]} ${two(d.getHours())}:${two(d.getMinutes())}`;
}
export function incomingLine({ from, savedAt }) {
  const where = from === 'desktop' ? SAVE_CARRY_COPY.fromDesktop : SAVE_CARRY_COPY.fromWeb;
  return SAVE_CARRY_COPY.incomingBody.replace('{from}', where).replace('{when}', whenLine(savedAt));
}

/** The carry's rules, no page. exportSave(): the whole save string now. accept(save): the caller's load path, answers
 *  whether it took. ask(incoming): shows the incoming card and calls back with true (accept) or false (keep mine).
 *  inPress(ev): is this the player's own press. say(line, { full }): the status line under the buttons. */
export function createSaveCarry({ send = () => {}, inPress = () => false, exportSave = () => '', accept = () => false, ask = () => {}, say = () => {}, small = false }) {
  let busy = null;   // 'offer' | 'ask' while a call flies
  return {
    get busy() { return busy; },
    offer(ev) {
      if (busy || !inPress(ev)) return false;
      const frame = saveOfferFrame(exportSave());
      if (!frame) { say(SAVE_CARRY_COPY['too-big']); return false; }
      busy = 'offer';
      say('');
      send('save-offer', frame);
      return true;
    },
    askForRun(ev) {
      if (busy || !inPress(ev)) return false;
      busy = 'ask';
      say('');
      send('save-ask');
      return true;
    },
    result(f) {
      if (!busy) return;
      const was = busy;
      if (f?.ok === true) {
        busy = null;
        if (was === 'offer') say(SAVE_CARRY_COPY.sent, { full: small });
        return;   // an ask that went: the run itself comes as save-incoming
      }
      busy = null;
      say(SAVE_CARRY_COPY[SAVE_REASONS.includes(f?.reason) ? f.reason : 'refused']);
    },
    incoming(f) {
      if (busy !== 'ask') return false;   // only as the answer to our own save-ask
      busy = null;
      const run = readSaveIncoming(f);
      if (!run) { say(SAVE_CARRY_COPY.broken); return false; }
      ask(run, (yes) => { if (yes && !accept(run.save)) say(SAVE_CARRY_COPY.broken); });
      return true;
    },
  };
}

/** Mounts the carry on the pause card, once. slot: the host keeps a save (saveSlotOn); without it nothing shows.
 *  cards: ui.cards. on/send: the bridge. small: offer the full-game line after a send (main.js asks isSmall).
 *  Answers the carry, or null. */
export function mountSaveCarry({ doc = document, win = doc.defaultView, cards, on = () => () => {}, send = () => {}, slot = false, small = false, exportSave, accept, ed }) {
  const card = doc.querySelector?.('#pause .card');
  if (!slot || !card || doc.getElementById('save-carry')) return null;
  const box = doc.createElement('div');
  box.id = 'save-carry';
  box.className = 'card-buttons small';
  const btn = (text) => { const b = doc.createElement('button'); b.type = 'button'; b.textContent = text; box.appendChild(b); return b; };
  const sendBtn = btn(SAVE_CARRY_COPY.send), askBtn = btn(SAVE_CARRY_COPY.ask);
  const status = doc.createElement('p');
  status.className = 'card-sub';
  status.setAttribute('role', 'status');
  status.hidden = true;
  const full = doc.createElement('button');
  full.type = 'button';
  full.textContent = SAVE_CARRY_COPY.fullButton;
  full.hidden = true;
  full.addEventListener('click', (ev) => sendNudge('save-sleep', { inPress: realPress(win, ev), send, win, ed }));
  card.appendChild(box);
  card.appendChild(status);
  card.appendChild(full);
  const say = (line, { full: withFull = false } = {}) => {
    status.textContent = withFull ? `${line} ${SAVE_CARRY_COPY.fullLine}` : line;
    status.hidden = !line;
    full.hidden = !withFull;
  };
  const carry = createSaveCarry({
    send, small, exportSave, accept, say,
    inPress: (ev) => realPress(win, ev)(),
    ask: (run, answer) => cards.open({
      title: SAVE_CARRY_COPY.incomingTitle,
      body: incomingLine(run),
      front: true,
      overPause: true,   // asked from the pause card: shown over it and answerable at once (ui/cards.js)
      onOpen: (dom) => dom.querySelector?.('button')?.focus?.(),
      buttons: [
        { label: SAVE_CARRY_COPY.keep, onPress: () => answer(false) },
        { label: SAVE_CARRY_COPY.accept, onPress: () => answer(true) },
      ],
    }),
  });
  sendBtn.addEventListener('click', (ev) => carry.offer(ev));
  askBtn.addEventListener('click', (ev) => carry.askForRun(ev));
  on('save-result', (f) => carry.result(f));
  on('save-incoming', (f) => carry.incoming(f));
  return carry;
}
