// Hold the right mouse button and drag for a bounded glance. Release returns the chase view.
import { devices as sharedDevices } from './device.js';

export function createPointerLook({ element, target = window, enabled = () => true, devices = sharedDevices } = {}) {
  const view = { x: 0, y: 0 };
  let pointer = null, lastX = 0, lastY = 0;
  const clamp = v => Math.max(-1, Math.min(1, v));
  function reset() {
    const old = pointer; pointer = null; view.x = view.y = 0;
    if (old !== null) { try { element.releasePointerCapture?.(old); } catch {} }
  }
  function down(e) {
    if (e.button !== 2 || e.pointerType !== 'mouse' || !enabled()) return;
    e.preventDefault(); pointer = e.pointerId; lastX = e.clientX; lastY = e.clientY;
    devices.use('keyboard');
    try { element.setPointerCapture?.(pointer); } catch {}
  }
  function move(e) {
    if (e.pointerId !== pointer) return;
    if (!enabled() || !(e.buttons & 2)) { reset(); return; }
    const scale = Math.max(180, Math.min(element.clientWidth || 1280, element.clientHeight || 720) * .45);
    view.x = clamp(view.x + (e.clientX - lastX) / scale);
    view.y = clamp(view.y + (e.clientY - lastY) / scale);
    lastX = e.clientX; lastY = e.clientY;
    e.preventDefault();
  }
  const up = e => { if (e.pointerId === pointer) reset(); };
  const menu = e => { if (enabled()) e.preventDefault(); };
  element.addEventListener('pointerdown', down);
  element.addEventListener('lostpointercapture', up);
  element.addEventListener('contextmenu', menu);
  target.addEventListener('pointermove', move);
  target.addEventListener('pointerup', up);
  target.addEventListener('pointercancel', up);
  target.addEventListener('blur', reset);
  return {
    read() { if (!enabled()) reset(); return view; },
    reset,
    dispose() {
      reset(); element.removeEventListener('pointerdown', down); element.removeEventListener('lostpointercapture', up);
      element.removeEventListener('contextmenu', menu); target.removeEventListener('pointermove', move);
      target.removeEventListener('pointerup', up); target.removeEventListener('pointercancel', up); target.removeEventListener('blur', reset);
    },
  };
}
