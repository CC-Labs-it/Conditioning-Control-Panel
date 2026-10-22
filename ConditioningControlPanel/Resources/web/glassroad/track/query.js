/* track/query.js - the ground probe. No three.js.
 *
 * query(px, py, pz, hintSeg = -1, out?) -> { seg, segT, s, lateral, height, normal, onRoad, edgeDist,
 *   surface, flags, railed, p }
 * Nearest centreline point by a coarse pass over the arc-length sample polyline (bounding spheres prune
 * whole segments), then Newton on the exact curve until (P - c) . t = 0. With hintSeg >= 0 only the
 * hint +-WINDOW segments are searched; a result further than FAR_M from the curve searches them all.
 * height and lateral are measured in the rolled frame; p is the foot of the normal on the road plane
 * (lateral not clamped, so p runs past the edge when the kart does).
 */
import { makeFrame, frameAtSegU } from './frames.js';

const WINDOW = 4, FAR_M = 25;

export function makeQuery() {
  return { seg: 0, segT: 0, s: 0, lateral: 0, height: 0, normal: [0, 1, 0], onRoad: false, edgeDist: 0, surface: 'glass', flags: null, railed: false, p: [0, 0, 0] };
}

export function createQuery(sp, refN, rollAt, widthAt, nodes) {
  const { N, SUB, samplePos: S } = sp;
  // bounding sphere per segment over its samples
  const centre = new Float64Array(N * 3), radius = new Float64Array(N);
  for (let i = 0; i < N; i++) {
    let x = 0, y = 0, z = 0;
    for (let k = 0; k <= SUB; k++) { const j = (i * SUB + k) * 3; x += S[j]; y += S[j + 1]; z += S[j + 2]; }
    x /= SUB + 1; y /= SUB + 1; z /= SUB + 1;
    let r = 0;
    for (let k = 0; k <= SUB; k++) { const j = (i * SUB + k) * 3; r = Math.max(r, Math.hypot(S[j] - x, S[j + 1] - y, S[j + 2] - z)); }
    centre[i * 3] = x; centre[i * 3 + 1] = y; centre[i * 3 + 2] = z; radius[i] = r;
  }
  const fr = makeFrame(), c = [0, 0, 0], d1 = [0, 0, 0], d2 = [0, 0, 0];
  let bestD2 = 0, bestSeg = 0, bestU = 0;

  const scanSeg = (i, px, py, pz) => {
    const dc = Math.hypot(px - centre[i * 3], py - centre[i * 3 + 1], pz - centre[i * 3 + 2]) - radius[i];
    if (dc > 0 && dc * dc >= bestD2) return;
    for (let k = 0; k < SUB; k++) {
      const j = (i * SUB + k) * 3;
      const ax = S[j], ay = S[j + 1], az = S[j + 2];
      const ex = S[j + 3] - ax, ey = S[j + 4] - ay, ez = S[j + 5] - az;
      const wx = px - ax, wy = py - ay, wz = pz - az;
      const ee = ex * ex + ey * ey + ez * ez;
      let f = ee > 0 ? (wx * ex + wy * ey + wz * ez) / ee : 0;
      f = f < 0 ? 0 : f > 1 ? 1 : f;
      const dx = wx - ex * f, dy = wy - ey * f, dz = wz - ez * f, dd = dx * dx + dy * dy + dz * dz;
      if (dd < bestD2) { bestD2 = dd; bestSeg = i; bestU = (k + f) / SUB; }
    }
  };
  const scan = (px, py, pz, hint) => {
    bestD2 = Infinity;
    if (hint >= 0) for (let o = -WINDOW; o <= WINDOW; o++) scanSeg(((hint + o) % N + N) % N, px, py, pz);
    else for (let i = 0; i < N; i++) scanSeg(i, px, py, pz);
  };

  return function query(px, py, pz, hintSeg = -1, out = makeQuery()) {
    const hint = hintSeg >= 0 && hintSeg < N ? hintSeg | 0 : -1;
    scan(px, py, pz, hint);
    if (hint >= 0 && bestD2 > FAR_M * FAR_M) scan(px, py, pz, -1);
    // Newton on f(u) = (P - c(u)) . c'(u), stepping into a neighbour segment once if u leaves [0, 1]
    let seg = bestSeg, u = bestU, hops = 0;
    for (let it = 0; it < 6; it++) {
      sp.pos(seg, u, c); sp.deriv(seg, u, d1); sp.deriv2(seg, u, d2);
      const wx = px - c[0], wy = py - c[1], wz = pz - c[2];
      const f = wx * d1[0] + wy * d1[1] + wz * d1[2];
      const df = wx * d2[0] + wy * d2[1] + wz * d2[2] - (d1[0] * d1[0] + d1[1] * d1[1] + d1[2] * d1[2]);
      if (df >= 0) break;
      const du = -f / df;
      u += du;
      if (u < 0) { if (hops++ < 1) { seg = (seg - 1 + N) % N; u += 1; } else { u = 0; break; } }
      else if (u > 1) { if (hops++ < 1) { seg = (seg + 1) % N; u -= 1; } else { u = 1; break; } }
      if (Math.abs(du) < 1e-7) break;
    }
    u = u < 0 ? 0 : u > 1 ? 1 : u;
    frameAtSegU(sp, refN, rollAt, widthAt, nodes, seg, u, fr);
    const wx = px - fr.p[0], wy = py - fr.p[1], wz = pz - fr.p[2];
    const lat = wx * fr.b[0] + wy * fr.b[1] + wz * fr.b[2];
    const h = wx * fr.n[0] + wy * fr.n[1] + wz * fr.n[2];
    const gap = sp.gap[seg], half = fr.w / 2, fl = fr.flags;
    out.seg = seg;
    out.segT = fr.segT;
    const sa = sp.sFromU(seg, u);
    out.s = sa >= sp.lengthM ? sa - sp.lengthM : sa;
    out.lateral = lat;
    out.height = h;
    out.normal[0] = fr.n[0]; out.normal[1] = fr.n[1]; out.normal[2] = fr.n[2];
    out.edgeDist = half - Math.abs(lat);
    out.onRoad = !gap && out.edgeDist >= 0;
    out.surface = fr.surface;
    out.flags = fl;
    out.railed = !gap && (fl.includes('rail') || fl.includes(lat >= 0 ? 'railR' : 'railL'));
    out.p[0] = fr.p[0] + fr.b[0] * lat; out.p[1] = fr.p[1] + fr.b[1] * lat; out.p[2] = fr.p[2] + fr.b[2] * lat;
    return out;
  };
}
