/* ============================================================================
 * door/wardrobe.js - the menu's Wardrobe screen: what your men wear.
 *
 * One row per slot (clothes, banner, insignia). Owned items are pills you can
 * pick; the rest stay visible, locked, with where to get them ("In the Prize
 * Counter"). A hosted page has the counter button, which asks the host to open
 * the Prize Counter on the chess tab; the host re-sends the grants when it
 * closes and the screen repaints with whatever was bought.
 *
 * The door owns the card; this file only draws the screen's body and reads the
 * one press it adds (`wear`, data-id "slot:itemId", an empty id = none).
 * ==========================================================================*/

import { SLOTS, CATALOG, NAMES, SLOT_WORDS, PRIME_ONLY } from '../skins/look.js';

export const LOCKED_NOTE = 'In the Prize Counter';

/**
 * @param wear   skins/wear.js
 * @param kit    the door's helpers: { esc, bar, bigBtn, hosted }
 */
export function wardrobeHtml(wear, kit) {
  const { esc, bar, bigBtn, hosted } = kit;
  const owned = wear ? wear.owned() : new Set();
  const picked = wear ? wear.picked() : {};
  const rows = SLOTS.map((slot) => {
    const cur = owned.has(picked[slot]) ? picked[slot] : null;
    const none = `<button type="button" class="level-pill${cur === null ? ' on' : ''}" data-act="wear" data-id="${slot}:" aria-pressed="${cur === null}">None</button>`;
    const pills = CATALOG[slot].map((id) => {
      const name = esc(NAMES[id] || id);
      if (owned.has(id)) {
        return `<button type="button" class="level-pill${cur === id ? ' on' : ''}" data-act="wear" data-id="${slot}:${esc(id)}" aria-pressed="${cur === id}">${name}</button>`;
      }
      const prime = PRIME_ONLY.includes(id) ? ' &middot; Prime' : '';
      return `<button type="button" class="level-pill locked" disabled aria-disabled="true" title="${LOCKED_NOTE}">${name}<span class="ward-lock">${LOCKED_NOTE}${prime}</span></button>`;
    }).join('');
    return `<div class="setup-row ward-row"><span class="setup-k">${SLOT_WORDS[slot]}</span><div class="level-row" role="group" aria-label="${SLOT_WORDS[slot]}">${none}${pills}</div></div>`;
  }).join('');
  const counter = hosted ? bigBtn('counter', 'go', 'Prize Counter', 'more for your men', 'slim') : '';
  const empty = owned.size ? '' : '<p class="door-sub">Nothing owned yet. Your men play as they are.</p>';
  return `
    ${bar('Wardrobe', 'Back', '<span class="door-count-k">your side</span>')}
    <div class="wardrobe">${rows}</div>
    ${empty}
    ${counter}`;
}

/** Split a pill's data-id into [slot, itemId|null]; junk answers null. */
export function readPick(dataId) {
  const s = String(dataId || '');
  const i = s.indexOf(':');
  if (i < 0) return null;
  const slot = s.slice(0, i), id = s.slice(i + 1) || null;
  if (!SLOTS.includes(slot)) return null;
  if (id !== null && !CATALOG[slot].includes(id)) return null;
  return [slot, id];
}
