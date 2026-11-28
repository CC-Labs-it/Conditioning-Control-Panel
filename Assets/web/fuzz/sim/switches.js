/* sim/switches.js - what the Control Panel may switch. Off takes a row's unlock off the screen and out of the sound; what the
 * row itself pays per second or per tap stays in the sim, so tidying up is never a punishment. Forks, endings, the moods,
 * `start` and the panel itself are never listed. Wake Up and panic are not rows, and nothing here reaches them. */

import { BY_ID, ITEMS, LANES } from '../data/items.js';

/* Rows that build on a parent's live state rather than on the screen alone: the feed's hooks, the frame the captions write in,
 * EMI herself, the hum. They cannot come and go alone, so a switch in a family remounts the family, and with the parent off
 * its children wait. Their own switches keep what the player chose. */
const laneFamily = (lane) => ITEMS.filter((it) => it.lane === lane && it.kind === 'item').map((it) => it.id);
const FAMILIES = [laneFamily('scroll'), laneFamily('blind'), [...laneFamily('emi'), 'quiz'], ['a_hum', 'a_phones', 'a_bin']];
const FAMILY_OF = new Map(FAMILIES.flatMap((f) => f.map((id) => [id, f])));

// These controls detach independently. Switching their parent still remounts the whole family.
export const familyOf = (id) => id === 'quiz' || id === 'd_autoscroll' ? [id] : FAMILY_OF.get(id) || null;
export const parentOf = (id) => { const f = FAMILY_OF.get(id); return f && f[0] !== id ? f[0] : null; };
/** Should this row be mounted? Its own switch is on and its family's parent is too. */
export const liveOn = (off, id) => !off[id] && !off[parentOf(id)];

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
