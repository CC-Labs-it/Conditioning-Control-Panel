/* track/frames.js - road frames along the spline. No three.js.
 *
 * Reference normal: parallel transport sample to sample, anchored to world up wherever the tangent
 * is not near vertical (pure transport drifts about 15 deg from upright on Threshold, which would tilt
 * every flat straight). Near vertical (|t.y| > 0.9) the anchor fades out and pure transport carries the
 * frame, so loops and walls stay flip free. The closing twist left over is spread evenly by s.
 * Then roll (radians, + tips n toward b so the right edge sits lower) is applied about t.
 * Frame: { p, t, n, b, roll, w, flags, surface, seg, segT }, b = t x n = right.
 */
import { smoothstep } from '../core/math.js';

const ANCHOR_FULL = 0.9, ANCHOR_NONE = 0.98;

export function makeFrame() {
  return { p: [0, 0, 0], t: [0, 0, 1], n: [0, 1, 0], b: [1, 0, 0], roll: 0, w: 0, flags: null, surface: 'glass', seg: 0, segT: 0 };
}

/** @returns {Float64Array} reference normal per arc-length sample, (N * SUB + 1) * 3 */
export function buildRefNormals(sp) {
  const M = sp.N * sp.SUB, refN = new Float64Array((M + 1) * 3), T = new Float64Array((M + 1) * 3);
  const d = [0, 0, 0];
  for (let j = 0; j <= M; j++) {
    const seg = j === M ? sp.N - 1 : Math.floor(j / sp.SUB), u = j === M ? 1 : (j % sp.SUB) / sp.SUB;
    sp.deriv(seg, u, d);
    const l = Math.hypot(d[0], d[1], d[2]) || 1;
    T[j * 3] = d[0] / l; T[j * 3 + 1] = d[1] / l; T[j * 3 + 2] = d[2] / l;
  }
  let nx = 0, ny = 1, nz = 0;
  for (let j = 0; j <= M; j++) {
    const tx = T[j * 3], ty = T[j * 3 + 1], tz = T[j * 3 + 2];
    // transport: drop the new tangent component
    let k = nx * tx + ny * ty + nz * tz;
    nx -= k * tx; ny -= k * ty; nz -= k * tz;
    // anchor toward the up projection
    const a = smoothstep(ANCHOR_NONE, ANCHOR_FULL, Math.abs(ty));
    if (a > 0) {
      const ux = -ty * tx, uy = 1 - ty * ty, uz = -ty * tz, ul = Math.hypot(ux, uy, uz) || 1;
      nx += (ux / ul - nx) * a; ny += (uy / ul - ny) * a; nz += (uz / ul - nz) * a;
      k = nx * tx + ny * ty + nz * tz;
      nx -= k * tx; ny -= k * ty; nz -= k * tz;
    }
    const l = Math.hypot(nx, ny, nz) || 1;
    nx /= l; ny /= l; nz /= l;
    refN[j * 3] = nx; refN[j * 3 + 1] = ny; refN[j * 3 + 2] = nz;
  }
  // closing twist: signed angle from the transported end normal to the start normal, about t0
  const e = M * 3;
  const cx = refN[e + 1] * refN[2] - refN[e + 2] * refN[1];
  const cy = refN[e + 2] * refN[0] - refN[e] * refN[2];
  const cz = refN[e] * refN[1] - refN[e + 1] * refN[0];
  const twist = Math.atan2(cx * T[0] + cy * T[1] + cz * T[2], refN[e] * refN[0] + refN[e + 1] * refN[1] + refN[e + 2] * refN[2]);
  if (Math.abs(twist) > 1e-9) {
    const L = sp.lengthM;
    for (let j = 0; j <= M; j++) rotateAbout(refN, j * 3, T, j * 3, twist * sp.sampleS[j] / L);
  }
  return refN;
}

/** Rotate the vector at a[ia] about unit axis t[it] by angle (Rodrigues; v is perpendicular to t). */
function rotateAbout(a, ia, t, it, ang) {
  const c = Math.cos(ang), s = Math.sin(ang);
  const vx = a[ia], vy = a[ia + 1], vz = a[ia + 2], tx = t[it], ty = t[it + 1], tz = t[it + 2];
  a[ia] = vx * c + (ty * vz - tz * vy) * s;
  a[ia + 1] = vy * c + (tz * vx - tx * vz) * s;
  a[ia + 2] = vz * c + (tx * vy - ty * vx) * s;
}

/**
 * Fill a frame at (seg, u). roll and width are (seg, u) => value; nodes carry flags and surface.
 */
export function frameAtSegU(sp, refN, rollAt, widthAt, nodes, seg, u, out) {
  sp.pos(seg, u, out.p);
  const t = sp.deriv(seg, u, out.t);
  const tl = Math.hypot(t[0], t[1], t[2]) || 1;
  t[0] /= tl; t[1] /= tl; t[2] /= tl;
  const x = u * sp.SUB, k = Math.min(Math.floor(x), sp.SUB - 1), f = x - k, j = (seg * sp.SUB + k) * 3;
  let nx = refN[j] + (refN[j + 3] - refN[j]) * f, ny = refN[j + 1] + (refN[j + 4] - refN[j + 1]) * f, nz = refN[j + 2] + (refN[j + 5] - refN[j + 2]) * f;
  const kk = nx * t[0] + ny * t[1] + nz * t[2];
  nx -= kk * t[0]; ny -= kk * t[1]; nz -= kk * t[2];
  const nl = Math.hypot(nx, ny, nz) || 1;
  nx /= nl; ny /= nl; nz /= nl;
  // b0 = t x n0, then roll: n = n0 cos r + b0 sin r
  const bx = t[1] * nz - t[2] * ny, by = t[2] * nx - t[0] * nz, bz = t[0] * ny - t[1] * nx;
  const r = rollAt(seg, u), c = Math.cos(r), s = Math.sin(r);
  const n = out.n, b = out.b;
  n[0] = nx * c + bx * s; n[1] = ny * c + by * s; n[2] = nz * c + bz * s;
  b[0] = t[1] * n[2] - t[2] * n[1]; b[1] = t[2] * n[0] - t[0] * n[2]; b[2] = t[0] * n[1] - t[1] * n[0];
  out.roll = r;
  out.w = widthAt(seg, u);
  out.flags = nodes[seg].flags;
  out.surface = nodes[seg].surface;
  out.seg = seg;
  const s0 = sp.nodeS[seg];
  out.segT = (sp.sFromU(seg, u) - s0) / (sp.nodeS[seg + 1] - s0);
  return out;
}
