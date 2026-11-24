/* ui/small-funnel.js - what the small edition shows locked, as plain data (owner 2026-11-24,
 * handoff-1124/tc-small-funnel/BRIEF.md). No DOM: the seed shelf (ui/shop-locked.js) and the recap wall
 * (ui/recap/wall.js) draw from it, and tests read it in node. Nothing here reads the edition: the callers are mounted in
 * small only. Every number comes from the data (data/editions.js SMALL_CUT, the catalog, data/fate.js, data/prestige.js),
 * never typed by hand. */

import { BY_ID } from '../data/items.js';
import { FATE_SETS } from '../data/fate.js';
import { teasedSets } from '../sim/economy.js';
import { SMALL_CUT, SMALL_ENDING_SET } from '../data/editions.js';
import { LANES } from '../data/prestige.js';
import { brandText } from '../data/brand.js';
import { SMALL_WALL_COPY } from '../data/content/small-funnel.js';

/** The sets a full run with these picks would be offered that small keeps locked: [{ id, end, seed }], in
 *  data/fate.js order. Lane A's sim rule (sim/economy.js teasedSets): empty for a full run, the cut Expo never teased. */
export const lockedSets = (run) => teasedSets(run);

/** An ending's own name from the catalog ("Ending: EMI Takeover" -> "EMI Takeover"). */
export const endingName = (endId) => brandText(String(BY_ID[endId]?.name || endId).replace(/^Ending:\s*/, ''));

/** Rows of a SMALL_CUT group that are real catalog rows (a stale id is never counted). */
const cutCount = (group) => (SMALL_CUT[group] || []).filter((id) => BY_ID[id]).length;

/** The wall card's fan-out: the endings small never opens (every fate set but Go Outside, theme sets included), then the
 *  parts cut from small, each with its count. A part with nothing behind it stays out. */
export function wallModel() {
  const endings = FATE_SETS.filter((set) => set.id !== SMALL_ENDING_SET)
    .map((set) => ({ id: set.end, set: set.id, name: endingName(set.end) }));
  const P = SMALL_WALL_COPY.parts;
  const stars = LANES.reduce((n, lane) => n + (lane.stars?.length || 0), 0);
  const parts = [
    { key: 'arcade', n: cutCount('arcade'), icon: 'toy_box' },
    { key: 'quiz', n: cutCount('quiz'), icon: 'swap' },
    { key: 'popups', n: cutCount('popups'), icon: 'inbox' },
    { key: 'combos', n: cutCount('combos'), icon: 'x_dream' },
    { key: 'renewal', n: stars, icon: null },
    { key: 'expo', n: cutCount('expo'), icon: 'll_end' },
  ].filter((p) => p.n > 0).map((p) => ({ ...p, label: P[p.key].replace('{n}', String(p.n)) }));
  return { endings, parts };
}
