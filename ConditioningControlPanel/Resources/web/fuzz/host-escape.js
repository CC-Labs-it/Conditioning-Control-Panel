// Same kept-key deduplication as piecebypiece/ui/host-escape.js; this tree ships alone.
export const HOST_ESCAPE = 'kept-escape';
export const LOOK_BACK_MS = 1500, LOOK_AHEAD_MS = 300;
export function createHostEscape({ win = globalThis.window, doc = globalThis.document,
  now = () => performance.now(), later = setTimeout, cancel = clearTimeout,
  makeEvent = () => new KeyboardEvent('keydown', { key: 'Escape', code: 'Escape', keyCode: 27, which: 27, bubbles: true, cancelable: true, composed: true }),
} = {}) {
  let lastReal = -Infinity, waiting = 0, playing = false;
  const onKey = e => {
    if (playing || e?.key !== 'Escape') return;
    lastReal = now();
    if (waiting) { cancel(waiting); waiting = 0; }
  };
  win?.addEventListener?.('keydown', onKey, true);
  return {
    kept() {
      const at = now();
      if (at - lastReal < LOOK_BACK_MS || waiting) return false;
      waiting = later(() => {
        waiting = 0;
        if (lastReal >= at) return;
        playing = true;
        try { (doc?.activeElement || doc?.body)?.dispatchEvent(makeEvent()); }
        finally { playing = false; }
      }, LOOK_AHEAD_MS);
      return true;
    },
    dispose() { if (waiting) cancel(waiting); waiting = 0; win?.removeEventListener?.('keydown', onKey, true); },
  };
}
