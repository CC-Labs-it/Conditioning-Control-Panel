/* ui/shop-locked.js - the small edition's one locked tile (lane S, owner 2026-11-22: "one tile, never one per cut item").
 * A greyed tile at the very end of the shop grid (ui/shop.js shopDoor.tail), in the shop's own look: a `.row.short`
 * like any row the player cannot afford yet, with a lock where the icon goes. Its press sends the `locked-tile` nudge
 * from the player's own press. It is never an engine row: not in the sim, the Journey, the Guide map, the sticker or
 * recap counts. It never takes focus: nothing here focuses it, and it sits after every row. main.js mounts it in small
 * only (ui/edition.js isSmall); nothing here reads the edition.
 *
 * COPY: APPROVED AND FROZEN (owner 2026-11-22, fourth dialog), pinned in tests/editions-placeholders.test.mjs. */

import { sendNudge } from './edition.js';
import { realPress, sendNudgeAs } from './nudges.js';
import { shopDoor } from './shop.js';
import { BY_ID } from '../data/items.js';
import { iconFor } from './icons.js';
import { fateCss } from './fate-look.js';
import { lockedSets, endingName } from './small-funnel.js';
import { LOCKED_SEED_COPY } from '../data/content/small-funnel.js';

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
  /* The funnel (owner 2026-11-24): the endings small keeps locked sit on the seed shelf beside the open seed. */
  door.lockedSeeds?.((run) => lockedSets(run).map((set) => ({ id: set.id, seed: set.seed, make: () => makeLockedSeed({ doc, win, send, ed, set }) })));
  return el;
}

/* ---- locked seeds (owner 2026-11-24, handoff-1124/tc-small-funnel/BRIEF.md) ----
 * A fate set a full run would offer here and small keeps locked: its seed card in the fate look (black notched card,
 * ui/fate-look.js), dimmed, a lock where the END seal goes, a FULL GAME stamp where NOT THIS RUN goes, and the ending's
 * name. Its press sends nudge {where:'locked-tile', want:'upgrade'} from a real press. Never an engine row: no data-id,
 * no price, nothing counts it. The shop (ui/shop.js lockedSeeds) keeps it after the open seeds and lets it go with them.
 * COPY: drafts, data/content/small-funnel.js. */
export const LOCKED_SEED_CSS = `
#shop .row.fate.fate-locked { cursor: pointer; }
#shop .row.fate.fate-locked .ico { opacity: .34; filter: grayscale(1) drop-shadow(1px 0 0 var(--fk)) drop-shadow(-1px 0 0 var(--fk)); }
#shop .row.fate.fate-locked .cost { white-space: normal; line-height: 1.05; font-size: 12px; color: var(--fk); }
#shop .row.fate.fate-locked::after { opacity: .2; }
#shop .row.fate.fate-locked .fl-lock { position: absolute; right: 5px; top: 5px; z-index: 2; width: 28px; height: 28px; border-radius: 50%;
  display: grid; place-items: center; background: var(--fk); color: var(--fc); box-shadow: 0 0 0 2px var(--fc); rotate: -10deg; }
#shop .row.fate.fate-locked .fl-lock svg { width: 16px; height: 16px; }
#shop .row.fate.fate-locked .fl-stamp { position: absolute; left: 50%; top: 42%; z-index: 3; translate: -50% -50%; rotate: -12deg;
  border: 2px dashed var(--fk); color: var(--fk); background: var(--fc); font: 700 9px/1.1 "Courier New", monospace; letter-spacing: .08em;
  padding: 2px 4px; white-space: nowrap; pointer-events: none; }
#shop .row.fate.fate-locked:hover { --frame: var(--fk); }
#shop .row.fate.fate-locked:hover .ico { opacity: .55; }
#shop .row.fate.fate-locked:active { translate: 0 2px; }
@media (prefers-reduced-motion: reduce) { #shop .row.fate.fate-locked:active { translate: none; } }
body.gentle #shop .row.fate.fate-locked:active { translate: none; }
`;

/** One locked seed card. Answers the element. */
export function makeLockedSeed({ doc = document, win = doc.defaultView, send = () => {}, ed, set }) {
  fateCss(doc, 'locked-seed-css', LOCKED_SEED_CSS);
  const name = endingName(set.end);
  const el = doc.createElement('button');
  el.type = 'button';
  el.className = 'row fate fate-seed short fate-locked';
  el.dataset.lockedSeed = set.id;
  el.setAttribute('aria-label', `${name}. ${LOCKED_SEED_COPY.aria}`);
  const seed = BY_ID[set.seed];
  let art;
  try { art = iconFor(doc, seed || { id: set.seed }); } catch { art = doc.createElement('span'); art.className = 'ico'; }
  const label = doc.createElement('span');
  label.className = 'cost';
  label.textContent = name;
  const lock = doc.createElement('i');
  lock.className = 'fl-lock';
  lock.setAttribute('aria-hidden', 'true');
  lock.innerHTML = LOCK;
  const stamp = doc.createElement('i');
  stamp.className = 'fl-stamp';
  stamp.setAttribute('aria-hidden', 'true');
  stamp.textContent = LOCKED_SEED_COPY.stamp;
  el.append(art, label, lock, stamp);
  el.addEventListener('click', (ev) => sendNudgeAs('locked-tile', 'upgrade', { inPress: realPress(win, ev), send, win, ed }));
  return el;
}
