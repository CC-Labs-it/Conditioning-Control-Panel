/* ui/unlocks/fate/museum/lines.js - pure helpers for the Gooning Museum cards: the ticket's serial and date, and placard
 * lines from live numbers. No DOM, no ctx.rng: callers pass their own pick (Math.random). */

import { PLACARDS } from '../../../../data/content/museum.js';

const MONTHS = ['JAN', 'FEB', 'MAR', 'APR', 'MAY', 'JUN', 'JUL', 'AUG', 'SEP', 'OCT', 'NOV', 'DEC'];

/** Six digits from the run seed, the same for the whole run. */
export function ticketSerial(seed) {
  const n = Math.imul((Number(seed) >>> 0) ^ 0x9e3779b9, 2654435761) >>> 0;
  return String(n % 1000000).padStart(6, '0');
}

/** "10 NOV 2026", no locale. */
export function ticketDate(date = new Date()) {
  const d = date instanceof Date && !Number.isNaN(date.getTime()) ? date : new Date();
  return `${d.getDate()} ${MONTHS[d.getMonth()]} ${d.getFullYear()}`;
}

/** Fills {field}s from `values`. Null when a field is missing or empty, so the caller tries another line. */
export function fill(template, values) {
  let ok = true;
  const out = String(template).replace(/\{(\w+)\}/g, (_, key) => {
    const v = values?.[key];
    if (v == null || v === '') { ok = false; return ''; }
    return String(v);
  });
  return ok ? out : null;
}

/** The numbers a placard may print, already formatted. Counts under two step aside ("Pressed 1 times" never shows). */
export function placardValues(state, fmt = (n) => String(Math.round(n)), cur = '') {
  const s = state || {};
  const num = (v) => { const n = Math.floor(Number(v)); return Number.isFinite(n) && n >= 2 ? fmt(n) : null; };
  return { taps: num(s.taps), fuzz: num(s.fuzz), buys: num(s.buys), minutes: num((Number(s.clock) || 0) / 60000), cur };
}

/** One placard line for a kind (button, counter, object). `index` picks the template; a line that cannot be filled
 *  falls through to the next one, and the last resort is the plain catalogue line. */
export function placardLine(kind, index, values, templates = PLACARDS) {
  const list = templates[kind] || templates.object;
  for (let k = 0; k < list.length; k++) {
    const line = fill(list[(Math.abs(Math.floor(index)) + k) % list.length], values);
    if (line) return line;
  }
  return kind === 'object' ? 'Unknown object. Donated by the visitor.' : `${kind[0].toUpperCase()}${kind.slice(1)}.`;
}

/** Placards the page shows at once: the button, the counter, then dock objects. Phones keep it to three. */
export const placardCap = (lite) => (lite ? 3 : 5);

