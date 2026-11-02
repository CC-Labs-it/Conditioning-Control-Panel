/* host/hostEscape.js - the Escape the panel kept reaches the page once.
 * Lifted (copied, not imported) from dtrh/race/hostEscape.js; each tree ships alone.
 *
 * Escape is the default panic key and the panel's keyboard hook sees every press. With
 * the race in front it keeps a first Escape as the game's pause (a second within 2 s is a
 * full panic, host side). While the WebView2 is out of keyboard focus the page never sees
 * that keydown, so the host hands the kept press over as { type: 'kept-escape' } and this
 * plays it as the page's own Escape: one keydown at the focused element. A focused page
 * hears the real key too, in either order, so one press is handled once: the frame is
 * dropped when a real Escape came within LOOK_BACK_MS before it, and it waits
 * LOOK_AHEAD_MS before it plays in case the real key is still on its way.
 * Escape only pauses here; nothing ever leaves on it. Make this before the page's own
 * key listeners: its capture listener hears every real Escape first.
 */

export const HOST_ESCAPE = 'kept-escape';
export const LOOK_BACK_MS = 1500;
export const LOOK_AHEAD_MS = 300;

const escapeKeydown = () => new KeyboardEvent('keydown', {
  key: 'Escape', code: 'Escape', keyCode: 27, which: 27, bubbles: true, cancelable: true, composed: true,
});

export function createHostEscape({
  win = globalThis.window,
  doc = globalThis.document,
  now = () => performance.now(),
  later = setTimeout,
  cancel = clearTimeout,
  makeEvent = escapeKeydown,
} = {}) {
  let lastReal = -Infinity;   // when a real Escape last reached the page
  let waiting = 0;            // the kept press, until LOOK_AHEAD_MS says no real key is coming
  let playing = false;        // our own keydown is not a real one

  const onKey = (e) => {
    if (playing || !e || e.key !== 'Escape') return;
    lastReal = now();
    if (waiting) { cancel(waiting); waiting = 0; }   // the real key got here after all: it is this press
  };
  win?.addEventListener?.('keydown', onKey, true);

  function play() {
    const target = doc?.activeElement || doc?.body;
    if (!target) return;
    playing = true;
    try { target.dispatchEvent(makeEvent()); }
    catch (e) { console.warn('[host-escape] kept Escape', e); }
    finally { playing = false; }
  }

  /** The host kept an Escape as the game's pause. True when it will play, false when dropped. */
  function kept() {
    const at = now();
    if (at - lastReal < LOOK_BACK_MS) return false;   // the page already had this press
    if (waiting) return false;                          // one kept press at a time
    waiting = later(() => {
      waiting = 0;
      if (lastReal >= at) return;
      play();
    }, LOOK_AHEAD_MS);
    return true;
  }

  function dispose() {
    if (waiting) { cancel(waiting); waiting = 0; }
    win?.removeEventListener?.('keydown', onKey, true);
  }

  return { kept, dispose };
}
