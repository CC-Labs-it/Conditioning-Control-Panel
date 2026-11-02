/* core/math.js - plain math on numbers and small arrays. No three.js here.
 *
 * Axes: right-handed, +Y up, metres, seconds, radians. Vectors are [x, y, z],
 * quaternions [x, y, z, w]. Functions that build a vector take `out` first and
 * return it (no hidden allocation); the `v3()` / `q4()` makers allocate.
 * Kart and camera local frame: forward -Z, right +X, up +Y (the three.js camera frame).
 */

export const DEG = Math.PI / 180;
export const RAD2DEG = 180 / Math.PI;
export const rad = (deg) => deg * DEG;
export const deg = (r) => r * RAD2DEG;

export const clamp = (x, lo, hi) => (x < lo ? lo : x > hi ? hi : x);
export const lerp = (a, b, t) => a + (b - a) * t;
export const invLerp = (a, b, x) => (b === a ? 0 : (x - a) / (b - a));
export function smoothstep(e0, e1, x) {
  const t = clamp(invLerp(e0, e1, x), 0, 1);
  return t * t * (3 - 2 * t);
}
/** Frame-rate independent ease of `a` toward `b` with time constant `tc` seconds. */
export const damp = (a, b, tc, dt) => (tc <= 0 ? b : b + (a - b) * Math.exp(-dt / tc));
/** Move `a` toward `b` by at most `step`. */
export const approach = (a, b, step) => (a < b ? Math.min(a + step, b) : Math.max(a - step, b));
/** Wrap x into [0, m). */
export const wrap = (x, m) => ((x % m) + m) % m;
/** Shortest signed angle from a to b, radians, in (-PI, PI]. */
export function angleDiff(a, b) {
  let d = wrap(b - a + Math.PI, 2 * Math.PI) - Math.PI;
  if (d <= -Math.PI) d += 2 * Math.PI;
  return d;
}

// ---- vec3 ----
export const v3 = (x = 0, y = 0, z = 0) => [x, y, z];
export function v3set(o, x, y, z) { o[0] = x; o[1] = y; o[2] = z; return o; }
export function v3copy(o, a) { o[0] = a[0]; o[1] = a[1]; o[2] = a[2]; return o; }
export function v3add(o, a, b) { o[0] = a[0] + b[0]; o[1] = a[1] + b[1]; o[2] = a[2] + b[2]; return o; }
export function v3sub(o, a, b) { o[0] = a[0] - b[0]; o[1] = a[1] - b[1]; o[2] = a[2] - b[2]; return o; }
export function v3scale(o, a, s) { o[0] = a[0] * s; o[1] = a[1] * s; o[2] = a[2] * s; return o; }
/** o = a + b * s */
export function v3addScaled(o, a, b, s) { o[0] = a[0] + b[0] * s; o[1] = a[1] + b[1] * s; o[2] = a[2] + b[2] * s; return o; }
export const v3dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
export function v3cross(o, a, b) {
  const x = a[1] * b[2] - a[2] * b[1], y = a[2] * b[0] - a[0] * b[2], z = a[0] * b[1] - a[1] * b[0];
  o[0] = x; o[1] = y; o[2] = z; return o;
}
export const v3len = (a) => Math.hypot(a[0], a[1], a[2]);
export const v3dist = (a, b) => Math.hypot(a[0] - b[0], a[1] - b[1], a[2] - b[2]);
export function v3norm(o, a) {
  const l = v3len(a);
  return l > 1e-12 ? v3scale(o, a, 1 / l) : v3set(o, 0, 0, 0);
}
export function v3lerp(o, a, b, t) {
  o[0] = a[0] + (b[0] - a[0]) * t; o[1] = a[1] + (b[1] - a[1]) * t; o[2] = a[2] + (b[2] - a[2]) * t; return o;
}
/** Remove the component of `a` along unit `n`. */
export function v3reject(o, a, n) { return v3addScaled(o, a, n, -v3dot(a, n)); }

// ---- quaternion [x, y, z, w] ----
export const q4 = () => [0, 0, 0, 1];
export function qcopy(o, a) { o[0] = a[0]; o[1] = a[1]; o[2] = a[2]; o[3] = a[3]; return o; }
export function qidentity(o) { o[0] = 0; o[1] = 0; o[2] = 0; o[3] = 1; return o; }
export function qnorm(o, a) {
  const l = Math.hypot(a[0], a[1], a[2], a[3]) || 1;
  o[0] = a[0] / l; o[1] = a[1] / l; o[2] = a[2] / l; o[3] = a[3] / l; return o;
}
export function qmul(o, a, b) {
  const ax = a[0], ay = a[1], az = a[2], aw = a[3], bx = b[0], by = b[1], bz = b[2], bw = b[3];
  o[0] = ax * bw + aw * bx + ay * bz - az * by;
  o[1] = ay * bw + aw * by + az * bx - ax * bz;
  o[2] = az * bw + aw * bz + ax * by - ay * bx;
  o[3] = aw * bw - ax * bx - ay * by - az * bz;
  return o;
}
/** Rotation of `angle` radians about unit `axis`. */
export function qaxis(o, axis, angle) {
  const s = Math.sin(angle / 2);
  o[0] = axis[0] * s; o[1] = axis[1] * s; o[2] = axis[2] * s; o[3] = Math.cos(angle / 2); return o;
}
/** Rotate vector `v` by quaternion `q`. */
export function qrotate(o, q, v) {
  const qx = q[0], qy = q[1], qz = q[2], qw = q[3], vx = v[0], vy = v[1], vz = v[2];
  const tx = 2 * (qy * vz - qz * vy), ty = 2 * (qz * vx - qx * vz), tz = 2 * (qx * vy - qy * vx);
  o[0] = vx + qw * tx + qy * tz - qz * ty;
  o[1] = vy + qw * ty + qz * tx - qx * tz;
  o[2] = vz + qw * tz + qx * ty - qy * tx;
  return o;
}
export function qslerp(o, a, b, t) {
  let bx = b[0], by = b[1], bz = b[2], bw = b[3];
  let c = a[0] * bx + a[1] * by + a[2] * bz + a[3] * bw;
  if (c < 0) { c = -c; bx = -bx; by = -by; bz = -bz; bw = -bw; }
  let k0 = 1 - t, k1 = t;
  if (c < 0.9995) {
    const th = Math.acos(c), s = Math.sin(th);
    k0 = Math.sin((1 - t) * th) / s; k1 = Math.sin(t * th) / s;
  }
  o[0] = a[0] * k0 + bx * k1; o[1] = a[1] * k0 + by * k1; o[2] = a[2] * k0 + bz * k1; o[3] = a[3] * k0 + bw * k1;
  return qnorm(o, o);
}
/** Quaternion from an orthonormal basis given as local +X (right), +Y (up), +Z (back = -forward). */
export function qfromBasis(o, right, up, back) {
  const m00 = right[0], m01 = up[0], m02 = back[0];
  const m10 = right[1], m11 = up[1], m12 = back[1];
  const m20 = right[2], m21 = up[2], m22 = back[2];
  const tr = m00 + m11 + m22;
  if (tr > 0) {
    const s = 0.5 / Math.sqrt(tr + 1);
    o[3] = 0.25 / s; o[0] = (m21 - m12) * s; o[1] = (m02 - m20) * s; o[2] = (m10 - m01) * s;
  } else if (m00 > m11 && m00 > m22) {
    const s = 2 * Math.sqrt(1 + m00 - m11 - m22);
    o[3] = (m21 - m12) / s; o[0] = 0.25 * s; o[1] = (m01 + m10) / s; o[2] = (m02 + m20) / s;
  } else if (m11 > m22) {
    const s = 2 * Math.sqrt(1 + m11 - m00 - m22);
    o[3] = (m02 - m20) / s; o[0] = (m01 + m10) / s; o[1] = 0.25 * s; o[2] = (m12 + m21) / s;
  } else {
    const s = 2 * Math.sqrt(1 + m22 - m00 - m11);
    o[3] = (m10 - m01) / s; o[0] = (m02 + m20) / s; o[1] = (m12 + m21) / s; o[2] = 0.25 * s;
  }
  return qnorm(o, o);
}
/** Quaternion that faces local -Z along `forward` with local +Y as close to `up` as possible. */
export function qlook(o, forward, up) {
  const back = v3norm([0, 0, 0], v3scale([0, 0, 0], forward, -1));
  const right = v3norm([0, 0, 0], v3cross([0, 0, 0], up, back));
  const u = v3cross([0, 0, 0], back, right);
  return qfromBasis(o, right, u, back);
}

// ---- mat3 (column-major, 9 numbers) ----
/** Column-major 3x3 rotation from quaternion; columns are local right, up, back in world. */
export function m3fromQuat(o, q) {
  const x = q[0], y = q[1], z = q[2], w = q[3];
  const x2 = x + x, y2 = y + y, z2 = z + z;
  const xx = x * x2, yx = y * x2, yy = y * y2, zx = z * x2, zy = z * y2, zz = z * z2;
  const wx = w * x2, wy = w * y2, wz = w * z2;
  o[0] = 1 - yy - zz; o[1] = yx + wz; o[2] = zx - wy;
  o[3] = yx - wz; o[4] = 1 - xx - zz; o[5] = zy + wx;
  o[6] = zx + wy; o[7] = zy - wx; o[8] = 1 - xx - yy;
  return o;
}
