/* track/mesh.js - MeshData for the road, rails, pads and jump markers. No three.js (render uploads it).
 *
 * MeshData: { positions, normals, uvs: Float32Array, indices: Uint32Array }. Triangles wind counter
 * clockwise seen from the side their normal points to (three.js FrontSide).
 * Road: top surface (u = lateral metres, + right; v = s metres, rising through the start line on a run
 * that wraps), a thin underside and edge skirt so the ribbon reads as glass, end caps where a run stops
 * at a gap. Rails: vertical ribbons on railed sides, inner and outer face, u = metres up, v = s.
 * Pads: a strip per boost 2 cm above the road, u = metres across from the pad centre, v = metres from 0.
 * Jumps: a lip marker strip over the last LIP_M metres before each lip, u = lateral, v from 0.
 * Edges (EDGES lane, render/edges.js draws them): edgeStrip, a hazard strip painted along every open edge (u =
 * metres in from the edge, v = s); edgeHaze, a curtain hanging from that lip into the drop (u = metres down,
 * v = s); railCaps, a tall end post wherever a rail run starts or stops (u = share of its height).
 */
import { makeFrame } from './frames.js';
import { supportIntervals } from './courts.js';
import { surfaceId } from './schema.js';
import { bendCanalMesh } from './canal.js';
import { createRoadDepth } from './roadDepth.js';

// RAIL_H: 1.2 m since the EDGES lane (was 0.9: a railed edge read too like an open one); render/look.js railHeightM matches
const COLS = 4, THICK_M = 0.25, RAIL_H = 1.2, RAIL_T = 0.08, DECAL_M = 0.02, LIP_M = 3, STRIP_STEP_M = 1;
/** Edge numbers (EDGES lane): the open-edge strip width, the haze curtain's depth and flare, the rail end post. */
export const EDGE_MESH = Object.freeze({ stripM: 1.2, hazeM: 7, hazeOutM: 2.2, capW: 0.34, capH: 2.1, capSkipM: 3 });

/* water: the road builder also carries a float `surface` (schema SURFACES index: 0 glass, 1 grip, 2 haze, 3 stream,
 * 4 pool) and a vec2 `flow` ([along, lat] m/s) per vertex; the other MeshData keep their four arrays. */
function builder(water = false) {
  const P = [], Nn = [], U = [], I = [], SF = [], FL = [];
  return {
    v(p, n, u, v, sf = 0, fa = 0, fl = 0) {
      P.push(p[0], p[1], p[2]); Nn.push(n[0], n[1], n[2]); U.push(u, v);
      if (water) { SF.push(sf); FL.push(fa, fl); }
      return P.length / 3 - 1;
    },
    /** Quad a b c d (a b one row, c d the next, a c on the same side); winding follows normal of a. */
    quad(a, b, c, d) {
      const ax = P[a * 3], ay = P[a * 3 + 1], az = P[a * 3 + 2];
      const e1x = P[b * 3] - ax, e1y = P[b * 3 + 1] - ay, e1z = P[b * 3 + 2] - az;
      const e2x = P[d * 3] - ax, e2y = P[d * 3 + 1] - ay, e2z = P[d * 3 + 2] - az;
      const cx = e1y * e2z - e1z * e2y, cy = e1z * e2x - e1x * e2z, cz = e1x * e2y - e1y * e2x;
      const forward = cx * Nn[a * 3] + cy * Nn[a * 3 + 1] + cz * Nn[a * 3 + 2] >= 0;
      if (forward) I.push(a, b, d, a, d, c);
      else I.push(a, d, b, a, c, d);
      return forward;
    },
    triangle(a, b, c) {
      // UV clipping can leave micrometre corners that collapse in the uploaded Float32 positions.
      // Keep every visible sliver, but never submit an empty or inverted physical triangle.
      const edge = (i) => [0, 1, 2].map(k => Math.fround(P[i * 3 + k]) - Math.fround(P[a * 3 + k]));
      const u = edge(b), v = edge(c), cross = [u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0]];
      if (Math.hypot(...cross) > 1e-10 && cross.reduce((sum, n, k) => sum + n * Nn[a * 3 + k], 0) > 0) I.push(a, b, c);
    },
    get size() { return P.length / 3; },
    done() {
      const m = { positions: new Float32Array(P), normals: new Float32Array(Nn), uvs: new Float32Array(U), indices: new Uint32Array(I) };
      if (water) { m.surface = new Float32Array(SF); m.flow = new Float32Array(FL); }
      return m;
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
  const depthAt = createRoadDepth(track);
  const columns = track.canalProfile ? 18 : COLS;
  const curvedPads = new Set((track.boosts || []).filter(p => {
    if (!track.canalProfile) return false;
    for (let ds = 0; ds <= p.len; ds += Math.min(STRIP_STEP_M, p.len))
      if (track.canalProfile(p.s + ds, p.x).blend > 0) return true;
    return false;
  }));
  const curvedPadList = [...curvedPads];
  const padRoadFaces = [];
  const padOverlaps = (p, s0, s1) => {
    // Wrapped runs carry unwrapped Float32 UVs. Use that same representation here:
    // rounding can move a row across the pad edge, requiring the preceding face.
    const uv0 = Math.fround(s0), uv1 = Math.fround(s1);
    const shift = Math.round(((p.s + p.len / 2) - (uv0 + uv1) / 2) / L) * L;
    return uv0 + shift < p.s + p.len && uv1 + shift > p.s;
  };
  const courtHoles = (track.courts || []).flatMap((c) => c.holes);
  const row = (q) => q % Q;              // row index of the start of quad q (wrapped)
  const rowS = (q) => dl.s[row(q)] + Math.floor(q / Q) * L;
  const edgeEndS = (q,side) => {
    const s=rowS(q), before=q?rowS(q-1):rowS(Q-1)-L;
    let best=Math.max(s-before,rowS(q+1)-s), result=s;
    for (const route of track.routes || []) {
      if ((route.side==='L'?-1:1)!==side) continue;
      for (const bound of [route.openS0,route.openS1]) {
        const candidate=bound+Math.round((s-bound)/L)*L, distance=Math.abs(candidate-s);
        if (distance<best) { best=distance;result=candidate; }
      }
    }
    return result;
  };
  // stones wave: inside a field the deck is gone (no road, no hazard strip; RENDER places the stones), so the
  // road and the open-edge runs break at the field's ends. With no fields every run is as before.
  const fields = track.fields || [];
  const inField = (q) => {
    if (!fields.length) return false;
    const sm = (((rowS(q) + rowS(q + 1)) / 2) % L + L) % L;
    for (let i = 0; i < fields.length; i++) if (sm >= fields[i].fromS && sm < fields[i].toS) return true;
    return false;
  };

  // road (water: every road vertex of a row carries its node's surface id and the flow lerped at that row)
  const road = builder(true), fw = [0, 0];
  const rowWater = (r) => {
    const seg = r >= Q ? dl.seg[Q - 1] : dl.seg[r], k = r >= Q ? dl.rows : r - seg * dl.rows;
    track.flowAt(seg, k / dl.rows, fw);
    return surfaceId(track.nodeSurface(seg));
  };
  for (const [q0, nq, closed] of runs(Q, (q) => !dl.gap[q] && !inField(q))) {
    const top = [], bot = [], el = [], er = [];
    for (let k = 0; k <= nq; k++) {
      const r = closed && k === nq ? Q : row(q0 + k), v = closed && k === nq ? L : rowS(q0 + k);
      const c = at(dl.c, r), n = at(dl.n, r), b = at(dl.b, r), h = dl.w[r] / 2, nn = neg(n);
      const sf = rowWater(r), fa = fw[0], fl = fw[1];
      const thick = depthAt(v);
      const tr = [], br = [];
      for (let i = 0; i <= columns; i++) {
        const x = -h + (2 * h * i) / columns;
        tr.push(road.v(off(c, b, x, n, 0), n, x, v, sf, fa, fl));
        br.push(road.v(off(c, b, x, n, -thick), nn, x, v, sf, fa, fl));
      }
      top.push(tr); bot.push(br);
      el.push([road.v(off(c, b, -h, n, 0), neg(b), 0, v, sf, fa, fl), road.v(off(c, b, -h, n, -thick), neg(b), thick, v, sf, fa, fl)]);
      er.push([road.v(off(c, b, h, n, 0), b, 0, v, sf, fa, fl), road.v(off(c, b, h, n, -thick), b, thick, v, sf, fa, fl)]);
    }
    for (let k = 0; k < nq; k++) {
      const rawS = rowS(q0 + k), offsetS = Math.floor(rawS / L) * L;
      const s0 = rawS - offsetS, s1 = rowS(q0 + k + 1) - offsetS;
      const holes = courtHoles.filter((h) => h.s < s1 && h.s + h.len > s0);
      const underCurvedPad = curvedPadList.some(p => padOverlaps(p, rawS, rowS(q0 + k + 1)));
      if (holes.length) courtStrip(road, track, s0, s1, holes, offsetS, depthAt);
      else for (let i = 0; i < columns; i++) {
        const a = top[k][i], b = top[k][i + 1], c = top[k + 1][i], d = top[k + 1][i + 1];
        const forward = road.quad(a, b, c, d);
        if (underCurvedPad) padRoadFaces.push(...(forward ? [[a, b, d], [a, d, c]] : [[a, d, b], [a, c, d]]));
        road.quad(bot[k][i], bot[k][i + 1], bot[k + 1][i], bot[k + 1][i + 1]);
      }
      road.quad(el[k][0], el[k][1], el[k + 1][0], el[k + 1][1]);
      road.quad(er[k][0], er[k][1], er[k + 1][0], er[k + 1][1]);
    }
    if (!closed) {
      for (const [k, sign] of [[0, -1], [nq, 1]]) {
        const r = row(q0 + k), c = at(dl.c, r), n = at(dl.n, r), b = at(dl.b, r), h = dl.w[r] / 2;
        const t = at(dl.t, r), tn = [t[0] * sign, t[1] * sign, t[2] * sign], sf = rowWater(r), fa = fw[0], fl = fw[1];
        const thick = depthAt(rowS(q0 + k));
        const a = road.v(off(c, b, -h, n, 0), tn, -h, 0, sf, fa, fl), bb = road.v(off(c, b, h, n, 0), tn, h, 0, sf, fa, fl);
        const cc = road.v(off(c, b, -h, n, -thick), tn, -h, thick, sf, fa, fl), d = road.v(off(c, b, h, n, -thick), tn, h, thick, sf, fa, fl);
        road.quad(a, bb, cc, d);
      }
    }
  }
  courtWalls(road, track, depthAt);

  // rails
  const rails = builder();
  const railed = (q, side) => {
    if (dl.gap[q]) return false;
    if (track.mainRailOpen?.((dl.s[q] + dl.s[q + 1]) / 2, side)) return false;
    const fl = track.nodeFlags(dl.seg[q]);
    if (fl.includes('rail') || fl.includes(side > 0 ? 'railR' : 'railL')) return true;
    // a drop shortcut's open side keeps its rail everywhere but the opening (polish 4)
    const sm = (dl.s[q] + dl.s[q + 1]) / 2;
    return (track.routes || []).some((r) => dl.seg[q] >= r.from && dl.seg[q] < r.to && (r.side === 'L' ? side < 0 : side > 0) && (sm < r.openS0 || sm > r.openS1));
  };
  const railEnds = [];
  for (const side of [-1, 1]) {
    for (const [q0, nq, closed] of runs(Q, (q) => railed(q, side))) {
      if (!closed) railEnds.push([edgeEndS(q0,side), side, -1], [edgeEndS(q0 + nq,side), side, 1]);
      const inner = [], outer = [];
      for (let k = 0; k <= nq; k++) {
        const r = closed && k === nq ? Q : row(q0 + k), raw = closed && k === nq ? L : rowS(q0 + k);
        const v = !closed && (k===0 || k===nq) ? edgeEndS(q0+k,side) : raw;
        const f = v!==raw ? track.frameAt(v) : null;
        const c = f?.p ?? at(dl.c, r), n = f?.n ?? at(dl.n, r), b = f?.b ?? at(dl.b, r), x = side * (f?.w ?? dl.w[r]) / 2;
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
  const roadData = bendCanalMesh(road.done(), track);
  const f = makeFrame();
  const strip = (m, s0, len, x0, w, uCentre) => {
    const rowsN = Math.max(1, Math.ceil(len / STRIP_STEP_M)), ids = [];
    for (let k = 0; k <= rowsN; k++) {
      const v = (len * k) / rowsN;
      track.frameAt(s0 + v, f);
      ids.push([-1, 1].map((sd) => {
        const x = x0 + (sd * w) / 2;
        const surface = track.surfaceAt ? track.surfaceAt(s0 + v, x) : null;
        return m.v(surface ? off(surface.p, surface.b, 0, surface.n, DECAL_M) : off(f.p, f.b, x, f.n, DECAL_M), surface ? surface.n : f.n, uCentre ? x - x0 : x, v);
      }));
    }
    for (let k = 0; k < rowsN; k++) m.quad(ids[k][0], ids[k][1], ids[k + 1][0], ids[k + 1][1]);
  };
  const pads = builder(), padDesign = [];
  for (const [index, p] of track.boosts.entries()) {
    const first = pads.size;
    if (curvedPads.has(p)) roadDecal(pads, roadData, padRoadFaces, p, L);
    else strip(pads, p.s, p.len, p.x, p.w, true);
    for (let i = first; i < pads.size; i++) padDesign.push(p.w * (index % 2 ? 1 : -1), p.len, p.s, p.x);
  }
  const jumps = builder();
  for (const j of track.jumps) {
    const w = track.frameAt(j.s - LIP_M).w;
    strip(jumps, j.s - LIP_M, LIP_M, 0, w, false);
  }
  for (const a of track.aerials || []) strip(jumps, a.gapS0 - LIP_M, LIP_M, 0, track.frameAt(a.gapS0).w, false);
  const { routeEdge, routeLand } = buildRouteMeshes(track, builder);
  const { edgeStrip, edgeHaze, railCaps } = buildEdgeMeshes(track, dl, runs, railed, railEnds, row, rowS, inField, edgeEndS);
  return { road: roadData, rails: bendCanalMesh(rails.done(), track), pads: { ...pads.done(), padDesign: new Float32Array(padDesign) }, jumps: jumps.done(), routeEdge, routeLand, edgeStrip, edgeHaze, railCaps };
}

/** Clip curved pads onto the rendered road's triangles. Sampling just the lateral edges floats
 * the pad across a canal; sampling the exact curve can bury it beneath the road's wider chords. */
function roadDecal(out, road, faces, pad, lapM) {
  const bounds = [[0, pad.x - pad.w / 2, 1], [0, pad.x + pad.w / 2, -1], [1, pad.s, 1], [1, pad.s + pad.len, -1]];
  for (const face of faces) {
    const meanS = face.reduce((sum, i) => sum + road.uvs[i * 2 + 1], 0) / 3;
    const shift = Math.round((pad.s + pad.len / 2 - meanS) / lapM) * lapM;
    let poly = face.map(i => ({ uv: [road.uvs[i * 2], road.uvs[i * 2 + 1] + shift],
      p: Array.from(road.positions.subarray(i * 3, i * 3 + 3)), n: Array.from(road.normals.subarray(i * 3, i * 3 + 3)) }));
    if (bounds.some(([axis, edge, sign]) => poly.every(v => (v.uv[axis] - edge) * sign < -1e-6))) continue;
    for (const [axis, edge, sign] of bounds) {
      const clipped = [];
      for (let i = 0; i < poly.length; i++) {
        const a = poly[i], b = poly[(i + 1) % poly.length];
        const da = (a.uv[axis] - edge) * sign, db = (b.uv[axis] - edge) * sign;
        if (da >= 0) clipped.push(a);
        if ((da >= 0) !== (db >= 0)) {
          const t = da / (da - db), lerp = key => a[key].map((v, k) => v + (b[key][k] - v) * t);
          clipped.push({ uv: lerp('uv'), p: lerp('p'), n: lerp('n') });
        }
      }
      poly = clipped;
      if (poly.length < 3) break;
    }
    poly = poly.filter((v, i) => {
      const prev = poly[(i + poly.length - 1) % poly.length];
      return Math.hypot(v.uv[0] - prev.uv[0], v.uv[1] - prev.uv[1]) > 1e-7;
    });
    if (poly.length < 3) continue;
    const ids = poly.map(v => {
      const length = Math.hypot(...v.n), n = v.n.map(value => value / length);
      return out.v(v.p.map((value, k) => value + n[k] * DECAL_M), n, v.uv[0] - pad.x, v.uv[1] - pad.s);
    });
    for (let i = 1; i < ids.length - 1; i++) out.triangle(ids[0], ids[i], ids[i + 1]);
  }
}

/** Split only affected road quads, at the exact collision bounds, on both visible faces. */
function courtStrip(m, track, s0, s1, holes, offsetS, depthAt) {
  const cuts = [s0, s1];
  for (const h of holes) for (const s of [h.s, h.s + h.len]) if (s > s0 && s < s1) cuts.push(s);
  cuts.sort((a, b) => a - b);
  for (let j = 1; j < cuts.length; j++) {
    const a = cuts[j - 1], b = cuts[j];
    if (b - a < 1e-8) continue;
    const f0 = track.frameAt(a), f1 = track.frameAt(b), fm = track.frameAt((a + b) / 2), half = fm.w / 2;
    const w0 = [surfaceId(f0.surface), ...track.flowAt(f0.seg, f0.segT, [0, 0])], w1 = [surfaceId(f1.surface), ...track.flowAt(f1.seg, f1.segT, [0, 0])];
    for (const [lo, hi] of supportIntervals(track.courts, (a + b) / 2, half)) {
      const left0 = Math.abs(lo + half) < 1e-6 ? -f0.w / 2 : lo, left1 = Math.abs(lo + half) < 1e-6 ? -f1.w / 2 : lo;
      const right0 = Math.abs(hi - half) < 1e-6 ? f0.w / 2 : hi, right1 = Math.abs(hi - half) < 1e-6 ? f1.w / 2 : hi;
      const cols = Math.max(1, Math.ceil((hi - lo) / 4));
      for (const lower of [false, true]) {
        let prev = null;
        for (let k = 0; k <= cols; k++) {
          const x0 = left0 + (right0 - left0) * k / cols, x1 = left1 + (right1 - left1) * k / cols;
          const ids = [m.v(off(f0.p, f0.b, x0, f0.n, lower ? -depthAt(a) : 0), lower ? neg(f0.n) : f0.n, x0, a + offsetS, ...w0),
            m.v(off(f1.p, f1.b, x1, f1.n, lower ? -depthAt(b) : 0), lower ? neg(f1.n) : f1.n, x1, b + offsetS, ...w1)];
          if (prev) m.quad(prev[0], ids[0], prev[1], ids[1]);
          prev = ids;
        }
      }
    }
  }
}

/** The exposed glass thickness inside each opening; no bottom cap spans a hole. */
function courtWalls(m, track, depthAt) {
  for (const c of track.courts || []) for (const h of c.holes) {
    for (const side of [-1, 1]) {
      let prev = null;
      const n = Math.max(1, Math.ceil(h.len / STRIP_STEP_M)), x = h.x + side * h.w / 2;
      for (let k = 0; k <= n; k++) {
        const s = h.s + h.len * k / n, f = track.frameAt(s), normal = f.b.map((v) => -side * v), wf = [surfaceId(f.surface), ...track.flowAt(f.seg, f.segT, [0, 0])];
        const ids = [0, -depthAt(s)].map((d) => m.v(off(f.p, f.b, x, f.n, d), normal, x, s, ...wf));
        if (prev) m.quad(prev[0], prev[1], ids[0], ids[1]);
        prev = ids;
      }
    }
    for (const [s, sign] of [[h.s, 1], [h.s + h.len, -1]]) {
      const f = track.frameAt(s), normal = f.t.map((v) => sign * v), wf = [surfaceId(f.surface), ...track.flowAt(f.seg, f.segT, [0, 0])];
      const ids = [0, -depthAt(s)].flatMap((d) => [-1, 1].map((side) => {
        const x = h.x + side * h.w / 2;
        return m.v(off(f.p, f.b, x, f.n, d), normal, x, s, ...wf);
      }));
      m.quad(ids[0], ids[1], ids[2], ids[3]);
    }
  }
}

/* EDGES lane: the open edges read "you can fall here" (a hazard strip and a lip, a haze falling away below it)
 * and every rail run ends in a tall post, so a railed edge and an open one differ in shape, not colour alone. */
function buildEdgeMeshes(track, dl, runs, railed, railEnds, row, rowS, inField = () => false, edgeEndS = rowS) {
  const E = EDGE_MESH, Q = dl.quads, L = track.lengthM;
  const strip = builder(), haze = builder(), caps = builder();
  for (const side of [-1, 1]) {
    for (const [q0, nq, closed] of runs(Q, (q) => !dl.gap[q] && !railed(q, side) && !inField(q) && !track.mainRailOpen?.((dl.s[q] + dl.s[q + 1]) / 2, side))) {
      const sr = [], hr = [];
      for (let k = 0; k <= nq; k++) {
        const r = closed && k === nq ? Q : row(q0 + k), raw = closed && k === nq ? L : rowS(q0 + k);
        const v = !closed && (k===0 || k===nq) ? edgeEndS(q0+k,side) : raw;
        const f = v!==raw ? track.frameAt(v) : null;
        const c = f?.p ?? at(dl.c, r), n = f?.n ?? at(dl.n, r), b = f?.b ?? at(dl.b, r), h = (f?.w ?? dl.w[r]) / 2, out = side > 0 ? b : neg(b);
        sr.push([strip.v(off(c, b, side * (h - E.stripM), n, DECAL_M), n, E.stripM, v), strip.v(off(c, b, side * h, n, DECAL_M), n, 0, v)]);
        const top = off(c, b, side * (h + 0.03), n, 0), low = off(c, b, side * (h + 0.03 + E.hazeOutM), n, -E.hazeM);
        hr.push([haze.v(top, out, 0, v), haze.v(low, out, E.hazeM, v)]);
      }
      for (let k = 0; k < nq; k++) {
        strip.quad(sr[k][0], sr[k][1], sr[k + 1][0], sr[k + 1][1]);
        haze.quad(hr[k][0], hr[k][1], hr[k + 1][0], hr[k + 1][1]);
      }
    }
  }
  // a tall end post on the rail line wherever a rail run stops (the drop shortcut's own posts stand at its opening)
  const f = makeFrame(), rs = track.routes || [];
  for (const [sRun, side] of railEnds) {
    const s = ((sRun % L) + L) % L;           // a run that wraps the start line ends past L
    if (rs.some((r) => Math.abs(s - r.openS0) < E.capSkipM || Math.abs(s - r.openS1) < E.capSkipM)) continue;
    track.frameAt(s, f);
    const x = side * (f.w / 2 + 0.04), hw = E.capW / 2;
    const P = (dx, dt, up) => [f.p[0] + f.b[0] * (x + dx) + f.t[0] * dt + f.n[0] * up, f.p[1] + f.b[1] * (x + dx) + f.t[1] * dt + f.n[1] * up,
      f.p[2] + f.b[2] * (x + dx) + f.t[2] * dt + f.n[2] * up];
    const face = (nrm, pts, us) => { const ids = pts.map((p, i) => caps.v(p, nrm, us[i], s)); caps.quad(ids[0], ids[1], ids[2], ids[3]); };
    for (const [ax, sg] of [[f.b, 1], [f.b, -1], [f.t, 1], [f.t, -1]]) {
      const nrm = [ax[0] * sg, ax[1] * sg, ax[2] * sg], onB = ax === f.b;
      const a0 = onB ? [sg * hw, -hw] : [-hw, sg * hw], a1 = onB ? [sg * hw, hw] : [hw, sg * hw];
      face(nrm, [P(a0[0], a0[1], 0), P(a1[0], a1[1], 0), P(a0[0], a0[1], E.capH), P(a1[0], a1[1], E.capH)], [0, 0, 1, 1]);
    }
    face(f.n.slice(), [P(-hw, -hw, E.capH), P(hw, -hw, E.capH), P(-hw, hw, E.capH), P(hw, hw, E.capH)], [1, 1, 1, 1]);
  }
  return { edgeStrip: strip.done(), edgeHaze: haze.done(), railCaps: caps.done() };
}

/* Polish 4 drop shortcuts (render/routes.js draws these). uv.x = a 0..1 coordinate a slow glow wave runs
 * along (outward across the gap, round the ring, forward on the arrows), uv.y = that piece's gain.
 * routeEdge: the kicker lip along the opening (a ramp up to the edge and its face), chevrons pointing off it, a
 * glowing post at each end of the break joined by an arch over the opening (the frame you aim through), and
 * floor chevrons leading to the opening from LEAD_M before it (EDGES lane: "I never made it to the shortcut").
 * routeLand: the soft catch zone, a ring round the landing pad and forward arrows inside it. */
const KICK_W = 1.4, KICK_UP = 0.18, POST_H = 4, POST_W = 0.5, CHEV_STEP_M = 4, CHEV_ARM = 1.5, CHEV_W = 0.45;
const ARCH_RISE = 1.6, ARCH_W = 0.8, ARCH_SEGS = 16, LEAD_M = 130, LEAD_STEP_M = 8, LEAD_ARM = 1.7, LEAD_W = 0.55, LEAD_TURN = 0.4, LEAD_DOUBLE_M = 1.5;
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
  // Wide repeated forward arrows make the lip and the exit legible from the chase view.
  // Reuse the existing warmed route paint, including its Gentle and pause behavior.
  for (const a of track.aerials || []) {
    for (const [m, s0, s1] of [[edge, a.rampS0 + 3, a.gapS0 - 4], [land, a.landingS0 + 4, a.landingS1 - 4]]) {
      for (let s = s0; s <= s1; s += 8) {
        track.frameAt(s, f);
        for (const x of [-f.w * .25, 0, f.w * .25]) for (const side of [-1, 1])
          flat(m, f, [[x, 2], [x, 1.1], [x + side * 2.5, -.8], [x + side * 2.5, -1.7]], [0, 0, .1, .1], 1.25);
      }
    }
    for (const [m, s] of [[edge, a.gapS0 - .8], [land, a.gapS1 + .8]]) {
      track.frameAt(s, f);
      flat(m, f, [[-f.w / 2, -.3], [f.w / 2, -.3], [-f.w / 2, .3], [f.w / 2, .3]], [0, 1, 0, 1], 1.4);
    }
  }
  for (const r of rs) {
    const side = r.side === 'L' ? -1 : 1, s0 = r.openS0, len = r.openS1 - r.openS0;
    // the kicker lip: a ramp rising to the edge and its face dropping away, u runs along it
    const n = Math.max(2, Math.ceil(len / STRIP_STEP_M));
    let prev = null;
    for (let k = 0; k <= n; k++) {
      track.frameAt(s0 + (len * k) / n, f);
      const h = f.w / 2, out = side > 0 ? f.b : neg(f.b), sl = Math.hypot(KICK_W, KICK_UP);
      const rn = [f.n[0] * KICK_W / sl + out[0] * -KICK_UP / sl, f.n[1] * KICK_W / sl + out[1] * -KICK_UP / sl, f.n[2] * KICK_W / sl + out[2] * -KICK_UP / sl];
      const row = [edge.v(off(f.p, f.b, side * (h - KICK_W), f.n, DECAL_M), rn, k / n, 0.75), edge.v(off(f.p, f.b, side * h, f.n, KICK_UP), rn, k / n, 1.1),
        edge.v(off(f.p, f.b, side * h, f.n, KICK_UP), out, k / n, 1), edge.v(off(f.p, f.b, side * h, f.n, -THICK_M), out, k / n, 0.6)];
      if (prev) { edge.quad(prev[0], prev[1], row[0], row[1]); edge.quad(prev[2], prev[3], row[2], row[3]); }
      prev = row;
    }
    // floor chevrons leading to the opening: from LEAD_M out, drifting toward the open side, nosed at the gap
    for (let d = LEAD_M; d >= LEAD_STEP_M * 1.5; d -= LEAD_STEP_M) {
      track.frameAt(s0 - d, f);
      const k = 1 - d / LEAD_M, x = side * (0.5 + k * (f.w / 2 - 3.2)), turn = side * LEAD_TURN * (0.4 + 0.6 * k);
      const dl = Math.hypot(turn, 1), dir = [turn / dl, 1 / dl], perp = [dir[1], -dir[0]], gain = 0.9 + 0.4 * k;
      for (const back of [0, LEAD_DOUBLE_M]) {           // a double chevron: reads as "this way" at speed
        const tx = x - dir[0] * back, ty = -dir[1] * back;
        for (const sg of [-1, 1]) {
          const tip = [tx, ty], tb = [tx - dir[0] * LEAD_W, ty - dir[1] * LEAD_W];
          const arm = [tx - dir[0] * LEAD_ARM + perp[0] * sg * LEAD_ARM, ty - dir[1] * LEAD_ARM + perp[1] * sg * LEAD_ARM];
          const ab = [arm[0] - dir[0] * LEAD_W, arm[1] - dir[1] * LEAD_W];
          flat(edge, f, [tip, tb, arm, ab], [k, k, k - 0.04, k - 0.04], gain);
        }
      }
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
    // the arch: a glowing band from post top to post top over the opening, facing the road
    let arc = null;
    for (let k = 0; k <= ARCH_SEGS; k++) {
      const u = k / ARCH_SEGS, y = POST_H + ARCH_RISE * Math.sin(Math.PI * u);
      track.frameAt(s0 + len * u, f);
      const inN = side > 0 ? neg(f.b) : f.b.slice();
      const row = [edge.v(off(f.p, f.b, side * f.w / 2, f.n, y - ARCH_W / 2), inN, u, 1.2), edge.v(off(f.p, f.b, side * f.w / 2, f.n, y + ARCH_W / 2), inN, u, 1.2)];
      if (arc) edge.quad(arc[0], arc[1], row[0], row[1]);
      arc = row;
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
    // Large repeated forward chevrons extend out of the ring, so the exit reads from above and on landing.
    for (let s = Math.max(r.land.fromS + 5, r.pad.s - 8); s <= Math.min(r.land.toS - 5, r.pad.s + 56); s += 12) {
      track.frameAt(s, f);
      const x = Math.max(-f.w / 2 + 2.8, Math.min(f.w / 2 - 2.8, cx));
      const u = (s - r.land.fromS) / (r.land.toS - r.land.fromS);
      for (const side of [-1, 1]) {
        const pts = [[x, 3], [x, 2.2], [x + side * 2.5, 0], [x + side * 2.5, -0.8]];
        flat(land, f, pts, [u, u, u - 0.05, u - 0.05], 1.3, DECAL_M * 2);
      }
      flat(land, f, [[x - 0.45, -2.6], [x + 0.45, -2.6], [x - 0.45, 2.2], [x + 0.45, 2.2]], [u, u, u, u], 1.3, DECAL_M * 2);
    }
  }
  return { routeEdge: edge.done(), routeLand: land.done() };
}
