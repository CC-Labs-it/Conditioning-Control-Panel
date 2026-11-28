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
/** Rows of one kind that each draw their own copy (picture frames, ad strips): a seat pool, first live wins.
 *  join({ draw }) returns a seat; draw() builds that row's copy and returns its element, and is called only when a place
 *  is free, so a capped row builds nothing at all. When a drawn row leaves (switched off, run over) the next waiting
 *  one draws. The first drawn copy wears the badge, and seat.times says how many owned rows each drawn one stands for.
 *  cap null: every row draws at once, as before. One pool per kind and page. */
const pools = new Map();
export function seatPool(kind, cap) {
  let pool = pools.get(kind);
  if (!pool) { pool = { seats: [] }; pools.set(kind, pool); }
  pool.cap = cap;
  const refresh = () => {
    const seats = pool.seats, max = pool.cap == null ? Infinity : pool.cap;
    let drawn = seats.filter((s) => s.drawn).length;
    for (const s of seats) {
      if (drawn >= max) break;
      if (s.drawn) continue;
      s.drawn = true; drawn += 1;
      s.el = s.draw() || null;
    }
    const lit = seats.filter((s) => s.drawn);
    const times = lit.length ? seats.length / lit.length : 0;
    lit.forEach((s, i) => { s.times = times; markStandIn(s.el, { owned: seats.length, drawn: lit.length, first: i === 0 }); });
    for (const s of seats) if (!s.drawn) s.times = 0;
  };
  return {
    join({ draw }) {
      const seat = { draw, drawn: false, el: null, times: 0 };
      seat.anchor = () => (seat.drawn ? seat.el : pool.seats.find((s) => s.drawn)?.el) || null;
      seat.leave = () => { const i = pool.seats.indexOf(seat); if (i >= 0) pool.seats.splice(i, 1); refresh(); };
      pool.seats.push(seat);
      refresh();
      return seat;
    },
    get size() { return pool.seats.length; },
  };
}
export const STAND_IN_CSS = `[data-stands-for]::after { content: attr(data-stands-for); position: absolute; right: -6px; top: -8px;
  padding: 0 5px; border-radius: 8px; background: var(--stamp, #2a2430); color: #fff; font: 700 11px/16px system-ui; pointer-events: none; }`;
