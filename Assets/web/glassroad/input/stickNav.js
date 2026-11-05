/* input/stickNav.js - the left stick and the d-pad as card navigation, pure (no DOM; tests/stick-nav.test.mjs).
 *
 * The stick counts once its dominant axis passes the dead zone (0.5); it lets go below 0.35, so a stick
 * hovering at the line never chatters. A diagonal keeps the direction it started with until it lets go,
 * so a wobble across the corner never skips a row. The first step lands on the crossing, then the held
 * direction repeats after 400 ms every 120 ms, like a held arrow key. The d-pad takes the same rule and
 * wins over the stick. block() makes a held direction wait for a release first: boot calls it on every
 * frame no card is open, so a stick held for steering never walks the pause card the moment it opens.
 */
export const NAV_DEADZONE = 0.5;
export const NAV_RELEASE = 0.35;
export const NAV_DELAY_MS = 400;
export const NAV_REPEAT_MS = 120;

export function createStickNav({ deadzone = NAV_DEADZONE, release = NAV_RELEASE, delayMs = NAV_DELAY_MS, repeatMs = NAV_REPEAT_MS } = {}) {
  let held = null;        // the direction counted now, or null at rest
  let nextAt = 0;         // when the held direction steps again
  let blocked = false;    // a held direction waits for a release

  function stickDir(x, y) {
    const ax = Math.abs(x) || 0, ay = Math.abs(y) || 0;
    if (held === 'up' || held === 'down' || held === 'left' || held === 'right') {
      const along = held === 'up' ? -y : held === 'down' ? y : held === 'left' ? -x : x;
      if (along >= release) return held;          // still leaning the same way: keep it, never flip on a diagonal
    }
    if (Math.max(ax, ay) < deadzone) return null;
    return ay >= ax ? (y < 0 ? 'up' : 'down') : (x < 0 ? 'left' : 'right');
  }

  /**
   * One frame. pad = { up, down, left, right } (the d-pad), x / y = the left stick. Returns a direction
   * for this frame (the crossing or a repeat) or null.
   */
  function step(pad, x, y, nowMs) {
    const dpad = pad && (pad.up ? 'up' : pad.down ? 'down' : pad.left ? 'left' : pad.right ? 'right' : null);
    const dir = dpad || stickDir(x, y);
    if (!dir) { held = null; blocked = false; return null; }
    if (blocked) { held = dir; return null; }
    if (dir !== held) { held = dir; nextAt = nowMs + delayMs; return dir; }
    if (nowMs >= nextAt) { nextAt = nowMs + repeatMs; return dir; }
    return null;
  }

  return {
    step,
    /** A held direction waits for a release before it counts again. */
    block() { blocked = true; },
    reset() { held = null; blocked = false; nextAt = 0; },
    get held() { return held; },
  };
}
