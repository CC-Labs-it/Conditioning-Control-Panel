import { holdSlop } from '../../dock.js';

/** One captured gesture belongs to the EMI under the pointer, even as the rest walk past. */
export function bindEmiGrab(ctx, box, { pick, lift, place, drop, pet }) {
  const win = box.ownerDocument.defaultView;
  let hand = null;
  function track(event) {
    if (!hand || event.pointerId !== hand.id || ctx.paused) return;
    const dx = event.clientX - hand.x, dy = event.clientY - hand.y;
    if (!hand.moved && Math.hypot(dx, dy) <= hand.slop) return;
    if (!hand.moved) {
      hand.moved = true;
      lift(hand.emi);
    }
    const delta = new DOMPoint(dx, dy, 0, 0).matrixTransform(hand.inverse);
    place(hand.emi, hand.left + delta.x, hand.top + delta.y);
  }
  function finish(event) {
    if (!hand || (event && event.pointerId !== hand.id)) return;
    const released = event?.type === 'pointerup' && !ctx.paused;
    if (released) track(event);
    const last = hand;
    hand = null;
    const { el } = last.emi;
    el.removeAttribute('data-held');
    if (el.hasPointerCapture(last.id)) el.releasePointerCapture(last.id);
    if (last.moved) {
      if (released) drop(last.emi);
      else place(last.emi, last.left, last.top);
    } else if (released) pet(last.emi, event);
  }
  box.addEventListener('pointerdown', event => {
    if (hand || ctx.paused || event.button !== 0 || !event.isPrimary) return;
    const el = event.target.closest('.et-emi'), emi = pick(el);
    if (!emi) return;
    event.preventDefault();
    const inverse = new DOMMatrixReadOnly(win.getComputedStyle(ctx.world).transform).inverse();
    hand = { emi, id: event.pointerId, x: event.clientX, y: event.clientY, left: emi.x, top: emi.y,
      slop: holdSlop(event.pointerType === 'touch'), inverse, moved: false };
    el.setPointerCapture(hand.id);
    el.setAttribute('data-held', '');
  });
  box.addEventListener('pointermove', track);
  for (const type of ['pointerup', 'pointercancel', 'lostpointercapture']) box.addEventListener(type, finish);
  const cancel = () => finish(null);
  for (const type of ['pause', 'suspend', 'ending']) ctx.on(type, cancel);
  win.addEventListener('blur', cancel);
  ctx.cleanup(() => { cancel(); win.removeEventListener('blur', cancel); });
  return { get active() { return !!hand; }, holds: emi => hand?.emi === emi, dragging: emi => hand?.emi === emi && hand.moved };
}
