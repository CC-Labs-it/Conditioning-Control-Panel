const TO_DEGREES = 180 / Math.PI;

/** The slice ring owns its pointer. Radius changes do not turn the rotor.
 * Angular deltas wrap across twelve o'clock and either side of atan2's seam. */
export function bindWheelDrag(ring, { ready, move, release }) {
  const doc = ring.ownerDocument, win = doc.defaultView;
  const listeners = new win.AbortController();
  const options = { signal: listeners.signal };
  let hand = null;

  function bearing(event) {
    const box = ring.getBoundingClientRect();
    const x = (event.clientX - box.left) / box.width - .5;
    const y = (event.clientY - box.top) / box.height - .5;
    // Crossing the axle has no well-defined bearing. Reacquire on the far side.
    return Math.hypot(x,y) > .12 ? Math.atan2(y,x) : null;
  }
  function sample(event) {
    const next = bearing(event), last = hand.bearing;
    hand.bearing = next;
    if (next === null || last === null) { hand.at = event.timeStamp; hand.omega = 0; return; }
    const delta = Math.atan2(Math.sin(next-last), Math.cos(next-last));
    if (!delta) return;
    const dt = Math.max(1, event.timeStamp - hand.at);
    hand.at = event.timeStamp;
    hand.turn += delta;
    hand.travel += Math.abs(delta);
    // Time-based smoothing keeps the throw consistent on high-rate pens and
    // touchscreens which deliver fewer samples than a desktop mouse.
    hand.omega += (delta/dt - hand.omega) * (1 - Math.exp(-dt/30));
    hand.intent = Math.max(-.12, Math.min(.12, hand.intent + delta));
    if (Math.abs(hand.intent) > .025) hand.sign = Math.sign(hand.intent);
    move(hand.turn * TO_DEGREES, hand.omega);
  }
  function finish(commit, at) {
    if (!hand) return;
    const drag = hand;
    hand = null;
    ring.removeAttribute('data-held');
    if (ring.hasPointerCapture(drag.id)) ring.releasePointerCapture(drag.id);
    if (!commit || drag.travel <= .1 || !ready()) { release(null); return; }
    const speed = Math.abs(drag.omega) * Math.exp(-Math.max(0, at - drag.at)/100);
    release(drag.sign * Math.max(.006, Math.min(.03, speed)));
  }
  const cancel = () => finish(false, 0);
  ring.addEventListener('pointerdown', event => {
    if (hand || !ready() || !event.isPrimary || event.button !== 0) return;
    event.preventDefault();
    ring.setPointerCapture(event.pointerId);
    hand = { id:event.pointerId, bearing:bearing(event), at:event.timeStamp, turn:0, travel:0, omega:0, intent:0, sign:1 };
    ring.setAttribute('data-held','');
    move(0,0);
  }, options);
  ring.addEventListener('pointermove', event => {
    if (hand?.id !== event.pointerId) return;
    if (!ready()) { cancel(); return; }
    sample(event);
  }, options);
  ring.addEventListener('pointerup', event => {
    if (hand?.id !== event.pointerId) return;
    sample(event);
    finish(true,event.timeStamp);
  }, options);
  for (const type of ['pointercancel','lostpointercapture']) {
    ring.addEventListener(type, event => { if (hand?.id === event.pointerId) cancel(); }, options);
  }
  win.addEventListener('blur', cancel, options);
  doc.addEventListener('visibilitychange', () => { if (doc.hidden) cancel(); }, options);
  return { cancel, dispose() { cancel(); listeners.abort(); } };
}
