/* ============================================================================
 * board/annotations.js - the player's own arrows and circles, lichess style.
 *
 *   right drag, square to square   an arrow (through men, legal or not)
 *   right click on one square      a circle
 *   the same again                 takes that one off
 *   any left click on the board    wipes them all (so does a new game)
 *
 * Purely local: nothing here talks to the bus, the server or the opponent, and
 * nothing is written to a save or a replay. A right press that starts OFF the
 * board, a middle press, and shift + right (pan) still belong to the camera;
 * only a right press that lands on a square is taken, and the camera never
 * sees it.
 *
 * A right release the board never hears (alt-tab or an overlay while held, let
 * go outside the window) must not leave the arrow open: any other press, lost
 * capture, a window blur or a move with the right button up ends it unplaced,
 * and only a right release places one (ccp-bugs #1625: the next LEFT click
 * used to close the old press and draw an arrow nobody meant).
 *
 * This file has no three.js in it, so the smoke tests drive it with a plain
 * EventTarget. board/annotate.js draws the shapes and finds the square.
 * ==========================================================================*/

const SQ = /^[a-h][1-8]$/;

/** The shapes themselves: a circle is a square on its own, an arrow two. */
export function createAnnotations() {
  const shapes = new Map();   // key -> { from, to }; to === from is a circle
  const keyOf = (from, to) => (from === to ? from : from + '-' + to);
  return {
    /** Add a shape, or take it off if it is already there. Returns 'added', 'removed' or null. */
    toggle(from, to = from) {
      if (!SQ.test(from || '') || !SQ.test(to || '')) return null;
      const key = keyOf(from, to);
      if (shapes.has(key)) { shapes.delete(key); return 'removed'; }
      shapes.set(key, { from, to });
      return 'added';
    },
    /** Wipe every shape. True when there was something to wipe. */
    clear() { const had = shapes.size > 0; shapes.clear(); return had; },
    list: () => [...shapes.values()].map(s => ({ ...s, kind: s.from === s.to ? 'circle' : 'arrow' })),
    size: () => shapes.size,
  };
}

/**
 * The hand for it. `target` is the canvas, `squareAt(ev)` names the square under
 * a pointer event (or null off the board), `onChange(shapes, preview)` redraws,
 * `blocked()` says the board is busy (a menu, a picker) and the press is not ours.
 * `win` hears the blur (the page's window; a plain EventTarget in the tests).
 */
export function createAnnotator({ target, squareAt, onChange = () => {}, blocked = () => false,
  win = globalThis.window }) {
  const shapes = createAnnotations();
  let press = null;      // { id, from, over } while a right press that started on a square is down

  const redraw = () => onChange(shapes.list(), press && press.over && press.over !== press.from
    ? { from: press.from, to: press.over, kind: 'arrow' }
    : press ? { from: press.from, to: press.from, kind: 'circle' } : null);

  /** End an open press without placing anything (its release was lost). */
  function drop() {
    if (!press) return false;
    const id = press.id;
    press = null;
    try { target.releasePointerCapture?.(id); } catch { /* never taken */ }
    return true;
  }

  function onDown(ev) {
    if (ev.pointerType === 'touch') return;
    // A press of any button while one is still open means its release never
    // reached us: that old press ends here and draws nothing.
    const dropped = drop();
    if (ev.button === 0) {
      // A left click is a move, a pick-up or a click away: the sketch is wiped
      // and the click goes on to drag.js untouched.
      if (shapes.clear() || dropped) redraw();
      return;
    }
    if (ev.button !== 2 || ev.shiftKey || blocked()) { if (dropped) redraw(); return; }
    const from = squareAt(ev);
    if (!from) { if (dropped) redraw(); return; }   // off the board: the camera orbits as before
    press = { id: ev.pointerId, from, over: from };
    ev.stopImmediatePropagation();      // the camera's own listener never hears this press
    ev.preventDefault();
    try { target.setPointerCapture?.(ev.pointerId); } catch { /* a synthetic pointer */ }
    redraw();
  }

  function onMove(ev) {
    if (!press || ev.pointerId !== press.id) return;
    // A mouse move that no longer holds the right button: it was let go unheard.
    if (ev.pointerType === 'mouse' && typeof ev.buttons === 'number' && !(ev.buttons & 2)) {
      drop(); redraw(); return;
    }
    const over = squareAt(ev);
    if (over && over !== press.over) { press.over = over; redraw(); }
  }

  function onUp(ev) {
    if (!press || ev.pointerId !== press.id || ev.button !== 2) return;   // only a right release places it
    const end = squareAt(ev) || press.over;
    const from = press.from;
    press = null;
    try { target.releasePointerCapture?.(ev.pointerId); } catch { /* never taken */ }
    shapes.toggle(from, end);
    redraw();
  }

  function onCancel(ev) {
    if (!press || (ev && ev.pointerId != null && ev.pointerId !== press.id)) return;
    drop();
    redraw();
  }

  const onBlur = () => { if (drop()) redraw(); };

  // Capture, so on the canvas itself this runs before the camera's bubble listener.
  target.addEventListener('pointerdown', onDown, { capture: true });
  target.addEventListener('pointermove', onMove);
  target.addEventListener('pointerup', onUp);
  target.addEventListener('pointercancel', onCancel);
  target.addEventListener('lostpointercapture', onCancel);
  win?.addEventListener?.('blur', onBlur);

  return {
    list: shapes.list,
    size: shapes.size,
    toggle(from, to) { const r = shapes.toggle(from, to); if (r) redraw(); return r; },
    clear() { press = null; shapes.clear(); redraw(); },
    isDrawing: () => !!press,
    dispose() {
      target.removeEventListener('pointerdown', onDown, { capture: true });
      target.removeEventListener('pointermove', onMove);
      target.removeEventListener('pointerup', onUp);
      target.removeEventListener('pointercancel', onCancel);
      target.removeEventListener('lostpointercapture', onCancel);
      win?.removeEventListener?.('blur', onBlur);
    },
  };
}
