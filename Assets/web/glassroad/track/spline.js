/* track/spline.js - closed centripetal Catmull-Rom through the node points, in Hermite form, plus the
 * arc-length tables. No three.js.
 *
 * Segment i runs node i -> node i+1 (wrapping), parameter u in [0, 1]:
 *   c(u) = a + b u + c u^2 + d u^3, stored as 12 numbers per segment.
 * Gaps (CONTRACT 5): the segment into a lip uses a phantom next point lip + (lip - prev), the landing
 * segment a phantom previous point land - (next - land). A gap segment itself is a Hermite arc that
 * leaves the lip on the kicker's tangent and meets the landing on its tangent, so frames stay smooth.
 * Arc length: SUB samples per segment (2-point Gauss per piece); s <-> u maps linearly between samples.
 */

export const SUB = 32;

const GL = 0.5 / Math.sqrt(3);

/**
 * @param {number[][]} pts node positions [x, y, z]
 * @param {boolean[]} gap gap[i] = segment i starts at a jump node (no road)
 */
export function createSpline(pts, gap) {
  const N = pts.length;
  const w = (i) => ((i % N) + N) % N;
  const P = (i) => pts[w(i)];
  const coef = new Float64Array(N * 12);

  const setHermite = (i, p1, p2, m1, m2) => {
    const o = i * 12;
    for (let k = 0; k < 3; k++) {
      coef[o + k] = p1[k];
      coef[o + 3 + k] = m1[k];
      coef[o + 6 + k] = 3 * (p2[k] - p1[k]) - 2 * m1[k] - m2[k];
      coef[o + 9 + k] = 2 * (p1[k] - p2[k]) + m1[k] + m2[k];
    }
  };
  // centripetal tangents (Barry-Goldman, scaled to the p1 -> p2 knot interval)
  const crTangents = (p0, p1, p2, p3) => {
    let d0 = Math.sqrt(dist(p0, p1)), d1 = Math.sqrt(dist(p1, p2)), d2 = Math.sqrt(dist(p2, p3));
    if (d1 < 1e-4) d1 = 1;
    if (d0 < 1e-4) d0 = d1;
    if (d2 < 1e-4) d2 = d1;
    const m1 = [0, 0, 0], m2 = [0, 0, 0];
    for (let k = 0; k < 3; k++) {
      m1[k] = d1 * ((p1[k] - p0[k]) / d0 - (p2[k] - p0[k]) / (d0 + d1) + (p2[k] - p1[k]) / d1);
      m2[k] = d1 * ((p2[k] - p1[k]) / d1 - (p3[k] - p1[k]) / (d1 + d2) + (p3[k] - p2[k]) / d2);
    }
    return [m1, m2];
  };
  const mirror = (a, b) => [2 * a[0] - b[0], 2 * a[1] - b[1], 2 * a[2] - b[2]];

  for (let i = 0; i < N; i++) {
    if (gap[i]) continue;
    const p1 = P(i), p2 = P(i + 1);
    const p0 = gap[w(i - 1)] ? mirror(p1, p2) : P(i - 1);
    const p3 = gap[w(i + 1)] ? mirror(p2, p1) : P(i + 2);
    const [m1, m2] = crTangents(p0, p1, p2, p3);
    setHermite(i, p1, p2, m1, m2);
  }
  for (let i = 0; i < N; i++) {
    if (!gap[i]) continue;
    const p1 = P(i), p2 = P(i + 1), L = dist(p1, p2);
    let [m1, m2] = crTangents(P(i - 1), p1, p2, P(i + 2));
    if (!gap[w(i - 1)]) m1 = scaleTo(derivAt(coef, w(i - 1), 1, [0, 0, 0]), L);
    if (!gap[w(i + 1)]) m2 = scaleTo(derivAt(coef, w(i + 1), 0, [0, 0, 0]), L);
    setHermite(i, p1, p2, m1, m2);
  }

  // arc-length samples: global j = seg * SUB + k, j = N * SUB closes the loop
  const M = N * SUB;
  const sampleS = new Float64Array(M + 1);
  const samplePos = new Float64Array((M + 1) * 3);
  const d = [0, 0, 0], q = [0, 0, 0];
  for (let i = 0; i < N; i++) {
    for (let k = 0; k < SUB; k++) {
      const j = i * SUB + k, u0 = k / SUB, h = 1 / SUB;
      posAt(coef, i, u0, q);
      samplePos[j * 3] = q[0]; samplePos[j * 3 + 1] = q[1]; samplePos[j * 3 + 2] = q[2];
      const la = len(derivAt(coef, i, u0 + h * (0.5 - GL), d)), lb = len(derivAt(coef, i, u0 + h * (0.5 + GL), d));
      sampleS[j + 1] = sampleS[j] + 0.5 * h * (la + lb);
    }
  }
  samplePos[M * 3] = samplePos[0]; samplePos[M * 3 + 1] = samplePos[1]; samplePos[M * 3 + 2] = samplePos[2];
  const lengthM = sampleS[M];
  const nodeS = new Float64Array(N + 1);
  for (let i = 0; i <= N; i++) nodeS[i] = sampleS[i * SUB];

  /** Segment holding arc length s (s already wrapped into [0, lengthM)). */
  const segAt = (s) => {
    let lo = 0, hi = N - 1;
    while (lo < hi) {
      const mid = (lo + hi + 1) >> 1;
      if (nodeS[mid] <= s) lo = mid; else hi = mid - 1;
    }
    return lo;
  };
  /** u on segment `seg` for arc length s inside it. */
  const uFromS = (seg, s) => {
    let lo = seg * SUB, hi = lo + SUB;
    if (s <= sampleS[lo]) return 0;
    if (s >= sampleS[hi]) return 1;
    while (hi - lo > 1) {
      const mid = (lo + hi) >> 1;
      if (sampleS[mid] <= s) lo = mid; else hi = mid;
    }
    const f = (s - sampleS[lo]) / (sampleS[hi] - sampleS[lo] || 1);
    return (lo - seg * SUB + f) / SUB;
  };
  /** Arc length at (seg, u), the exact inverse of uFromS. */
  const sFromU = (seg, u) => {
    const x = Math.min(Math.max(u, 0), 1) * SUB;
    const k = Math.min(Math.floor(x), SUB - 1), j = seg * SUB + k;
    return sampleS[j] + (sampleS[j + 1] - sampleS[j]) * (x - k);
  };

  return {
    N, SUB, coef, sampleS, samplePos, nodeS, lengthM, gap,
    segAt, uFromS, sFromU,
    pos: (seg, u, out) => posAt(coef, seg, u, out),
    deriv: (seg, u, out) => derivAt(coef, seg, u, out),
    deriv2: (seg, u, out) => deriv2At(coef, seg, u, out),
  };
}

export function posAt(coef, i, u, o) {
  const c = i * 12, u2 = u * u, u3 = u2 * u;
  for (let k = 0; k < 3; k++) o[k] = coef[c + k] + coef[c + 3 + k] * u + coef[c + 6 + k] * u2 + coef[c + 9 + k] * u3;
  return o;
}
export function derivAt(coef, i, u, o) {
  const c = i * 12;
  for (let k = 0; k < 3; k++) o[k] = coef[c + 3 + k] + 2 * coef[c + 6 + k] * u + 3 * coef[c + 9 + k] * u * u;
  return o;
}
export function deriv2At(coef, i, u, o) {
  const c = i * 12;
  for (let k = 0; k < 3; k++) o[k] = 2 * coef[c + 6 + k] + 6 * coef[c + 9 + k] * u;
  return o;
}

/**
 * Monotone cubic through per-node values on a closed loop (no overshoot between equal neighbours).
 * @returns {(seg: number, u: number) => number}
 */
export function createScalar(values) {
  const N = values.length, m = new Float64Array(N);
  for (let i = 0; i < N; i++) {
    const d0 = values[i] - values[(i - 1 + N) % N], d1 = values[(i + 1) % N] - values[i];
    if (d0 * d1 <= 0) m[i] = 0;
    else {
      const a = (d0 + d1) / 2, lim = 3 * Math.min(Math.abs(d0), Math.abs(d1));
      m[i] = Math.sign(a) * Math.min(Math.abs(a), lim);
    }
  }
  return (seg, u) => {
    const j = (seg + 1) % N, u2 = u * u, u3 = u2 * u;
    return (2 * u3 - 3 * u2 + 1) * values[seg] + (u3 - 2 * u2 + u) * m[seg] + (-2 * u3 + 3 * u2) * values[j] + (u3 - u2) * m[j];
  };
}

function dist(a, b) { return Math.hypot(b[0] - a[0], b[1] - a[1], b[2] - a[2]); }
function len(a) { return Math.hypot(a[0], a[1], a[2]); }
function scaleTo(v, L) { const l = len(v) || 1; return [v[0] * L / l, v[1] * L / l, v[2] * L / l]; }
