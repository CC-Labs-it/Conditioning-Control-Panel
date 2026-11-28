/* ============================================================================
 * door/wardrobe.js - the menu's Wardrobe screen: what your men wear.
 *
 * One row per slot. Piece finishes appear when the player owns one. Other owned items are pills you can
 * pick; the rest stay visible, locked, with where to get them ("In the Prize
 * Counter"). A hosted page has the counter button, which asks the host to open
 * the Prize Counter on the chess tab; the host re-sends the grants when it
 * closes and the screen repaints with whatever was bought. A host that says
 * `pbp:settings.prizeCounter: false` (the web shim while the site's counter
 * door is shut) gets no button; the desktop never sends it and keeps it.
 *
 * The door owns the card; this file only draws the screen's body and reads the
 * one press it adds (`wear`, data-id "slot:itemId", an empty id = none).
 * ==========================================================================*/

import { SLOTS, CATALOG, NAMES, SLOT_WORDS, PRIME_ONLY } from '../skins/look.js';

export const LOCKED_NOTE = 'In the Prize Counter';

const DAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

/**
 * The lock note for an item the player does not own. The host's `pbp:stock` frame says which rows are
 * out of this week's Prize Counter stock and the Monday (UTC) they are back (ccp-bugs #1738): those
 * read "Back in the Prize Counter on Mon 30 Nov". A date that has come (the window was left open over
 * the week's turn) or junk falls back to the plain note.
 */
export function lockNote(from, now = Date.now()) {
  const at = typeof from === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(from) ? Date.parse(from + 'T00:00:00Z') : NaN;
  if (!Number.isFinite(at) || at <= now) return LOCKED_NOTE;
  const d = new Date(at);
  return `Back in the Prize Counter on ${DAYS[d.getUTCDay()]} ${d.getUTCDate()} ${MONTHS[d.getUTCMonth()]}`;
}

/**
 * @param wear   skins/wear.js
 * @param kit    the door's helpers: { esc, bar, bigBtn, hosted, counter, now } (counter: false hides the button;
 *               now: a clock for tests)
 */
export function wardrobeHtml(wear, kit) {
  const { esc, bar, bigBtn, hosted, counter: counterOpen } = kit;
  const now = typeof kit.now === 'number' ? kit.now : Date.now();
  const owned = wear ? wear.owned() : new Set();
  const picked = wear ? wear.picked() : {};
  const rows = SLOTS.map((slot) => {
    const items = ['finish', 'board', 'companion'].includes(slot) ? CATALOG[slot].filter((id) => owned.has(id)) : CATALOG[slot];
    if (!items.length) return '';
    const cur = owned.has(picked[slot]) ? picked[slot] : null;
    const none = `<button type="button" class="level-pill${cur === null ? ' on' : ''}" data-act="wear" data-id="${slot}:" aria-pressed="${cur === null}">None</button>`;
    const pills = items.map((id) => {
      const name = esc(NAMES[id] || id);
      if (owned.has(id)) {
        return `<button type="button" class="level-pill${cur === id ? ' on' : ''}" data-act="wear" data-id="${slot}:${esc(id)}" aria-pressed="${cur === id}">${name}</button>`;
      }
      const prime = PRIME_ONLY.includes(id) ? ' &middot; Prime' : '';
      const note = lockNote(wear && wear.backOn ? wear.backOn(id) : null, now);
      return `<button type="button" class="level-pill locked" disabled aria-disabled="true" title="${note}">${name}<span class="ward-lock">${note}${prime}</span></button>`;
    }).join('');
    return `<div class="setup-row ward-row"><span class="setup-k">${SLOT_WORDS[slot]}</span><div class="level-row" role="group" aria-label="${SLOT_WORDS[slot]}">${none}${pills}</div></div>`;
  }).join('');
  const counter = hosted && counterOpen !== false ? bigBtn('counter', 'go', 'Prize Counter', 'more for your men', 'slim') : '';
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
