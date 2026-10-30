/* Per-frame instance matrix writes without Matrix4.compose + setMatrixAt (Matrix4.toArray).
 * toArray is shared by every array kind in three (plain arrays, Float32Array, bone buffers), so its stores go
 * megamorphic and it showed as the top self-time cost of the GOONWORKS sparkles (Firefox perf, 2026-10-26).
 * These helpers write straight into the instance Float32Array with the exact arithmetic of r186
 * Matrix4.compose, so the stored values are bit-identical to compose().toArray(). */

/** Matrix4.compose(p, q, s) written into `array` at matrix slot `index` (16 floats per slot). */
export function composeInto(array, index, p, q, s) {
  const o = index * 16, x = q._x, y = q._y, z = q._z, w = q._w;
  const x2 = x + x, y2 = y + y, z2 = z + z, xx = x * x2, xy = x * y2, xz = x * z2;
  const yy = y * y2, yz = y * z2, zz = z * z2, wx = w * x2, wy = w * y2, wz = w * z2;
  const sx = s.x, sy = s.y, sz = s.z;
  array[o] = (1 - (yy + zz)) * sx; array[o + 1] = (xy + wz) * sx; array[o + 2] = (xz - wy) * sx; array[o + 3] = 0;
  array[o + 4] = (xy - wz) * sy; array[o + 5] = (1 - (xx + zz)) * sy; array[o + 6] = (yz + wx) * sy; array[o + 7] = 0;
  array[o + 8] = (xz + wy) * sz; array[o + 9] = (yz - wx) * sz; array[o + 10] = (1 - (xx + yy)) * sz; array[o + 11] = 0;
  array[o + 12] = p.x; array[o + 13] = p.y; array[o + 14] = p.z; array[o + 15] = 1;
}

/** matrix.elements copied into `array` at matrix slot `index` (the same values setMatrixAt writes). */
export function matrixInto(array, index, matrix) {
  const o = index * 16, e = matrix.elements;
  for (let k = 0; k < 16; k++) array[o + k] = e[k];
}

/** The zero-scale matrix a hidden pooled instance wears (Object3D at the origin with scale 0). */
export function zeroInto(array, index) {
  const o = index * 16;
  for (let k = 0; k < 15; k++) array[o + k] = 0;
  array[o + 15] = 1;
}
