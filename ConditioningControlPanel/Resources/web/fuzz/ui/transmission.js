/* ui/transmission.js - the transmission card and the llama-8 card (lane S6, owner 2026-11-22; wire in CONTRACT.md 6.5).
 *
 *   fetch   full edition, desktop, Expo absent, 4 llamas or more: a message from the future is transmitting the Expo.
 *           YES sends `expo-fetch` from the player's own press, `expo-progress` moves a bar, `expo-ready` says it
 *           arrived (main.js hands the base to ui/edition.js expoArrived first), `expo-failed` says why and offers it
 *           again. A stop button sends `expo-cancel`. NOT NOW only closes the card: the pen sign offers it again, and at
 *           the wormhole (ui/unlocks/fate/llama/ending.js) the same card comes first while the Expo is still absent.
 *   nudge   small edition, 4 llamas or more: the same card, without the size, and YES is the `transmission` nudge.
 *   llama8  small edition, 8 llamas or more: one card, its button is the `llama8` nudge.
 *
 * Which kind a herd calls for is ui/edition.js transmissionKind; nothing here reads the edition itself. Nothing here
 * imports or fetches an Expo file. The card is the game's own card queue (ui/cards.js) in its theme tokens.
 * Gentle and reduced motion: nothing here moves; the bar only changes width. Pure rules first, so tests need no page. */

import { transmissionKind, sendNudge, editionState, expoAllowed, expoMb, isSmall } from './edition.js';

/** APPROVED AND FROZEN (owner 2026-11-22, handoff-1122/TC-EDITIONS-PLAN.md, third dialog, point 5). Character for
 *  character: never edit one of these without the owner. `{mb}` is the host's number (ui/edition.js expoMb). */
export const TRANSMISSION_COPY = Object.freeze({
  title: 'INCOMING TRANSMISSION',
  body: 'A message from the future is transmitting some data. Do you accept the transmission?',
  size: '{mb} MB, saved to your CCP content folder.',
  yes: 'YES',
  notNow: 'NOT NOW',
  /* Small: the same title and first sentence, then this line (same record). */
  smallBody: 'A message from the future is transmitting some data.',
  smallLine: 'It does not fit here. The full game can receive it.',
});

/** Every other line: NOT APPROVED. One table, one survey for the owner; the slot ids are listed in the lane S note. */
export const TRANSMISSION_PLACEHOLDERS = Object.freeze({
  progress: '[PLACEHOLDER S6-progress] Receiving: {pct}%',
  arrived: '[PLACEHOLDER S6-arrived] Transmission received.',
  offline: '[PLACEHOLDER S6-failed-offline] No connection. It can try again later.',
  disk: '[PLACEHOLDER S6-failed-disk] Not enough room on the disk.',
  refused: '[PLACEHOLDER S6-failed-refused] The transmission was refused. It can try again later.',
  stop: '[PLACEHOLDER S6-stop] Stop',
  stopped: '[PLACEHOLDER S6-stopped] Stopped. What arrived is kept.',
  penSign: '[PLACEHOLDER S6-pen-sign] Transmission',
  llama8Title: '[PLACEHOLDER S6-llama8-title] Eight llamas',
  llama8Body: '[PLACEHOLDER S6-llama8-body] The herd is ready for the Expo. The Expo is in the full game.',
  llama8Button: '[PLACEHOLDER S6-llama8-button] Get the full game',
});

export const FAIL_REASONS = Object.freeze(['offline', 'disk', 'refused']);
export const LLAMA8_AT = 8;

/** The size line, or '' when the host gave no size. */
export function sizeLine(mb) {
  const n = Math.round(Number(mb));
  return Number.isFinite(n) && n > 0 ? TRANSMISSION_COPY.size.replace('{mb}', String(n)) : '';
}

/** The card's words for a kind: { title, body, size, line, yes, notNow }. */
export function cardWords(kind, mb) {
  const C = TRANSMISSION_COPY;
  if (kind === 'nudge') return { title: C.title, body: C.smallBody, size: '', line: C.smallLine, yes: C.yes, notNow: C.notNow };
  return { title: C.title, body: C.body, size: sizeLine(mb), line: '', yes: C.yes, notNow: C.notNow };
}

/** The status line for a state of the card ('' while it only asks). */
export function statusLine({ state, pct = 0, reason = null }) {
  const P = TRANSMISSION_PLACEHOLDERS;
  if (state === 'fetching') return P.progress.replace('{pct}', String(pct));
  if (state === 'arrived') return P.arrived;
  if (state === 'failed') return P[FAIL_REASONS.includes(reason) ? reason : 'refused'];
  if (state === 'stopped') return P.stopped;
  return '';
}

/** The card's state. kind: 'fetch' or 'nudge'. send: the host frame sender. inPress: is this the player's own press.
 *  YES and stop act only inside a press; host frames (progress, ready, failed) move the state. */
export function createTransmission({ kind, send = () => {}, inPress = () => false, ed = editionState, nudge = sendNudge }) {
  const s = { kind, state: 'offer', pct: 0, reason: null };
  const fns = new Set();
  const changed = () => { for (const fn of fns) fn({ ...s }); };
  return {
    get view() { return { ...s }; },
    onChange(fn) { fns.add(fn); return () => fns.delete(fn); },
    /** YES. fetch: one `expo-fetch` (again only after a failure or a stop). nudge: the `transmission` nudge. */
    yes() {
      if (!inPress()) return false;
      if (kind === 'nudge') return nudge('transmission', { inPress, send, ed: ed() });
      if (kind !== 'fetch' || s.state === 'fetching' || s.state === 'arrived') return false;
      send('expo-fetch');
      s.state = 'fetching'; s.pct = 0; s.reason = null;
      changed();
      return true;
    },
    stop() {
      if (kind !== 'fetch' || s.state !== 'fetching' || !inPress()) return false;
      send('expo-cancel');
      s.state = 'stopped';
      changed();
      return true;
    },
    progress(pct) {
      const n = Math.round(Number(pct));
      if (s.state !== 'fetching' || !Number.isFinite(n)) return;
      s.pct = Math.max(s.pct, Math.min(100, Math.max(0, n)));
      changed();
    },
    ready() {
      if (kind !== 'fetch' || s.state === 'arrived') return;
      s.state = 'arrived'; s.pct = 100; s.reason = null;
      changed();
    },
    failed(reason) {
      if (kind !== 'fetch' || s.state === 'arrived') return;
      s.state = 'failed'; s.reason = FAIL_REASONS.includes(reason) ? reason : 'refused';
      changed();
    },
  };
}

/** Does the card open by itself now? Once per save (extras.transmissionSeen), when the herd calls for a kind. */
export const autoOfferDue = (llamas, extras, ed = editionState()) => !!transmissionKind(llamas, ed) && !extras?.transmissionSeen;
/** Does the pen wear the sign? After the first offer, while the herd calls for a kind, the card is shut and nothing
 *  arrived. */
export const penSignDue = ({ llamas, extras, open = false, state = 'offer', ed = editionState() }) =>
  !!transmissionKind(llamas, ed) && !!extras?.transmissionSeen && !open && state !== 'arrived';
/** The llama-8 card: small only, once per save (extras.llama8Seen). */
export const llama8Due = (llamas, extras, ed = editionState()) => isSmall(ed) && Number(llamas) >= LLAMA8_AT && !extras?.llama8Seen;

/** A real press: the browser's own transient activation, which a script-made click does not carry. */
const realPress = (win) => () => !!win?.navigator?.userActivation?.isActive;

const CSS = `
.tx-card .tx-size { margin: 0 0 8px; opacity: .8; font-size: .9em; }
.tx-card .tx-line { margin: 0 0 8px; }
.tx-card .tx-status { min-height: 1.3em; margin: 0 0 6px; }
.tx-card .tx-bar { height: 8px; margin: 0 0 10px; border-radius: 4px; background: var(--line, #0002); overflow: hidden; }
.tx-card .tx-bar i { display: block; height: 100%; width: 0; background: var(--accent, #6a4cff); }
.tx-card [hidden] { display: none !important; }
#llama-sign { position: absolute; z-index: 5; font: 600 12px/1.2 var(--font, system-ui); min-height: 32px; padding: 4px 10px;
  border-radius: 6px; border: 1px solid var(--line, #0003); background: var(--panel, #fff); color: var(--ink, #222); pointer-events: auto; cursor: pointer; }
`;

let host = null;   // the live page's transmission, for the wormhole

/** Mounts the transmission for this run: the first offer, the pen sign, the llama-8 card and the host frames.
 *  cards: ui.cards. on/send: the bridge. pen: the llama pen's element (the sign sits beside it). Answers { open, close }. */
export function mountTransmission({ doc = document, win = doc.defaultView, cards, on = () => () => {}, send = () => {}, llamas = 0, extras = {}, save = () => {}, pen = null }) {
  host?.close();
  host = null;
  if (!doc.getElementById('tx-css')) { const st = doc.createElement('style'); st.id = 'tx-css'; st.textContent = CSS; doc.head.appendChild(st); }
  const kind = transmissionKind(llamas);
  const inPress = realPress(win);
  const offs = [];
  let tx = kind ? createTransmission({ kind, send, inPress }) : null;
  let card = null, sign = null, waiters = [];

  const drawSign = () => {
    const due = tx && penSignDue({ llamas, extras, open: !!card, state: tx.view.state });
    if (due && !sign && pen?.isConnected) {
      sign = doc.createElement('button');
      sign.id = 'llama-sign'; sign.type = 'button'; sign.textContent = TRANSMISSION_PLACEHOLDERS.penSign;
      const r = pen.getBoundingClientRect();
      sign.style.left = `${Math.round(r.left)}px`; sign.style.top = `${Math.round(r.bottom + 4)}px`;
      sign.addEventListener('click', () => open());
      (pen.parentNode || doc.body).appendChild(sign);
    } else if (!due && sign) { sign.remove(); sign = null; }
  };

  function open({ front = false } = {}) {
    if (!tx || card) return card;
    const v = tx.view, w = cardWords(kind, expoMb());
    const node = doc.createElement('div');
    const el = (tag, cls, text = '') => { const e = doc.createElement(tag); e.className = cls; e.textContent = text; node.appendChild(e); return e; };
    if (w.size) el('p', 'tx-size', w.size);
    if (w.line) el('p', 'tx-line', w.line);
    const status = el('p', 'tx-status');
    status.setAttribute('role', 'status');
    const bar = el('div', 'tx-bar'); const fill = doc.createElement('i'); bar.appendChild(fill);
    const stop = el('button', 'tx-stop', TRANSMISSION_PLACEHOLDERS.stop); stop.type = 'button';
    stop.addEventListener('click', () => tx.stop());
    let yesBtn = null;
    const paint = (s = tx.view) => {
      status.textContent = statusLine(s);
      status.hidden = !status.textContent;
      bar.hidden = kind !== 'fetch' || (s.state !== 'fetching' && s.state !== 'arrived');
      fill.style.width = `${s.pct}%`;
      stop.hidden = s.state !== 'fetching';
      if (yesBtn) yesBtn.hidden = s.state === 'fetching' || s.state === 'arrived';
    };
    card = cards.open({
      className: 'tx-card', title: w.title, body: w.body, node, front,
      buttons: [
        { label: w.yes, keepOpen: true, onPress: () => { if (tx.yes() && kind === 'nudge') card?.close(); } },
        { label: w.notNow },
      ],
      onOpen: (dom) => { yesBtn = dom.querySelector('.card-buttons button'); paint(v); },
      onClose: () => { off(); card = null; drawSign(); const ws = waiters; waiters = []; for (const fn of ws) fn(expoAllowed()); },
    });
    const off = tx.onChange((s) => {
      paint(s);
      if (s.state === 'arrived' && waiters.length) card?.close();   // at the wormhole: on into the Expo
    });
    drawSign();
    return card;
  }

  if (tx) {
    offs.push(on('expo-progress', (f) => tx.progress(f?.pct)));
    offs.push(on('expo-ready', () => { if (expoAllowed()) { tx.ready(); drawSign(); } }));
    offs.push(on('expo-failed', (f) => tx.failed(f?.reason)));
    if (autoOfferDue(llamas, extras)) { extras.transmissionSeen = 1; save(); open(); }
    else drawSign();
  }
  if (llama8Due(llamas, extras)) {
    extras.llama8Seen = 1; save();
    const P = TRANSMISSION_PLACEHOLDERS;
    cards.open({
      title: P.llama8Title, body: P.llama8Body,
      buttons: [
        { label: P.llama8Button, onPress: () => sendNudge('llama8', { inPress, send, win }) },
        { label: TRANSMISSION_COPY.notNow },
      ],
    });
  }

  const me = {
    open,
    /** The wormhole, Expo absent: the card first. Answers whether the Expo is here when the card closes. */
    atWormhole() {
      if (expoAllowed()) return Promise.resolve(true);
      if (!tx || kind !== 'fetch') return Promise.resolve(true);   // nothing to fetch here: the door keeps its own refusal
      return new Promise((resolve) => { waiters.push(resolve); if (!card) open({ front: true }); });
    },
    close() { for (const off of offs) off?.(); card?.close(); sign?.remove(); sign = null; },
    get state() { return tx?.view || null; },
  };
  host = me;
  return me;
}

/** For the wormhole (ui/unlocks/fate/llama/ending.js): with the Expo absent on a page that can fetch it, the card comes
 *  first. Answers false only when that card closed with the Expo still absent (no visit); otherwise true, and with no
 *  transmission on this page the door keeps its own refusal, as before. */
export function transmissionAtWormhole() {
  if (expoAllowed()) return Promise.resolve(true);
  return host ? host.atWormhole() : Promise.resolve(true);
}
