// Same handoff as chess: deduplicate either arrival order using native time.
export const HOST_ESCAPE = 'kept-escape';
export function createHostEscape({ win = globalThis.window, doc = globalThis.document,
  now = performance.now.bind(performance), later = setTimeout, cancel = clearTimeout,
  makeEvent = () => new KeyboardEvent('keydown', { key: 'Escape', code: 'Escape', keyCode: 27, which: 27, bubbles: true, cancelable: true, composed: true }),
} = {}) {
  let lastReal = -Infinity, waiting = 0, playing = false;
  const onKey = (e) => {
    if (playing || e.key !== 'Escape') return;
    lastReal = now();
    if (waiting) { cancel(waiting); waiting = 0; }
  };
  win?.addEventListener?.('keydown', onKey, true);
  return {
    kept() {
      const at = now();
      if (at - lastReal < 1500 || waiting) return false;
      waiting = later(() => {
        waiting = 0;
        if (lastReal >= at) return;
        playing = true;
        try { (doc?.activeElement || doc?.body)?.dispatchEvent(makeEvent()); }
        finally { playing = false; }
      }, 300);
      return true;
    },
    dispose() { if (waiting) cancel(waiting); waiting = 0; win?.removeEventListener?.('keydown', onKey, true); },
  };
}
