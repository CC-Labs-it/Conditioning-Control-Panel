/* ui/shop-next-model.js - what the "Next up" strip under the shop shows (owner go 2026-11-10, "Even beat"): up to three
 * ghost tiles of what the shop offers after the tiles on show. Pure, so tests need no page.
 *   Journey shop: the planner's own look-ahead (sim/journey.js peek).
 *   Plain shop:   open rows waiting behind the window (cheapest first), then the main path's row after the goal.
 * A fork shows as a signpost and an ending as an unknown tile; nothing past either is shown, so no choice is spoiled. */

import { ITEMS } from '../data/items.js';
import * as E from '../sim/economy.js';

export const NEXT_MAX = 3;

/** Plain words for the two tiles that keep their secret. */
export const SECRET = Object.freeze({
  fork: { name: 'A choice', what: 'The road splits here. You pick when you get there.' },
  end: { name: 'An ending', what: 'How this run ends stays a secret until you reach it.' },
});

/** The main path's row after the goal, or null. */
export function trunkAfter(s, goalId) {
  let seen = !goalId;
  for (const it of ITEMS) {
    if (it.lane !== 'trunk' || it.group !== 'run' || E.has(s, it.id)) continue;
    if (!seen) { if (it.id === goalId) seen = true; continue; }
    return it;
  }
  return null;
}

/** Look-ahead for the plain shop. `win` is what the shop shows now ({ goal, rows }). */
export function plainPeek(s, win, n = NEXT_MAX, mods = null) {
  if (s.ended) return [];
  const shown = new Set([win?.goal?.id, ...(win?.rows || []).map((it) => it.id)].filter(Boolean));
  const out = E.openItems(s, mods).filter((it) => !shown.has(it.id) && it.kind !== 'fork' && it.kind !== 'end').slice(0, n);
  const after = win?.goal ? trunkAfter(s, win.goal.id) : null;
  if (after && out.length < n && !shown.has(after.id) && !out.includes(after)) out.push(after);
  return out;
}

/** The strip's tiles from a look-ahead list: { key, kind: 'item' | 'fork' | 'end', item }. At most n, device rows this
 *  page cannot use left out, and the list ends at the first fork or ending. */
export function ghostsFor(list, n = NEXT_MAX, canOffer = () => true) {
  const out = [];
  for (const item of list || []) {
    if (!item || out.length >= n) break;
    const kind = item.kind === 'fork' ? 'fork' : item.kind === 'end' ? 'end' : 'item';
    if (kind === 'item' && !canOffer(item)) continue;
    out.push({ key: kind === 'item' ? item.id : 'next-' + kind, kind, item });
    if (kind !== 'item') break;
  }
  return out;
}

/** One string per strip, so the shop rebuilds the strip only when what it shows changes. */
export const stripKey = (ghosts) => ghosts.map((g) => g.key).join('|');
