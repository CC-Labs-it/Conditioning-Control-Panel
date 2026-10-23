/* bank-card.js - THE BANK's end card (conversion lane G1, brief C10).
 *
 * "Bank it with a Lockdown stake" (Basic), once, after THE BANK has finished paying a big win. Never on a
 * bust (a bust never pays), never mid-spin (the next bank run takes it down), never while the tokens fly
 * (it waits for the run's own `done`), and never to a viewer the host has not said is on the free tier:
 * an unknown tier shows nothing, so a Basic player is never sold what they have.
 *
 * shared/win/bank.js loads this lazily, only where there is a DOM, and calls `paid` / `start`. Everything
 * the decision reads is in `bankCardWanted`, which is pure so node can hold it.
 * ==========================================================================*/

import { buildEndCard, planOf, postFunnel, viewerTier, loadEndCardCss } from './end-card.js';

/** plan.js PARTY.MS[3]: the rollup a rung-3 ("big") win gets. Smaller wins never carry the card. */
export const BIG_WIN_ROLLUP_MS = 2000;
/** How long the card stays up when nobody touches it. */
export const BANK_CARD_MS = 12000;
export const BANK_CARD_LINE = 'Bank it with a Lockdown stake.';

/**
 * Should this settled pay carry the card? `state` is the room's own state (`window.__backroom.state`):
 * `tier` (0 | 1 | 2, from the host) and `breakout` (a full Breakout is Prime, so never).
 */
export function bankCardWanted({ fromValue, toValue, rollupMs } = {}, state, alreadyShown = false) {
  if (alreadyShown) return false;
  if (!(Number(toValue) > Number(fromValue))) return false;              // a win, never a bust
  if (!(Number(rollupMs) >= BIG_WIN_ROLLUP_MS)) return false;            // a big one
  if (!state || typeof state !== 'object') return false;
  if (state.breakout && state.breakout.demo === false) return false;     // full Breakout = Prime
  return viewerTier(state) === 0;
}

let shown = false, node = null, timer = 0;

function hide() {
  if (timer) { clearTimeout(timer); timer = 0; }
  const n = node; node = null;
  if (!n) return;
  try {
    n.classList.remove('is-in');
    setTimeout(() => { try { n.remove(); } catch (e) { /* gone */ } }, 450);
  } catch (e) { /* gone */ }
}

/** THE BANK finished a pay run on its own (not skipped). */
export function paid(info) {
  try {
    const room = globalThis.__backroom;
    const state = room && room.state;
    if (!bankCardWanted(info, state, shown)) return;
    const doc = globalThis.document;
    if (!doc || !doc.body) return;
    shown = true;                                    // once per visit, whatever happens next
    loadEndCardCss(doc);
    const card = buildEndCard({ doc, line: BANK_CARD_LINE, plan: planOf(state.plans, 'basic'), feature: 'backroom_bank', cls: 'br-bank-card' });
    const close = doc.createElement('button');
    close.type = 'button'; close.className = 'br-bank-card-close'; close.setAttribute('aria-label', 'Close'); close.textContent = '×';
    close.addEventListener('click', hide);
    card.append(close);
    card.querySelector('.br-end-card-btn')?.addEventListener('click', () => setTimeout(hide, 0));
    doc.body.append(card);
    node = card;
    requestAnimationFrame(() => { if (node === card) card.classList.add('is-in'); });
    timer = setTimeout(hide, BANK_CARD_MS);
    postFunnel('gate_shown', 'backroom_bank', 'basic');
  } catch (e) { /* the bank never depends on its card */ }
}

/** A new bank run started (the next spin, the next hand): the card leaves. */
export function start() { hide(); }

/** Tests only: forget the once-per-visit latch. */
export function _resetForTest() { shown = false; hide(); }
