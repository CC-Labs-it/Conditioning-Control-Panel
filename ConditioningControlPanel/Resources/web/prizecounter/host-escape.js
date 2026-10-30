/* host-escape.js - the Escape the panel kept reaches the counter once (copy of backroom/shared/host-escape.js;
 * each tree ships alone on the web, so each keeps its own).
 *
 * With the counter in front and the panic key on Escape, the panel keeps a first Escape as the page's back
 * (PanicPolicy, a second within 2 s is a full panic) and posts { type: 'kept-escape' }
 * (PrizeCounterHostService.PostKeptEscape). While the WebView2 is out of keyboard focus the real keydown never
 * comes, so this plays the kept press as one keydown at the focused element. A focused page hears the real key
 * too, in either order, so the frame is dropped when a real Escape came within LOOK_BACK_MS before it or
 * LOOK_AHEAD_MS after it. Make it before the page's own listeners (its capture listener hears every real Escape). */

export const HOST_ESCAPE = 'kept-escape';
export const LOOK_BACK_MS = 1500;
export const LOOK_AHEAD_MS = 300;

const escapeKeydown = () => new KeyboardEvent('keydown', {
  key: 'Escape', code: 'Escape', keyCode: 27, which: 27, bubbles: true, cancelable: true, composed: true,
});

export function createHostEscape({ win = globalThis.window, doc = globalThis.document, now = () => performance.now(),
  later = setTimeout, cancel = clearTimeout, makeEvent = escapeKeydown } = {}) {
  let lastReal = -Infinity, waiting = 0, playing = false;
  const onKey = (e) => {
    if (playing || !e || e.key !== 'Escape') return;
    lastReal = now();
    if (waiting) { cancel(waiting); waiting = 0; }
  };
  win?.addEventListener?.('keydown', onKey, true);
  function play() {
    const target = doc?.activeElement || doc?.body;
    if (!target) return;
    playing = true;
    try { target.dispatchEvent(makeEvent()); } catch (e) { /* page gone */ } finally { playing = false; }
  }
  /** The host kept an Escape as the page's back. True when it will play, false when dropped. */
  function kept() {
    const at = now();
    if (at - lastReal < LOOK_BACK_MS || waiting) return false;
    waiting = later(() => { waiting = 0; if (lastReal < at) play(); }, LOOK_AHEAD_MS);
    return true;
  }
  function dispose() { if (waiting) { cancel(waiting); waiting = 0; } win?.removeEventListener?.('keydown', onKey, true); }
  return { kept, dispose };
}
