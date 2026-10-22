/* ============================================================================
 * door/back-button.js - the one Back button every front-door screen wears.
 *
 * Owner, 2026-10-14: "add an esc button clickable on the screens, every time
 * atm I gotta press ESC". So every screen that used to say "esc - back" in
 * small print carries this instead: top-left of the panel, an arrow and a
 * word, a real <button> (Tab reaches it, Enter and Space press it). Escape
 * keeps doing what it always did; the button is the same step for a mouse.
 *
 *   backButton(label = 'Back')   -> the HTML string (drop it first in the panel)
 *   wireBack(root, onBack)       -> one delegated click listener on root for
 *                                   every Back inside it, now and after any
 *                                   re-render; returns the unwire function
 *
 * The press plays ui.back(); the squash and lift are door.css (.door-back).
 * The ladder and the pictures screens use the same pair (sound lane).
 * ==========================================================================*/

import { ui } from '../audio/ui-cues.js';

const ARROW = '<svg class="door-back-ico" viewBox="0 0 20 20" aria-hidden="true" focusable="false">'
  + '<path d="M11.5 4.5 6 10l5.5 5.5" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"/></svg>';

function esc(s) { return String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])); }

/** The button itself. `label` is one short word: Back, Leave, Menu, Cancel. */
export function backButton(label = 'Back') {
  const word = esc(label || 'Back');
  return `<button type="button" class="door-back" data-back aria-label="${word} (Esc)" title="${word} (Esc)">${ARROW}<span>${word}</span></button>`;
}

/**
 * Every [data-back] inside root calls onBack(event). Delegated, so a screen
 * that re-renders its innerHTML keeps working with no second wire.
 */
export function wireBack(root, onBack) {
  if (!root || typeof root.addEventListener !== 'function') return () => {};
  const onClick = (e) => {
    const b = e.target && e.target.closest ? e.target.closest('[data-back]') : null;
    if (!b || !root.contains(b) || b.disabled) return;
    e.preventDefault();
    e.stopPropagation();
    try { ui.back(); } catch { /* sound is optional */ }
    try { onBack(e); } catch (err) { console.warn('[pbp] back', err); }
  };
  root.addEventListener('click', onClick);
  return () => root.removeEventListener('click', onClick);
}

export default backButton;
