/* ui/pop-picture.js - the one rule for a picture a power-up pops over the play area (owner 2026-11-08): it never takes a click
 * and it draws at 80%, so play carries on underneath and through it. Mark the overlay's root: styles.css makes the root and
 * everything in it click-through and draws its img/video at POP_PICTURE_OPACITY. A canvas print multiplies its own fade by the
 * same number (ui/unlocks/arcade/slot-flight.js). Credits ("via Scrolller") stay at full strength.
 * Not for pictures that are part of a control (Picture Frame, Wall of Frames, reels, wheel wedges, Deep End, Breakout, race). */

export const POP_PICTURE_OPACITY = 0.8;
export const POP_PICTURE_ATTR = 'data-pop-picture';

export function markPopPicture(el) {
  el.setAttribute(POP_PICTURE_ATTR, '');
  el.style.pointerEvents = 'none';
  return el;
}
