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
 */
export function createAnnotator({ target, squareAt, onChange = () => {}, blocked = () => false }) {
  const shapes = createAnnotations();
  let press = null;      // { id, from, over } while a right press that started on a square is down

  const redraw = () => onChange(shapes.list(), press && press.over && press.over !== press.from
    ? { from: press.from, to: press.over, kind: 'arrow' }
    : press ? { from: press.from, to: press.from, kind: 'circle' } : null);

  function onDown(ev) {
    if (ev.pointerType === 'touch') return;
    if (ev.button === 0) {
      // A left click is a move, a pick-up or a click away: the sketch is wiped
      // and the click goes on to drag.js untouched.
      if (shapes.clear()) redraw();
      return;
    }
    if (ev.button !== 2 || ev.shiftKey || press || blocked()) return;
    const from = squareAt(ev);
    if (!from) return;                  // off the board: the camera orbits as before
    press = { id: ev.pointerId, from, over: from };
    ev.stopImmediatePropagation();      // the camera's own listener never hears this press
    ev.preventDefault();
    try { target.setPointerCapture?.(ev.pointerId); } catch { /* a synthetic pointer */ }
    redraw();
  }

  function onMove(ev) {
    if (!press || ev.pointerId !== press.id) return;
    const over = squareAt(ev);
    if (over && over !== press.over) { press.over = over; redraw(); }
  }

  function onUp(ev) {
    if (!press || ev.pointerId !== press.id) return;
    const end = squareAt(ev) || press.over;
    const from = press.from;
    press = null;
    try { target.releasePointerCapture?.(ev.pointerId); } catch { /* never taken */ }
    shapes.toggle(from, end);
    redraw();
  }

  function onCancel(ev) {
    if (!press || (ev && ev.pointerId !== press.id)) return;
    press = null;
    redraw();
  }

  // Capture, so on the canvas itself this runs before the camera's bubble listener.
  target.addEventListener('pointerdown', onDown, { capture: true });
  target.addEventListener('pointermove', onMove);
  target.addEventListener('pointerup', onUp);
  target.addEventListener('pointercancel', onCancel);

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
    },
  };
}
