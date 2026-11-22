/* ui/phone-caps.js - the small edition's hard caps on a phone (lane S5, owner 2026-11-22; numbers in data/editions.js
 * PHONE_CAPS, read only through ui/edition.js phoneCaps). Caps, not options: no setting turns them off.
 *
 * The cap is applied AT THE SPAWN, never in the sim: the run owns and pays for every row it bought. A phone draws fewer,
 * and each one it draws stands for its share of the rest: a wall hit of a drawn one pays `times` hits (engine.wallHit
 * `times`), so income is the same with and without the cap, and the badge on the first drawn one shows how many it is.
 * A row the player bought never looks like it did nothing. Pure rules first, so tests need no page. */

import { phoneCaps } from './edition.js';
import { runEdition } from '../sim/economy.js';

/** The caps for this run on this screen, or null (full edition, no reading, or not a phone). */
export const capsFor = (run, win = globalThis.window) => {
  const ed = runEdition(run);
  return ed ? phoneCaps(win, ed) : null;
};

/** owned of a kind, at most `cap` drawn: { drawn, times } with times = how many owned each drawn one stands for. */
export function capPlan(owned, cap) {
  const n = Math.max(0, Math.floor(Number(owned) || 0));
  if (cap == null || n <= cap) return { drawn: n, times: 1 };
  const drawn = Math.max(0, Math.floor(cap));
  return { drawn, times: drawn ? n / drawn : 0 };
}

/** The bouncing words of a run, in the order a phone keeps them: Bouncing Word first, then the two Bouncing Words. */
export const WORD_SLOTS = Object.freeze([['e_bounce', 0], ['e_words', 0], ['e_words', 1]]);

/** Which word slots draw here and what each drawn one stands for. has(id) answers whether the run owns that row. */
export function wordPlan(has, cap) {
  const owned = WORD_SLOTS.filter(([id]) => has(id));
  const { drawn, times } = capPlan(owned.length, cap);
  const keep = new Set(owned.slice(0, drawn).map(([id, i]) => id + ':' + i));
  return { drawn, times, owned: owned.length, draws: (id, i) => keep.has(id + ':' + i), first: owned[0] ? owned[0].join(':') : null };
}

/** The "stands for" badge: the first drawn copy shows the owned count when the phone draws fewer. A number, no words. */
export function markStandIn(el, { owned, drawn, first }) {
  if (!el?.dataset) return;
  if (first && owned > drawn) el.dataset.standsFor = '×' + owned;
  else delete el.dataset.standsFor;
}
export const STAND_IN_CSS = `[data-stands-for]::after { content: attr(data-stands-for); position: absolute; right: -6px; top: -8px;
  padding: 0 5px; border-radius: 8px; background: var(--stamp, #2a2430); color: #fff; font: 700 11px/16px system-ui; pointer-events: none; }`;
