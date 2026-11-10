import { holdSlop } from '../../dock.js';

/** Hold the original EMI with the same pointer until release, even while her page tilts. */
export function bindEmiGrab(ctx, main, { place, drop, pet }) {
  const { el } = main;
  const win = el.ownerDocument.defaultView;
  let hand = null;
  el.classList.add('et-main');
  function track(event) {
    if (!hand || event.pointerId !== hand.id || ctx.paused) return;
    const dx = event.clientX - hand.x, dy = event.clientY - hand.y;
    if (!hand.moved && Math.hypot(dx, dy) <= hand.slop) return;
    hand.moved = true;
    const delta = new DOMPoint(dx, dy, 0, 0).matrixTransform(hand.inverse);
    place(hand.left + delta.x, hand.top + delta.y);
  }
  function finish(event) {
    if (!hand || (event && event.pointerId !== hand.id)) return;
    const released = event?.type === 'pointerup' && !ctx.paused;
    if (released) track(event);
    const last = hand;
    hand = null;
    el.removeAttribute('data-held');
    if (el.hasPointerCapture(last.id)) el.releasePointerCapture(last.id);
    if (last.moved) {
      if (released) drop();
      else place(last.left, last.top);
    } else if (released) pet(event);
  }
  el.addEventListener('pointerdown', event => {
    if (hand || ctx.paused || event.button !== 0 || !event.isPrimary) return;
    event.preventDefault();
    const inverse = new DOMMatrixReadOnly(win.getComputedStyle(ctx.world).transform).inverse();
    hand = { id: event.pointerId, x: event.clientX, y: event.clientY, left: main.x, top: main.y,
      slop: holdSlop(event.pointerType === 'touch'), inverse, moved: false };
    el.setPointerCapture(hand.id);
    el.setAttribute('data-held', '');
  });
  el.addEventListener('pointermove', track);
  for (const type of ['pointerup', 'pointercancel', 'lostpointercapture']) el.addEventListener(type, finish);
  const cancel = () => finish(null);
  for (const type of ['pause', 'suspend', 'ending']) ctx.on(type, cancel);
  win.addEventListener('blur', cancel);
  ctx.cleanup(() => { cancel(); win.removeEventListener('blur', cancel); });
  return { get held() { return !!hand; } };
}
