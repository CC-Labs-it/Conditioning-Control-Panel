// A small driver glance around the chase view, with no orbit or persistent camera offset.
import { clamp, damp } from '../core/math.js';

const DEG = Math.PI / 180;
export const DRIVER_LOOK = Object.freeze({ yawDeg: 28, upDeg: 12, downDeg: 9, inS: .12, returnS: .30 });
const axis = v => Number.isFinite(v) ? clamp(v, -1, 1) : 0;

export function createLookAround() {
  const view = { yaw: 0, pitch: 0 };
  function update(input, dt, gentle = false) {
    const x = axis(input?.x), y = axis(input?.y);
    const tc = (x || y ? DRIVER_LOOK.inS : DRIVER_LOOK.returnS) * (gentle ? 1.4 : 1);
    const step = Number.isFinite(dt) ? clamp(dt, 0, .1) : 0;
    view.yaw = damp(view.yaw, x * DRIVER_LOOK.yawDeg * DEG, tc, step);
    view.pitch = damp(view.pitch, -y * (y < 0 ? DRIVER_LOOK.upDeg : DRIVER_LOOK.downDeg) * DEG, tc, step);
    if (!x && Math.abs(view.yaw) < 1e-5) view.yaw = 0;
    if (!y && Math.abs(view.pitch) < 1e-5) view.pitch = 0;
    return view;
  }
  function apply(eye, look, up, neutralPitchDeg = 0) {
    const pitch = view.pitch + neutralPitchDeg * DEG;
    if (!view.yaw && !pitch) return look;
    const dx = look[0] - eye[0], dy = look[1] - eye[1], dz = look[2] - eye[2];
    const reach = Math.hypot(dx, dy, dz) || 1;
    let fx = dx / reach, fy = dy / reach, fz = dz / reach;
    let rx = fy * up[2] - fz * up[1], ry = fz * up[0] - fx * up[2], rz = fx * up[1] - fy * up[0];
    const r = Math.hypot(rx, ry, rz) || 1; rx /= r; ry /= r; rz /= r;
    const ux = ry * fz - rz * fy, uy = rz * fx - rx * fz, uz = rx * fy - ry * fx;
    const cy = Math.cos(view.yaw), sy = Math.sin(view.yaw), cp = Math.cos(pitch), sp = Math.sin(pitch);
    fx = (fx * cy + rx * sy) * cp + ux * sp;
    fy = (fy * cy + ry * sy) * cp + uy * sp;
    fz = (fz * cy + rz * sy) * cp + uz * sp;
    look[0] = eye[0] + fx * reach; look[1] = eye[1] + fy * reach; look[2] = eye[2] + fz * reach;
    return look;
  }
  return { view, update, apply, reset() { view.yaw = view.pitch = 0; } };
}
