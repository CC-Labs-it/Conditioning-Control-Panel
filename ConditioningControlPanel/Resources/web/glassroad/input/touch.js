/* input/touch.js - touch InputSource, lifted from dtrh/race/touch.js and adapted.
 *
 * A THIRD additive source, never a remap: left stays left. Built only on a touchable page
 * (`(pointer: coarse)` or maxTouchPoints > 0; `?touch=1` / `?touch=0` override).
 *   LEFT 55%   relative drag stick: full lock at +-STEER_LOCK_PX from where the thumb landed,
 *              nothing inside STEER_DEAD_PX, zero the moment it lifts.
 *   RIGHT      hold = hop and drift (the press is the hop, the lift releases the drift and fires
 *              its boost).
 *   THROTTLE   auto-accelerate, from the first touch on the layer (a touch laptop driven by keys
 *              never cruises on its own).
 *   PAUSE      one 48 px button (Escape does not exist on glass; the phone panic is the host's).
 * The WebKit lessons from the old race stay: touchstart's default is prevented over the road, only
 * the wheel pointer is captured, lifts are also heard on the window, and a touchend reconciles held
 * ids with the fingers still down so a swallowed pointerup never strands a drift.
 *   createTouch({ root, win, doc }) -> null, or { el, poll(into), dispose() }
 */
import { devices } from './device.js';

export const STEER_LOCK_PX = 72;
export const STEER_DEAD_PX = 6;
export const STEER_SIDE = 0.55;

const clamp = (v, a, b) => Math.max(a, Math.min(b, v));

export function steerFromDx(dx, lock = STEER_LOCK_PX, dead = STEER_DEAD_PX) {
  const m = Math.abs(dx);
  if (!(m > dead)) return 0;
  const v = Math.min(1, (m - dead) / Math.max(1, lock - dead));
  return dx < 0 ? -v : v;
}

export function wantsTouch(win) {
  const w = win || (typeof window !== 'undefined' ? window : null);
  if (!w) return false;
  try {
    const q = new URLSearchParams(w.location.search).get('touch');
    if (q === '1' || q === 'on') return true;
    if (q === '0' || q === 'off') return false;
  } catch (e) { /* no location: fall through to the device test */ }
  const nav = w.navigator;
  if (nav && typeof nav.maxTouchPoints === 'number' && nav.maxTouchPoints > 0) return true;
  try { return !!(typeof w.matchMedia === 'function' && w.matchMedia('(pointer: coarse)').matches); } catch (e) { return false; }
}

export function createTouch({ root = null, win = null, doc = null } = {}) {
  const w = win || (typeof window !== 'undefined' ? window : null);
  const d = doc || (w && w.document) || null;
  if (!root || !d || !wantsTouch(w)) return null;

  const mk = (cls, parent, css) => { const n = d.createElement('div'); n.className = cls; n.style.cssText = css; parent.appendChild(n); return n; };
  const layer = mk('gr-touch', root, 'position:absolute;inset:0;z-index:12;touch-action:none;user-select:none;-webkit-user-select:none');
  const pad = mk('gr-touch-pad', layer, 'position:absolute;display:none;width:0;height:0');
  mk('gr-touch-ring', pad, 'position:absolute;left:-48px;top:-48px;width:96px;height:96px;border-radius:50%;border:2px solid rgba(255,255,255,.35)');
  const dot = mk('gr-touch-dot', pad, 'position:absolute;left:-18px;top:-18px;width:36px;height:36px;border-radius:50%;background:rgba(255,255,255,.5)');
  const pauseBtn = d.createElement('button');
  pauseBtn.type = 'button'; pauseBtn.className = 'gr-touch-btn gr-touch-pause'; pauseBtn.textContent = 'II';
  pauseBtn.setAttribute('aria-label', 'pause');
  pauseBtn.style.cssText = 'position:absolute;top:max(12px,env(safe-area-inset-top));right:max(12px,env(safe-area-inset-right));min-width:48px;min-height:48px';
  layer.appendChild(pauseBtn);
  const cameraButton = (name, label, offset) => {
    const b = d.createElement('button'); b.type = 'button'; b.className = 'gr-touch-btn gr-touch-' + name;
    b.textContent = label; b.setAttribute('aria-label', name === 'view' ? 'Change chase or first person view' : 'Hold to look behind');
    b.style.cssText = 'position:absolute;bottom:max(12px,env(safe-area-inset-bottom));left:calc(50% + ' + offset + 'px);min-width:52px;min-height:48px;border:2px solid #fff;border-radius:8px;background:#201b29;color:#fff;font:700 13px sans-serif;touch-action:none';
    layer.appendChild(b); return b;
  };
  const viewBtn = cameraButton('view', 'View', -58), rearBtn = cameraButton('rear', 'Rear', 6);

  let steerId = -1, steerX0 = 0, steer = 0, actId = -1, touched = false, disposed = false;
  let hopEdge = false, pauseEdge = false, cameraEdge = false, rearId = -1;
  const onBtn = (t) => !!(t && typeof t.closest === 'function' && t.closest('.gr-touch-btn'));
  const sideOf = (x) => { const r = layer.getBoundingClientRect(); return x - r.left < (r.width || 1) * STEER_SIDE ? 'left' : 'right'; };

  const onPause = (e) => { if (e.cancelable) e.preventDefault(); e.stopPropagation(); touched = true; pauseEdge = true; devices.use('touch'); };
  pauseBtn.addEventListener('pointerdown', onPause);
  const onView = (e) => { if (e.cancelable) e.preventDefault(); e.stopPropagation(); cameraEdge = true; devices.use('touch'); };
  const onRear = (e) => {
    if (e.cancelable) e.preventDefault(); e.stopPropagation();
    if (rearId >= 0) return;
    rearId = e.pointerId; devices.use('touch');
    try { rearBtn.setPointerCapture(rearId); } catch (err) { /* window release still clears it */ }
  };
  const onViewClick = (e) => { if (e.detail === 0) onView(e); };
  viewBtn.addEventListener('pointerdown', onView);
  viewBtn.addEventListener('click', onViewClick);
  rearBtn.addEventListener('pointerdown', onRear);
  rearBtn.addEventListener('lostpointercapture', onUp);

  function endSteer() { steerId = -1; steer = 0; pad.style.display = 'none'; }
  function onDown(e) {
    if (disposed || onBtn(e.target)) return;
    touched = true;
    devices.use('touch');
    if (sideOf(e.clientX) === 'left') {
      if (steerId >= 0) return;
      const r = layer.getBoundingClientRect();
      steerId = e.pointerId; steerX0 = e.clientX; steer = 0;
      pad.style.left = (e.clientX - r.left) + 'px'; pad.style.top = (e.clientY - r.top) + 'px';
      pad.style.display = 'block'; dot.style.transform = 'translate(0px,0px)';
      try { layer.setPointerCapture(e.pointerId); } catch (err) { /* listeners still fire */ }
    } else {
      if (actId >= 0) return;
      actId = e.pointerId; hopEdge = true;     // not captured: capture is what WebKit hands back as a cancel
    }
    if (e.cancelable) e.preventDefault();
  }
  function onMove(e) {
    if (disposed || e.pointerId !== steerId) return;
    const dx = e.clientX - steerX0;
    steer = steerFromDx(dx);
    dot.style.transform = 'translate(' + clamp(dx, -STEER_LOCK_PX, STEER_LOCK_PX) + 'px,0px)';
    if (e.cancelable) e.preventDefault();
  }
  function onUp(e) {
    if (disposed) return;
    if (e.pointerId === rearId) {
      rearId = -1;
      try { rearBtn.releasePointerCapture(e.pointerId); } catch (err) { /* already gone */ }
      return;
    }
    if (e.pointerId === steerId) endSteer();
    else if (e.pointerId === actId) actId = -1;
    else return;
    try { layer.releasePointerCapture(e.pointerId); } catch (err) { /* already gone */ }
  }
  function onTouchStart(e) { if (!disposed && !onBtn(e.target) && e.cancelable) e.preventDefault(); }
  function onTouchEnd(e) {
    if (disposed) return;
    let left = false, right = false, rear = false;
    const rest = e.touches || [];
    for (let i = 0; i < rest.length; i++) {
      if (rest[i].target === rearBtn) rear = true;
      if (onBtn(rest[i].target)) continue;
      if (sideOf(rest[i].clientX) === 'left') left = true; else right = true;
    }
    if (!left && steerId >= 0) endSteer();
    if (!right) actId = -1;
    if (!rear) rearId = -1;
  }
  const noMenu = (e) => { if (e.cancelable) e.preventDefault(); };
  const onBlur = () => { endSteer(); actId = rearId = -1; hopEdge = cameraEdge = false; };

  layer.addEventListener('pointerdown', onDown);
  layer.addEventListener('pointermove', onMove);
  layer.addEventListener('pointerup', onUp);
  layer.addEventListener('pointercancel', onUp);
  layer.addEventListener('contextmenu', noMenu);
  layer.addEventListener('touchstart', onTouchStart, { passive: false });
  layer.addEventListener('touchend', onTouchEnd, { passive: false });
  layer.addEventListener('touchcancel', onTouchEnd, { passive: false });
  if (w && typeof w.addEventListener === 'function') {
    w.addEventListener('pointerup', onUp, true);
    w.addEventListener('pointercancel', onUp, true);
    w.addEventListener('blur', onBlur);
  }

  function poll(into) {
    into.steer = clamp(steer, -1, 1);
    into.steerKind = 'touch';                       // the action map applies the curve (input/steerCurve.js)
    into.throttle = touched ? 1 : 0;
    into.hop = actId >= 0;
    into.hopPressed = hopEdge;
    into.pause = pauseEdge;
    into.cameraPressed = cameraEdge;
    into.rearView = rearId >= 0;
    hopEdge = false; pauseEdge = false; cameraEdge = false;
  }
  function dispose() {
    disposed = true;
    onBlur();
    pauseBtn.removeEventListener('pointerdown', onPause);
    viewBtn.removeEventListener('pointerdown', onView);
    viewBtn.removeEventListener('click', onViewClick);
    rearBtn.removeEventListener('pointerdown', onRear);
    rearBtn.removeEventListener('lostpointercapture', onUp);
    layer.removeEventListener('pointerdown', onDown);
    layer.removeEventListener('pointermove', onMove);
    layer.removeEventListener('pointerup', onUp);
    layer.removeEventListener('pointercancel', onUp);
    layer.removeEventListener('contextmenu', noMenu);
    layer.removeEventListener('touchstart', onTouchStart);
    layer.removeEventListener('touchend', onTouchEnd);
    layer.removeEventListener('touchcancel', onTouchEnd);
    if (w && typeof w.removeEventListener === 'function') {
      w.removeEventListener('pointerup', onUp, true);
      w.removeEventListener('pointercancel', onUp, true);
      w.removeEventListener('blur', onBlur);
    }
    if (layer.parentNode) layer.parentNode.removeChild(layer);
  }
  return { el: layer, poll, dispose };
}
