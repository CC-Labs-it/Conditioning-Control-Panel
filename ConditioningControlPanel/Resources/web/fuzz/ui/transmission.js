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
import { TRANSMISSION_MIN_RUN_MS, LLAMA8_GAP_MS } from '../data/editions.js';
import { realPress } from './nudges.js';   // the one press rule

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
  /* Small: its first button (fourth dialog); the full card keeps YES. */
  smallYes: 'THE FULL GAME',
});

/** Every other line of the cards: APPROVED AND FROZEN (owner 2026-11-22, fourth dialog): character for character, pinned in
 * tests/editions-placeholders.test.mjs. */
export const TRANSMISSION_LINES = Object.freeze({
  progress: "Receiving. {pct}%",
  arrived: "Transmission received.",
  arrivedClose: "CLOSE",
  offline: "No signal. The future will try again.",
  disk: "Not enough room on the disk.",
  refused: "The transmission did not check out. It can be sent again.",
  stop: "STOP",
  stopped: "Stopped. What arrived is kept.",
  penSign: "TRANSMISSION WAITING",
  llama8Title: "A full pen.",
  llama8Body: "Eight llamas open something this edition cannot show. It is in the full game.",
  llama8Button: "The full game",
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
  if (kind === 'nudge') return { title: C.title, body: C.smallBody, size: '', line: C.smallLine, yes: C.smallYes, notNow: C.notNow };
  return { title: C.title, body: C.body, size: sizeLine(mb), line: '', yes: C.yes, notNow: C.notNow };
}

/** The status line for a state of the card ('' while it only asks). */
export function statusLine({ state, pct = 0, reason = null }) {
  const P = TRANSMISSION_LINES;
  if (state === 'fetching') return P.progress.replace('{pct}', String(pct));
  if (state === 'arrived') return P.arrived;
  if (state === 'failed') return P[FAIL_REASONS.includes(reason) ? reason : 'refused'];
  if (state === 'stopped') return P.stopped;
  return '';
}

/** The card's state. kind: 'fetch' or 'nudge'. send: the host frame sender. inPress: is this the player's own press.
 *  YES and stop act only inside a press (inPress(ev): ev is the press event); host frames move the state. */
export function createTransmission({ kind, send = () => {}, inPress = () => false, ed = editionState, nudge = sendNudge }) {
  const s = { kind, state: 'offer', pct: 0, reason: null };
  const fns = new Set();
  const changed = () => { for (const fn of fns) fn({ ...s }); };
  return {
    get view() { return { ...s }; },
    onChange(fn) { fns.add(fn); return () => fns.delete(fn); },
    /** YES. fetch: one `expo-fetch` (again only after a failure or a stop). nudge: the `transmission` nudge. */
    yes(ev) {
      if (!inPress(ev)) return false;
      if (kind === 'nudge') return nudge('transmission', { inPress: () => inPress(ev), send, ed: ed() });
      if (kind !== 'fetch' || s.state === 'fetching' || s.state === 'arrived') return false;
      send('expo-fetch');
      s.state = 'fetching'; s.pct = 0; s.reason = null;
      changed();
      return true;
    },
    stop(ev) {
      if (kind !== 'fetch' || s.state !== 'fetching' || !inPress(ev)) return false;
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

/** Which card opens by itself now, if any: 'transmission', 'llama8' or null. clock: the run clock (ms played).
 *  opened: this run's self-opened cards, { transmission, llama8 } = the clock each opened at, or null. Nothing before
 *  TRANSMISSION_MIN_RUN_MS; the transmission card first; one per run, unless LLAMA8_GAP_MS more play has passed. */
export function autoCardDue({ clock, llamas, extras, opened = {}, ed = editionState() }) {
  if (!(Number(clock) >= TRANSMISSION_MIN_RUN_MS)) return null;
  const t = opened.transmission ?? null, l = opened.llama8 ?? null;
  if (t == null && l == null && autoOfferDue(llamas, extras, ed)) return 'transmission';
  if (l != null || !llama8Due(llamas, extras, ed)) return null;
  return t == null || clock >= t + LLAMA8_GAP_MS ? 'llama8' : null;
}


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
 *  cards: ui.cards. on/send: the bridge. pen: the llama pen's element (the sign sits beside it). llamas: the herd, a
 *  number or a function (it may grow mid-run). clock: the run clock in ms (autoCardDue). Answers { open, close, tick }. */
export function mountTransmission({ doc = document, win = doc.defaultView, cards, on = () => () => {}, send = () => {}, llamas = 0, clock = () => 0, extras = {}, save = () => {}, pen = null }) {
  host?.close();
  host = null;
  if (!doc.getElementById('tx-css')) { const st = doc.createElement('style'); st.id = 'tx-css'; st.textContent = CSS; doc.head.appendChild(st); }
  const herd = typeof llamas === 'function' ? llamas : () => llamas;
  const inPress = (ev) => realPress(win, ev)();
  const offs = [];
  let kind = null, tx = null, card = null, sign = null, waiters = [];
  const opened = { transmission: null, llama8: null };   // this run's self-opened cards, by run clock
  /* The herd may reach the count mid-run: the transmission is made the first time it calls for a kind. */
  const ensureTx = () => {
    if (tx) return tx;
    kind = transmissionKind(herd());
    if (!kind) return null;
    tx = createTransmission({ kind, send, inPress });
    offs.push(on('expo-progress', (f) => tx.progress(f?.pct)));
    offs.push(on('expo-ready', () => { if (expoAllowed()) { tx.ready(); drawSign(); } }));
    offs.push(on('expo-failed', (f) => tx.failed(f?.reason)));
    return tx;
  };

  const drawSign = () => {
    const due = tx && penSignDue({ llamas: herd(), extras, open: !!card, state: tx.view.state });
    if (due && !sign && pen?.isConnected) {
      sign = doc.createElement('button');
      sign.id = 'llama-sign'; sign.type = 'button'; sign.textContent = TRANSMISSION_LINES.penSign;
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
    const stop = el('button', 'tx-stop', TRANSMISSION_LINES.stop); stop.type = 'button';
    stop.addEventListener('click', (ev) => tx.stop(ev));
    let yesBtn = null, shutBtn = null;
    const paint = (s = tx.view) => {
      status.textContent = statusLine(s);
      status.hidden = !status.textContent;
      bar.hidden = kind !== 'fetch' || (s.state !== 'fetching' && s.state !== 'arrived');
      fill.style.width = `${s.pct}%`;
      stop.hidden = s.state !== 'fetching';
      if (yesBtn) yesBtn.hidden = s.state === 'fetching' || s.state === 'arrived';
      if (shutBtn) shutBtn.textContent = s.state === 'arrived' ? TRANSMISSION_LINES.arrivedClose : w.notNow;
    };
    card = cards.open({
      className: 'tx-card', title: w.title, body: w.body, node, front,
      buttons: [
        { label: w.yes, keepOpen: true, onPress: (ev) => { if (tx.yes(ev) && kind === 'nudge') card?.close(); } },
        { label: w.notNow },
      ],
      onOpen: (dom) => { const row = dom.querySelectorAll?.('.card-buttons button') || []; yesBtn = row[0] || dom.querySelector('.card-buttons button'); shutBtn = row[1] || null; paint(v); },
      onClose: () => { off(); card = null; drawSign(); const ws = waiters; waiters = []; for (const fn of ws) fn(expoAllowed()); },
    });
    const off = tx.onChange((s) => {
      paint(s);
      if (s.state === 'arrived' && waiters.length) card?.close();   // at the wormhole: on into the Expo
    });
    drawSign();
    return card;
  }

  function openLlama8() {
    const P = TRANSMISSION_LINES;
    cards.open({
      title: P.llama8Title, body: P.llama8Body,
      buttons: [
        { label: P.llama8Button, onPress: (ev) => sendNudge('llama8', { inPress: realPress(win, ev), send, win }) },
        { label: TRANSMISSION_COPY.notNow },
      ],
    });
  }
  /* Once a second (and at once): a self-opening card when autoCardDue says so, the pen sign otherwise. */
  const tick = () => {
    ensureTx();
    const now = Number(clock()) || 0;
    const due = autoCardDue({ clock: now, llamas: herd(), extras, opened });
    if (due === 'transmission') { extras.transmissionSeen = 1; opened.transmission = now; save(); open(); }
    else if (due === 'llama8') { extras.llama8Seen = 1; opened.llama8 = now; save(); openLlama8(); }
    drawSign();
  };
  tick();
  const timer = setInterval(tick, 1000);
  timer?.unref?.();
  offs.push(() => clearInterval(timer));

  const me = {
    open,
    tick,
    /** The wormhole, Expo absent: the card first. Answers whether the Expo is here when the card closes. */
    atWormhole() {
      if (expoAllowed()) return Promise.resolve(true);
      if (!ensureTx() || kind !== 'fetch') return Promise.resolve(true);   // nothing to fetch here: the door keeps its own refusal
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
