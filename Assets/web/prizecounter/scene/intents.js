/* intents.js - keys, pad buttons and swipes turned into intents. Pure: no DOM, no three.
 * The scene only reports; the state model (B3-W1) decides where focus goes. */

const KEYS = {
  ArrowLeft: 'left', KeyA: 'left', ArrowRight: 'right', KeyD: 'right',
  ArrowUp: 'up', KeyW: 'up', ArrowDown: 'down', KeyS: 'down',
  KeyQ: 'tabPrev', KeyE: 'tabNext', Enter: 'select', NumpadEnter: 'select', Space: 'select', Escape: 'back',
};
const BY_KEY = { a: 'left', d: 'right', w: 'up', s: 'down', q: 'tabPrev', e: 'tabNext', ' ': 'select', Esc: 'back' };
export const DIRECTIONS = new Set(['left', 'right', 'up', 'down']);

// From a KeyboardEvent-like { code, key, repeat }; null when the key means nothing here. Held keys repeat
// directions only: a held Enter never buys twice.
export function keyIntent({ code, key, repeat } = {}) {
  const type = KEYS[code] || KEYS[key] || BY_KEY[String(key || '').toLowerCase()] || BY_KEY[key] || null;
  if (!type) return null;
  if (repeat && !DIRECTIONS.has(type)) return null;
  return { type };
}

// Standard gamepad mapping: A 0, B 1, LB 4, RB 5, d-pad 12-15.
const PAD = { 0: 'select', 1: 'back', 4: 'tabPrev', 5: 'tabNext', 12: 'up', 13: 'down', 14: 'left', 15: 'right' };
export const padIntent = (index) => (PAD[index] ? { type: PAD[index] } : null);

// Left stick past the dead zone, the stronger axis wins.
export function stickIntent(x, y, dead = 0.55) {
  if (Math.max(Math.abs(x), Math.abs(y)) < dead) return null;
  if (Math.abs(x) >= Math.abs(y)) return { type: x < 0 ? 'left' : 'right' };
  return { type: y < 0 ? 'up' : 'down' };
}

/* A touch swipe drags the shelves like a sheet: swipe left shows what is to the right, swipe up shows the row
 * below. Under `min` pixels it is a tap, not a swipe (null). */
export function swipeIntent(dx, dy, min = 40) {
  if (Math.hypot(dx, dy) < min) return null;
  if (Math.abs(dx) >= Math.abs(dy)) return { type: dx < 0 ? 'right' : 'left' };
  return { type: dy < 0 ? 'down' : 'up' };
}
