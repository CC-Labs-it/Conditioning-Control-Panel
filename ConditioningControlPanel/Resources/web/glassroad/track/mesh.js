/* track/mesh.js - MeshData for the road, rails, pads and jump markers. No three.js (render uploads it).
 *
 * MeshData: { positions, normals, uvs: Float32Array, indices: Uint32Array }. Triangles wind counter
 * clockwise seen from the side their normal points to (three.js FrontSide).
 * Road: top surface (u = lateral metres, + right; v = s metres, rising through the start line on a run
 * that wraps), a thin underside and edge skirt so the ribbon reads as glass, end caps where a run stops
 * at a gap. Rails: vertical ribbons on railed sides, inner and outer face, u = metres up, v = s.
 * Pads: a strip per boost 2 cm above the road, u = metres across from the pad centre, v = metres from 0.
 * Jumps: a lip marker strip over the last LIP_M metres before each lip, u = lateral, v from 0.
 */
import { makeFrame } from './frames.js';

const COLS = 4, THICK_M = 0.25, RAIL_H = 0.9, RAIL_T = 0.08, DECAL_M = 0.02, LIP_M = 3, STRIP_STEP_M = 1;

function builder() {
  const P = [], Nn = [], U = [], I = [];
  return {
    v(p, n, u, v) { P.push(p[0], p[1], p[2]); Nn.push(n[0], n[1], n[2]); U.push(u, v); return P.length / 3 - 1; },
    /** Quad a b c d (a b one row, c d the next, a c on the same side); winding follows normal of a. */
    quad(a, b, c, d) {
      const ax = P[a * 3], ay = P[a * 3 + 1], az = P[a * 3 + 2];
      const e1x = P[b * 3] - ax, e1y = P[b * 3 + 1] - ay, e1z = P[b * 3 + 2] - az;
      const e2x = P[d * 3] - ax, e2y = P[d * 3 + 1] - ay, e2z = P[d * 3 + 2] - az;
      const cx = e1y * e2z - e1z * e2y, cy = e1z * e2x - e1x * e2z, cz = e1x * e2y - e1y * e2x;
      if (cx * Nn[a * 3] + cy * Nn[a * 3 + 1] + cz * Nn[a * 3 + 2] >= 0) I.push(a, b, d, a, d, c);
      else I.push(a, d, b, a, c, d);
    },
    get size() { return P.length / 3; },
    done() {
      return { positions: new Float32Array(P), normals: new Float32Array(Nn), uvs: new Float32Array(U), indices: new Uint32Array(I) };
    },
  };
}

const at = (arr, r) => [arr[r * 3], arr[r * 3 + 1], arr[r * 3 + 2]];
const off = (c, b, x, n, y) => [c[0] + b[0] * x + n[0] * y, c[1] + b[1] * x + n[1] * y, c[2] + b[2] * x + n[2] * y];
const neg = (a) => [-a[0], -a[1], -a[2]];

/**
 * Runs of consecutive quads where keep(q) holds, as [startQuad, quadCount] with startQuad possibly
 * past the end (wrapping). A loop with no break gives one run over every quad.
 */
function runs(Q, keep) {
  let first = -1;
  for (let q = 0; q < Q; q++) if (!keep(q)) { first = q; break; }
  if (first < 0) return [[0, Q, true]];
  const out = [];
  let start = -1;
  for (let k = 1; k <= Q; k++) {
    const q = first + k, ok = k < Q && keep(q % Q);
    if (ok && start < 0) start = q;
    if (!ok && start >= 0) { out.push([start, q - start, false]); start = -1; }
  }
  return out;
}

export function buildMeshes(track, dl) {
  const Q = dl.quads, L = track.lengthM;
  const row = (q) => q % Q;              // row index of the start of quad q (wrapped)
  const rowS = (q) => dl.s[row(q)] + Math.floor(q / Q) * L;

  // road
  const road = builder();
  for (const [q0, nq, closed] of runs(Q, (q) => !dl.gap[q])) {
    const top = [], bot = [], el = [], er = [];
    for (let k = 0; k <= nq; k++) {
      const r = closed && k === nq ? Q : row(q0 + k), v = closed && k === nq ? L : rowS(q0 + k);
      const c = at(dl.c, r), n = at(dl.n, r), b = at(dl.b, r), h = dl.w[r] / 2, nn = neg(n);
      const tr = [], br = [];
      for (let i = 0; i <= COLS; i++) {
        const x = -h + (2 * h * i) / COLS;
        tr.push(road.v(off(c, b, x, n, 0), n, x, v));
        br.push(road.v(off(c, b, x, n, -THICK_M), nn, x, v));
      }
      top.push(tr); bot.push(br);
      el.push([road.v(off(c, b, -h, n, 0), neg(b), 0, v), road.v(off(c, b, -h, n, -THICK_M), neg(b), THICK_M, v)]);
      er.push([road.v(off(c, b, h, n, 0), b, 0, v), road.v(off(c, b, h, n, -THICK_M), b, THICK_M, v)]);
    }
    for (let k = 0; k < nq; k++) {
      for (let i = 0; i < COLS; i++) {
        road.quad(top[k][i], top[k][i + 1], top[k + 1][i], top[k + 1][i + 1]);
        road.quad(bot[k][i], bot[k][i + 1], bot[k + 1][i], bot[k + 1][i + 1]);
      }
      road.quad(el[k][0], el[k][1], el[k + 1][0], el[k + 1][1]);
      road.quad(er[k][0], er[k][1], er[k + 1][0], er[k + 1][1]);
    }
    if (!closed) {
      for (const [k, sign] of [[0, -1], [nq, 1]]) {
        const r = row(q0 + k), c = at(dl.c, r), n = at(dl.n, r), b = at(dl.b, r), h = dl.w[r] / 2;
        const t = at(dl.t, r), tn = [t[0] * sign, t[1] * sign, t[2] * sign];
        const a = road.v(off(c, b, -h, n, 0), tn, -h, 0), bb = road.v(off(c, b, h, n, 0), tn, h, 0);
        const cc = road.v(off(c, b, -h, n, -THICK_M), tn, -h, THICK_M), d = road.v(off(c, b, h, n, -THICK_M), tn, h, THICK_M);
        road.quad(a, bb, cc, d);
      }
    }
  }

  // rails
  const rails = builder();
  const railed = (q, side) => {
    if (dl.gap[q]) return false;
    const fl = track.nodeFlags(dl.seg[q]);
    if (fl.includes('rail') || fl.includes(side > 0 ? 'railR' : 'railL')) return true;
    // a drop shortcut's open side keeps its rail everywhere but the opening (polish 4)
    const sm = (dl.s[q] + dl.s[q + 1]) / 2;
    return (track.routes || []).some((r) => dl.seg[q] >= r.from && dl.seg[q] < r.to && (r.side === 'L' ? side < 0 : side > 0) && (sm < r.openS0 || sm > r.openS1));
  };
  for (const side of [-1, 1]) {
    for (const [q0, nq, closed] of runs(Q, (q) => railed(q, side))) {
      const inner = [], outer = [];
      for (let k = 0; k <= nq; k++) {
        const r = closed && k === nq ? Q : row(q0 + k), v = closed && k === nq ? L : rowS(q0 + k);
        const c = at(dl.c, r), n = at(dl.n, r), b = at(dl.b, r), x = side * dl.w[r] / 2;
        const inN = side > 0 ? neg(b) : b, outN = neg(inN);
        inner.push([rails.v(off(c, b, x, n, 0), inN, 0, v), rails.v(off(c, b, x, n, RAIL_H), inN, RAIL_H, v)]);
        const xo = x + side * RAIL_T;
        outer.push([rails.v(off(c, b, xo, n, 0), outN, 0, v), rails.v(off(c, b, xo, n, RAIL_H), outN, RAIL_H, v)]);
      }
      for (let k = 0; k < nq; k++) {
        rails.quad(inner[k][0], inner[k][1], inner[k + 1][0], inner[k + 1][1]);
        rails.quad(outer[k][0], outer[k][1], outer[k + 1][0], outer[k + 1][1]);
      }
    }
  }

  // decal strips that follow the surface: s0..s0+len, lateral x0 - w/2 .. x0 + w/2
  const f = makeFrame();
  const strip = (m, s0, len, x0, w, uCentre) => {
    const rowsN = Math.max(1, Math.ceil(len / STRIP_STEP_M)), ids = [];
    for (let k = 0; k <= rowsN; k++) {
      const v = (len * k) / rowsN;
      track.frameAt(s0 + v, f);
      ids.push([-1, 1].map((sd) => {
        const x = x0 + (sd * w) / 2;
        return m.v(off(f.p, f.b, x, f.n, DECAL_M), f.n, uCentre ? x - x0 : x, v);
      }));
    }
    for (let k = 0; k < rowsN; k++) m.quad(ids[k][0], ids[k][1], ids[k + 1][0], ids[k + 1][1]);
  };
  const pads = builder();
  for (const p of track.boosts) strip(pads, p.s, p.len, p.x, p.w, true);
  const jumps = builder();
  for (const j of track.jumps) {
    const w = track.frameAt(j.s - LIP_M).w;
    strip(jumps, j.s - LIP_M, LIP_M, 0, w, false);
  }
  const { routeEdge, routeLand } = buildRouteMeshes(track, builder);
  return { road: road.done(), rails: rails.done(), pads: pads.done(), jumps: jumps.done(), routeEdge, routeLand };
}

/* Polish 4 drop shortcuts (render/routes.js draws these). uv.x = a 0..1 coordinate a slow glow wave runs
 * along (outward across the gap, round the ring, forward on the arrows), uv.y = that piece's gain.
 * routeEdge: a low kerb strip along the open edge, chevrons pointing off it, a glowing post at each end of
 * the break. routeLand: the soft catch zone, a ring round the landing pad and forward arrows inside it. */
const KERB_W = 0.5, KERB_UP = 0.06, POST_H = 3.2, POST_W = 0.5, CHEV_STEP_M = 4, CHEV_ARM = 1.5, CHEV_W = 0.45;
const RING_R = 4.6, RING_W = 0.45, RING_SEGS = 48, ZONE_LEN = 24, ZONE_HALF = 3.5, ZONE_LINE = 0.25;

function buildRouteMeshes(track, builder) {
  const edge = builder(), land = builder(), f = makeFrame();
  const rs = track.routes || [];
  // a flat quad on the road at frame fr: corners as [lateral, ahead] pairs (a b one row, c d the next)
  const flat = (m, fr, pts, us, gain, lift = DECAL_M) => {
    const ids = pts.map(([x, d], i) => m.v([fr.p[0] + fr.b[0] * x + fr.t[0] * d + fr.n[0] * lift, fr.p[1] + fr.b[1] * x + fr.t[1] * d + fr.n[1] * lift,
      fr.p[2] + fr.b[2] * x + fr.t[2] * d + fr.n[2] * lift], fr.n, us[i], gain));
    m.quad(ids[0], ids[1], ids[2], ids[3]);
  };
  for (const r of rs) {
    const side = r.side === 'L' ? -1 : 1, s0 = r.openS0, len = r.openS1 - r.openS0;
    // the kerb: follows the opening, u runs along it
    const n = Math.max(2, Math.ceil(len / STRIP_STEP_M));
    let prev = null;
    for (let k = 0; k <= n; k++) {
      track.frameAt(s0 + (len * k) / n, f);
      const row = [side * (f.w / 2 - KERB_W), side * f.w / 2].map((x) => edge.v(off(f.p, f.b, x, f.n, KERB_UP), f.n, k / n, 0.8));
      if (prev) edge.quad(prev[0], prev[1], row[0], row[1]);
      prev = row;
    }
    // chevrons pointing off the edge, three across, through the opening and a little before it
    for (let d = len / 2 - CHEV_STEP_M * 2; d <= len / 2 + 1e-6; d += CHEV_STEP_M) {
      track.frameAt(s0 + d, f);
      for (let j = 0; j < 3; j++) {
        const tip = side * (f.w / 2 - 0.9 - j * 1.9), back = tip - side * CHEV_ARM, u = 1 - j / 3;
        for (const s of [-1, 1]) {
          const pts = [[tip, 0], [tip - side * CHEV_W, 0], [back, s * CHEV_ARM], [back - side * CHEV_W, s * CHEV_ARM]];
          flat(edge, f, pts, [u, u, u - 0.1, u - 0.1], 1);
        }
      }
    }
    // glowing posts at both ends of the break (two crossed vertical quads each)
    for (const s of [r.openS0, r.openS1]) {
      track.frameAt(s, f);
      const base = off(f.p, f.b, side * f.w / 2, f.n, 0);
      const up = (p) => [p[0] + f.n[0] * POST_H, p[1] + f.n[1] * POST_H, p[2] + f.n[2] * POST_H];
      for (const [ax, nrm] of [[f.t, f.b], [f.b, f.t]]) {
        const a = [base[0] - ax[0] * POST_W / 2, base[1] - ax[1] * POST_W / 2, base[2] - ax[2] * POST_W / 2];
        const b = [base[0] + ax[0] * POST_W / 2, base[1] + ax[1] * POST_W / 2, base[2] + ax[2] * POST_W / 2];
        edge.quad(edge.v(a, nrm, 0, 1.2), edge.v(b, nrm, 0, 1.2), edge.v(up(a), nrm, 1, 1.2), edge.v(up(b), nrm, 1, 1.2));
      }
    }
    // the landing: soft catch zone (fill and side lines), the ring, two forward arrows
    const fill = (x0, x1, gain, lift) => {
      const m = Math.ceil(ZONE_LEN / STRIP_STEP_M), rows = [];
      for (let k = 0; k <= m; k++) {
        const fr = track.frameAt(r.pad.s - ZONE_LEN / 2 + (ZONE_LEN * k) / m, f);
        rows.push([x0, x1].map((x) => land.v(off(fr.p, fr.b, r.pad.x + x, fr.n, lift), fr.n, k / m, gain)));
      }
      for (let k = 0; k < m; k++) land.quad(rows[k][0], rows[k][1], rows[k + 1][0], rows[k + 1][1]);
    };
    fill(-ZONE_HALF, ZONE_HALF, 0.22, DECAL_M * 0.5);
    fill(-ZONE_HALF, -ZONE_HALF + ZONE_LINE, 0.9, DECAL_M);
    fill(ZONE_HALF - ZONE_LINE, ZONE_HALF, 0.9, DECAL_M);
    track.frameAt(r.pad.s, f);
    const cx = r.pad.x, ring = [];
    for (let k = 0; k <= RING_SEGS; k++) {
      const a = (k / RING_SEGS) * Math.PI * 2, ca = Math.cos(a), sa = Math.sin(a);
      ring.push([RING_R - RING_W, RING_R].map((rr) => land.v([
        f.p[0] + f.b[0] * (cx + ca * rr) + f.t[0] * sa * rr + f.n[0] * DECAL_M * 2,
        f.p[1] + f.b[1] * (cx + ca * rr) + f.t[1] * sa * rr + f.n[1] * DECAL_M * 2,
        f.p[2] + f.b[2] * (cx + ca * rr) + f.t[2] * sa * rr + f.n[2] * DECAL_M * 2], f.n, k / RING_SEGS, 1)));
    }
    for (let k = 0; k < RING_SEGS; k++) land.quad(ring[k][0], ring[k][1], ring[k + 1][0], ring[k + 1][1]);
    for (const [d0, u] of [[-1.6, 0.3], [0.6, 0.7]]) {
      for (const s of [-1, 1]) {
        const pts = [[cx, d0 + 1.4], [cx, d0 + 1.4 - CHEV_W * 1.5], [cx + s * 1.6, d0], [cx + s * 1.6, d0 - CHEV_W * 1.5]];
        flat(land, f, pts, [u, u, u - 0.15, u - 0.15], 1, DECAL_M * 2);
      }
    }
  }
  return { routeEdge: edge.done(), routeLand: land.done() };
}
