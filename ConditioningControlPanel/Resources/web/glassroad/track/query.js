/* track/query.js - the ground probe. No three.js.
 *
 * query(px, py, pz, hintSeg = -1, out?) -> { seg, segT, s, lateral, height, normal, onRoad, edgeDist,
 *   surface, flags, railed, flow, p }
 * Water wave 1: flow = [along, lat] m/s, an array the result owns; [0, 0] off a stream node, otherwise the
 * node's flow lerped by segT toward the next node's (zero when that one is not a stream), see flowAt.
 * Nearest centreline point by a coarse pass over the arc-length sample polyline (bounding spheres prune
 * whole segments), then Newton on the exact curve until (P - c) . t = 0. With hintSeg >= 0 only the
 * hint +-WINDOW segments are searched; a result further than FAR_M from the curve searches them all.
 * height and lateral are measured in the rolled frame; p is the foot of the normal on the road plane
 * (lateral not clamped, so p runs past the edge when the kart does).
 *
 * Polish 6 (stacked courses): query.below(px, py, pz, out?) -> the same result for the nearest road UNDER P,
 * or null: a segment whose plan footprint (samples +- half width) holds P, whose surface faces up
 * (normal y > BELOW_UP) and lies below P (height >= -BELOW_PASS_M, so a kart a tick past it still counts),
 * on the road (no gap, inside the edges). The highest such road wins (the smallest height). A falling kart
 * lands on it (sim/kart.js); nothing beneath keeps the edge drop and rescue.
 */
import { makeFrame, frameAtSegU } from './frames.js';
import { holeAt, holeDistance, fieldAt, stoneIn, stoneDistance } from './courts.js';
import { canalNormal } from './canal.js';

const WINDOW = 4, FAR_M = 25, BELOW_UP = 0.5, BELOW_PASS_M = 1.5, BELOW_END_M = 0.5;

/* Stones wave: inside a field (track/courts.js) a result over a stone is on road as usual and carries `stone` (the
 * stone row); over the void onRoad is false, `hole` is the field's one shared void object ({ id, field, lip: true })
 * and holeDist is minus the distance to the nearest stone edge. Outside every field `stone` is null. */
export function makeQuery() {
  return { seg: 0, segT: 0, s: 0, lateral: 0, height: 0, normal: [0, 1, 0], onRoad: false, edgeDist: 0, hole: null, holeDist: Infinity, stone: null, surface: 'glass', flags: null, railed: false, flow: [0, 0], p: [0, 0, 0] };
}

/**
 * The water flow at (seg, segT) written into out ([along, lat] m/s): [0, 0] unless nodes[seg] is a stream with a
 * flow, else that flow lerped by segT toward the next node's flow (a non-stream neighbour counts as [0, 0], so a
 * stream eases out over its last segment). Shared by the query and the road mesh.
 */
export function flowAt(nodes, seg, segT, out = [0, 0]) {
  const a = nodes[seg], af = a && a.surface === 'stream' ? a.flow : null;
  if (!af) { out[0] = 0; out[1] = 0; return out; }
  const b = nodes[(seg + 1) % nodes.length], bf = b && b.surface === 'stream' && b.flow ? b.flow : null;
  const t = segT < 0 ? 0 : segT > 1 ? 1 : segT;
  out[0] = af[0] + ((bf ? bf[0] : 0) - af[0]) * t;
  out[1] = af[1] + ((bf ? bf[1] : 0) - af[1]) * t;
  return out;
}

export function createQuery(sp, refN, rollAt, widthAt, nodes, courts = [], fields = [], profile = null, aerials = []) {
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
  // plan boxes per segment for below(): x0, z0, x1, z1 over the samples, grown by the widest half width, and the lowest y
  const box = new Float64Array(N * 4), minY = new Float64Array(N);
  for (let i = 0; i < N; i++) {
    const hw = Math.max(widthAt(i, 0), widthAt(i, 0.5), widthAt(i, 1)) / 2 + 1;
    let x0 = Infinity, z0 = Infinity, x1 = -Infinity, z1 = -Infinity, y0 = Infinity;
    for (let k = 0; k <= SUB; k++) {
      const j = (i * SUB + k) * 3;
      x0 = Math.min(x0, S[j]); x1 = Math.max(x1, S[j]); z0 = Math.min(z0, S[j + 2]); z1 = Math.max(z1, S[j + 2]); y0 = Math.min(y0, S[j + 1]);
    }
    box[i * 4] = x0 - hw; box[i * 4 + 1] = z0 - hw; box[i * 4 + 2] = x1 + hw; box[i * 4 + 3] = z1 + hw; minY[i] = y0 - hw;
  }
  const fr = makeFrame(), c = [0, 0, 0], d1 = [0, 0, 0], d2 = [0, 0, 0];
  // polish 6: the running best lives in a typed scratch (best[0] d2, best[1] seg, best[2] u); doubles kept in the
  // closure context boxed a fresh heap number on every store (the top garbage source of a race)
  const best = new Float64Array(3);
  const curved = {};

  const scanSeg = (i, px, py, pz) => {
    const dc = Math.hypot(px - centre[i * 3], py - centre[i * 3 + 1], pz - centre[i * 3 + 2]) - radius[i];
    if (dc > 0 && dc * dc >= best[0]) return;
    for (let k = 0; k < SUB; k++) {
      const j = (i * SUB + k) * 3;
      const ax = S[j], ay = S[j + 1], az = S[j + 2];
      const ex = S[j + 3] - ax, ey = S[j + 4] - ay, ez = S[j + 5] - az;
      const wx = px - ax, wy = py - ay, wz = pz - az;
      const ee = ex * ex + ey * ey + ez * ez;
      let f = ee > 0 ? (wx * ex + wy * ey + wz * ez) / ee : 0;
      f = f < 0 ? 0 : f > 1 ? 1 : f;
      const dx = wx - ex * f, dy = wy - ey * f, dz = wz - ez * f, dd = dx * dx + dy * dy + dz * dz;
      if (dd < best[0]) { best[0] = dd; best[1] = i; best[2] = (k + f) / SUB; }
    }
  };
  const scan = (px, py, pz, hint) => {
    best[0] = Infinity;
    if (hint >= 0) for (let o = -WINDOW; o <= WINDOW; o++) scanSeg(((hint + o) % N + N) % N, px, py, pz);
    else for (let i = 0; i < N; i++) scanSeg(i, px, py, pz);
  };

  /** Newton on f(u) = (P - c(u)) . c'(u) from (seg, u), stepping into a neighbour segment once if u leaves [0, 1]; fills out. */
  function solve(seg, u, px, py, pz, out) {
    let hops = 0;
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
    const sa = sp.sFromU(seg, u);
    const at = sa >= sp.lengthM ? sa - sp.lengthM : sa;
    const gap = sp.gap[seg] || aerials.some(a => at >= a.gapS0 && at < a.gapS1), half = fr.w / 2, fl = fr.flags;
    out.seg = seg;
    if (out.forkId !== undefined) { out.forkId = null; out.forkS = 0; out.pathScale = 1; out.beyond = false; }
    out.segT = fr.segT;
    out.s = at;
    out.lateral = lat;
    out.height = h;
    out.normal[0] = fr.n[0]; out.normal[1] = fr.n[1]; out.normal[2] = fr.n[2];
    out.edgeDist = half - Math.abs(lat);
    out.hole = holeAt(courts, out.s, lat);
    out.holeDist = out.hole ? holeDistance(out.hole, out.s, lat) : Infinity;
    out.stone = null;
    if (fields.length && !out.hole) {
      const fd = fieldAt(fields, out.s);
      if (fd) {
        out.stone = stoneIn(fd, out.s, lat);
        if (!out.stone) { out.hole = fd.void; out.holeDist = -stoneDistance(fd, out.s, lat); }
      }
    }
    out.onRoad = !gap && !out.hole && out.edgeDist >= 0;
    out.surface = fr.surface;
    out.flags = fl;
    if (!out.flow) out.flow = [0, 0];
    flowAt(nodes, seg, fr.segT, out.flow);
    out.railed = !gap && (fl.includes('rail') || fl.includes(lat >= 0 ? 'railR' : 'railL'));
    out.p[0] = fr.p[0] + fr.b[0] * lat; out.p[1] = fr.p[1] + fr.b[1] * lat; out.p[2] = fr.p[2] + fr.b[2] * lat;
    if (profile && profile(out.s, lat, curved).blend) {
      canalNormal(fr, curved, out.normal);
      for (let k = 0; k < 3; k++) out.p[k] += fr.n[k] * curved.height;
      out.height = (px - out.p[0]) * out.normal[0] + (py - out.p[1]) * out.normal[1] + (pz - out.p[2]) * out.normal[2];
    }
    return out;
  }

  // A shoulder wider than the centreline search radius needs a surface-distance check. Keep this
  // bounded to broad decks near the kart's feet; airborne routes still use the usual centreline search.
  const broad = [];
  for (let i = 0; i < N; i++) if (Math.max(widthAt(i, 0), widthAt(i, 1)) > FAR_M * 2) broad.push(i);
  const segments = Array.from({ length: N }, (_, i) => i);
  const courtProbe = makeQuery();
  function query(px, py, pz, hintSeg = -1, out = makeQuery()) {
    const hint = hintSeg >= 0 && hintSeg < N ? hintSeg | 0 : -1;
    scan(px, py, pz, hint);
    // A wide platform gallery owns its nearby flight even when another deck has a nearer
    // centreline. Keep the exact void result: this selects coordinates, never extra support.
    if (hint >= 0 && fields.some(f => f.platforming && hint >= f.from && hint < f.to)) {
      solve(best[1] | 0, best[2], px, py, pz, out);
      const field = fieldAt(fields, out.s);
      const along = (px - fr.p[0]) * fr.t[0] + (py - fr.p[1]) * fr.t[1] + (pz - fr.p[2]) * fr.t[2];
      if (field?.platforming && out.edgeDist >= 0 && out.height >= -.8 && out.height <= 8 && Math.abs(along) <= 1e-4) return out;
    }
    if (hint >= 0 && best[0] > FAR_M * FAR_M) scan(px, py, pz, -1);
    solve(best[1] | 0, best[2], px, py, pz, out);
    // A broad court can be nearer in surface distance while another deck's centreline is nearer.
    // Only probe its bounded footprint near the floor; preserve the ordinary ribbon and flight query.
    for (const court of courts) for (let i = court.from; i < court.to; i++) {
      const o = i * 4;
      if (px < box[o] || px > box[o + 2] || pz < box[o + 1] || pz > box[o + 3]) continue;
      solve(i, 0.5, px, py, pz, courtProbe);
      const h = Math.abs(courtProbe.height), along = (px - fr.p[0]) * fr.t[0] + (py - fr.p[1]) * fr.t[1] + (pz - fr.p[2]) * fr.t[2];
      if (courtProbe.s < court.fromS || courtProbe.s >= court.toS || courtProbe.edgeDist < -1e-6 || h > 4 || Math.abs(along) > 1e-4) continue;
      if (h < Math.abs(out.height) - 1e-7 || out.edgeDist < 0) copyQuery(out, courtProbe);
    }
    // A widened shoulder can be closer to another deck's centreline than its own centreline.
    // Without a hint, check nearby support above or below that wrong deck. Preserve a hinted
    // road's off-edge departure and positive-height flights: query.below owns those landings.
    const wrongDeck = Math.abs(out.height) > 4 && (hint < 0 || (out.height < -4 && out.onRoad));
    for (const i of wrongDeck ? segments : broad) {
      const o = i * 4;
      if (px < box[o] || px > box[o + 2] || pz < box[o + 1] || pz > box[o + 3]) continue;
      solve(i, 0.5, px, py, pz, courtProbe);
      const h = Math.abs(courtProbe.height), along = (px - fr.p[0]) * fr.t[0] + (py - fr.p[1]) * fr.t[1] + (pz - fr.p[2]) * fr.t[2];
      if (!courtProbe.onRoad || courtProbe.height < -BELOW_PASS_M || h > 4 || Math.abs(along) > 1e-4) continue;
      if (h < Math.abs(out.height) - 1e-7 || out.edgeDist < 0) copyQuery(out, courtProbe);
    }
    return out;
  }

  const tmp = makeQuery();
  /** The nearest road under P (see the header), written into out; null when there is none. Allocates nothing. */
  query.below = function below(px, py, pz, out = makeQuery()) {
    let found = false, bestH = Infinity;
    for (let i = 0; i < N; i++) {
      const o = i * 4;
      if (px < box[o] || px > box[o + 2] || pz < box[o + 1] || pz > box[o + 3] || minY[i] > py + BELOW_PASS_M) continue;
      // the nearest sample point in plan on this segment
      let bd = Infinity, bu = 0;
      for (let k = 0; k < SUB; k++) {
        const j = (i * SUB + k) * 3;
        const ax = S[j], az = S[j + 2], ex = S[j + 3] - ax, ez = S[j + 5] - az, wx = px - ax, wz = pz - az, ee = ex * ex + ez * ez;
        let f = ee > 0 ? (wx * ex + wz * ez) / ee : 0;
        f = f < 0 ? 0 : f > 1 ? 1 : f;
        const dx = wx - ex * f, dz = wz - ez * f, dd = dx * dx + dz * dz;
        if (dd < bd) { bd = dd; bu = (k + f) / SUB; }
      }
      // the road surface there must lie below P, not over it
      sp.pos(i, bu, c);
      if (c[1] > py + BELOW_PASS_M + 1) continue;
      solve(i, bu, px, py, pz, tmp);
      // P must lie over this stretch, not past its end (a ramp's lip, a road that stops): fr is solve's frame
      const along = (px - fr.p[0]) * fr.t[0] + (py - fr.p[1]) * fr.t[1] + (pz - fr.p[2]) * fr.t[2];
      if (along > BELOW_END_M || along < -BELOW_END_M) continue;
      if (!tmp.onRoad || tmp.normal[1] < BELOW_UP || tmp.height < -BELOW_PASS_M || tmp.height >= bestH) continue;
      bestH = tmp.height; found = true;
      copyQuery(out, tmp);
    }
    return found ? out : null;
  };
  return query;
}

/** Copy query result a into o (arrays copied in place). */
export function copyQuery(o, a) {
  o.seg = a.seg; o.segT = a.segT; o.s = a.s; o.lateral = a.lateral; o.height = a.height;
  if (a.forkId !== undefined || o.forkId !== undefined) { o.forkId = a.forkId || null; o.forkS = a.forkS || 0; o.pathScale = a.pathScale || 1; o.beyond = !!a.beyond; }
  o.normal[0] = a.normal[0]; o.normal[1] = a.normal[1]; o.normal[2] = a.normal[2];
  o.onRoad = a.onRoad; o.edgeDist = a.edgeDist; o.surface = a.surface; o.flags = a.flags; o.railed = a.railed;
  o.hole = a.hole; o.holeDist = a.holeDist; o.stone = a.stone || null;
  if (!o.flow) o.flow = [0, 0];
  if (a.flow) { o.flow[0] = a.flow[0]; o.flow[1] = a.flow[1]; } else { o.flow[0] = 0; o.flow[1] = 0; }
  o.p[0] = a.p[0]; o.p[1] = a.p[1]; o.p[2] = a.p[2];
  return o;
}
