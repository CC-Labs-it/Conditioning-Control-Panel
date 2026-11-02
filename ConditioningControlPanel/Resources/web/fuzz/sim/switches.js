/* sim/switches.js - what the Control Panel may switch. Off takes a row's unlock off the screen and out of the sound; what the
 * row itself pays per second or per tap stays in the sim, so tidying up is never a punishment. Forks, endings, the moods,
 * `start` and the panel itself are never listed. Wake Up and panic are not rows, and nothing here reaches them. */

import { BY_ID, LANES } from '../data/items.js';

export const switchable = (it) => !!it && it.kind === 'item' && it.group === 'run' && it.id !== 'start' && it.id !== 'panel';

/** The panel's list: owned rows that have something to switch, by lane in the game's lane order, then in the order bought. */
export function panelGroups(run, hasEntry = () => true) {
  const rows = run.order.map((id) => BY_ID[id]).filter((it) => switchable(it) && hasEntry(it.id));
  return LANES
    .map((lane) => ({ lane: lane.id, name: lane.name, rows: rows.filter((it) => it.lane === lane.id) }))
    .filter((g) => g.rows.length);
}

/** The off list from a save: only rows it could really be. */
export function cleanOff(src, owned) {
  const off = {};
  if (src && typeof src === 'object' && !Array.isArray(src)) {
    for (const id of Object.keys(src)) if (switchable(BY_ID[id]) && owned[id]) off[id] = 1;
  }
  return off;
}
