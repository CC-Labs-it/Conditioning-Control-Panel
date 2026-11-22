/* ui/shop-locked.js - the small edition's one locked tile (lane S, owner 2026-11-22: "one tile, never one per cut item").
 * A greyed tile at the very end of the shop grid (ui/shop.js shopDoor.tail), in the shop's own look: a `.row.short`
 * like any row the player cannot afford yet, with a lock where the icon goes. Its press sends the `locked-tile` nudge
 * from the player's own press. It is never an engine row: not in the sim, the Journey, the Guide map, the sticker or
 * recap counts. It never takes focus: nothing here focuses it, and it sits after every row. main.js mounts it in small
 * only (ui/edition.js isSmall); nothing here reads the edition.
 *
 * COPY: APPROVED AND FROZEN (owner 2026-11-22, fourth dialog), pinned in tests/editions-placeholders.test.mjs. */

import { sendNudge } from './edition.js';
import { realPress } from './nudges.js';
import { shopDoor } from './shop.js';

export const LOCKED_TILE_COPY = Object.freeze({ label: 'More in the full game' });

const LOCK = '<svg viewBox="0 0 20 20" width="30" height="30" aria-hidden="true"><g fill="none" stroke="currentColor" stroke-width="2" '
  + 'stroke-linecap="round" stroke-linejoin="round"><rect x="4" y="9" width="12" height="8.5" rx="1.5"/><path d="M6.5 9V6.5a3.5 3.5 0 0 1 7 0V9"/></g></svg>';

/** Builds the tile and hands it to the shop. door: the live shop (ui/shop.js shopDoor); none yet = nothing shown.
 *  Answers the tile, or null. */
export function mountLockedTile({ doc = document, win = doc.defaultView, send = () => {}, ed, door = shopDoor } = {}) {
  if (!door?.tail || doc.getElementById('locked-tile')) return null;
  const el = doc.createElement('button');
  el.type = 'button';
  el.id = 'locked-tile';
  el.className = 'row short locked-tile';
  el.dataset.lockedTile = '';
  el.setAttribute('aria-label', LOCKED_TILE_COPY.label);
  const ico = doc.createElement('span');
  ico.className = 'ico';
  ico.innerHTML = LOCK;
  const label = doc.createElement('span');
  label.className = 'cost';
  label.style.whiteSpace = 'normal';
  label.textContent = LOCKED_TILE_COPY.label;
  el.append(ico, label);
  el.addEventListener('click', (ev) => sendNudge('locked-tile', { inPress: realPress(win, ev), send, win, ed }));
  door.tail(el);
  return el;
}
