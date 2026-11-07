/* preview/loader.js - lazy per-focus loading for the HUD preview box (lane B3-W5). Pure: the drawing is injected.
 *
 *   const pv = createPreviewLoader({ resolve, open, release, empty, wait, settleMs, outMs, log });
 *   pv.focus(itemId | null); pv.dispose();
 *
 * resolve(id) -> spec | null (preview/catalog.js). open(spec) -> Promise<{ show(), hide(), dispose() }> loads and
 * builds one preview; nothing loads until the focus has rested settleMs, so a walk along a shelf fetches only where
 * it stops. A stale load (the focus moved on while it ran) is disposed unseen. Leaving an item: hide() (the OUT), then
 * dispose() outMs later. release(kind) drops what a kind keeps between its items (the Rain avatar, the chess knight)
 * when the focus moves to another kind, and on dispose. empty(on) tells the box whether anything will be drawn. */

export function createPreviewLoader({ resolve, open, release = () => {}, empty = () => {}, wait, settleMs = 140, outMs = 120,
  log = () => {} }) {
  let seq = 0, cur, shown = null, kind = null, dead = false;
  const pause = wait || ((ms) => new Promise((r) => setTimeout(r, ms)));

  async function retire(h, ms) {
    if (!h) return;
    try { h.hide(); } catch { /* the OUT is best effort */ }
    if (ms) await pause(ms);
    try { h.dispose(); } catch (e) { log(`prizecounter: preview dispose (${(e && e.message) || e})`); }
  }

  return {
    get id() { return cur === undefined ? null : cur; },
    get kind() { return kind; },
    async focus(idIn) {
      const id = idIn || null;
      if (dead || id === cur) return;
      cur = id;
      const tok = ++seq, old = shown;
      shown = null;
      const spec = id ? resolve(id) : null;
      empty(!spec);
      const out = retire(old, outMs);
      if (!spec) return out;
      // Release a kind's shared art only once the old preview is gone: it is a clone of that art and keeps drawing
      // through its fade OUT, so freeing it sooner re-uploads the freed buffers (and leaks them). Skip the release if
      // the focus came back to that kind meanwhile.
      if (spec.kind !== kind) {
        const was = kind;
        kind = spec.kind;
        if (was) out.then(() => { if (kind !== was) release(was); });
      }
      await pause(settleMs);
      if (tok !== seq || dead) return out;
      let h = null;
      try { h = await open(spec); } catch (e) { log(`prizecounter: preview ${id} unavailable (${(e && e.message) || e})`); }
      if (tok !== seq || dead) { if (h) retire(h, 0); return out; }
      await out;
      if (tok !== seq || dead) { if (h) retire(h, 0); return; }
      if (!h) { empty(true); return; }
      shown = h;
      h.show();
    },
    /** The counter closed: drop the shown preview at once and everything kept between items. */
    dispose() {
      if (dead) return;
      dead = true; seq++;
      const h = shown; shown = null;
      retire(h, 0);
      if (kind) release(kind);
      kind = null;
    },
  };
}
