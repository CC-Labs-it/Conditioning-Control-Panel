/* track/load.js - Track JSON v1 -> Track (CONTRACT.md section 5). No three.js.
 * Degrees in the JSON (node roll) become radians here; everything derived (spline, frames, lengthM,
 * driveline, checklines, meshes) is built at load, the meshes and driveline on first access.
 */
import { validate as validateJson } from './schema.js';
import { DEG, qfromBasis, wrap } from '../core/math.js';
import { createSpline, createScalar } from './spline.js';
import { buildRefNormals, frameAtSegU, makeFrame } from './frames.js';
import { createQuery, flowAt } from './query.js';
import { buildDriveline, buildChecklines } from './driveline.js';
import { buildMeshes } from './mesh.js';
import { buildCourts, buildFields } from './courts.js';
import { canalProfile, canalNormal } from './canal.js';
import { installForks } from './forks.js';

/** @returns {object} Track; throws an Error listing the schema errors */
export function loadTrack(json, { validate = true } = {}) {
  if (validate) {
    const r = validateJson(json);
    if (!r.ok) throw new Error('track invalid: ' + r.errors.join('; '));
  }
  if (json.loop !== true) throw new Error('track invalid: only closed loops are supported');
  // water wave 1: a stream node's flow ([along, lat] m/s) rides along for the query and the road mesh
  const nodes = json.nodes.map((n) => ({ p: n.p.slice(), roll: n.roll * DEG, w: n.w, flags: n.flags.slice(), speed: n.speed, surface: n.surface, flow: n.flow ? n.flow.slice() : null }));
  const N = nodes.length;
  const sp = createSpline(nodes.map((n) => n.p), nodes.map((n) => n.flags.includes('jump')));
  const refN = buildRefNormals(sp, nodes.map((n) => n.flags.includes('magnet')));
  const rollAt = createScalar(nodes.map((n) => n.roll));
  const widthAt = createScalar(nodes.map((n) => n.w));
  const { nodeS, lengthM } = sp;
  const isGap = (i) => sp.gap[((i % N) + N) % N];

  const frameAt = (s, out = makeFrame()) => {
    const sw = wrap(s, lengthM), seg = sp.segAt(sw);
    return frameAtSegU(sp, refN, rollAt, widthAt, nodes, seg, sp.uFromS(seg, sw), out);
  };

  const sections = json.sections.map((s) => ({ id: s.id, act: s.act, palette: s.palette, sky: s.sky, bandSpeed: s.bandSpeed, from: s.from, to: s.to, fromS: nodeS[s.from], toS: nodeS[s.to] }));
  const sectionAt = (s) => {
    const sw = wrap(s, lengthM);
    for (let i = sections.length - 1; i > 0; i--) if (sw >= sections[i].fromS) return i;
    return 0;
  };
  const hasCanal = sections.some(s => s.id === 'coolant-canal');
  const profile = hasCanal ? (s, x, out) => canalProfile(sections, wrap(s, lengthM), x, out) : null;
  const surfaceProfile = {}, surfaceN = [0, 0, 0];
  const surfaceAt = (s, x = 0, out = makeFrame()) => {
    frameAt(s, out);
    const h = profile ? profile(s, x, surfaceProfile).height : 0;
    for (let k = 0; k < 3; k++) out.p[k] += out.b[k] * x + out.n[k] * h;
    if (profile && surfaceProfile.blend) {
      canalNormal(out, surfaceProfile, surfaceN);
      for (let k = 0; k < 3; k++) out.t[k] += out.n[k] * surfaceProfile.alongSlope;
      const tLength = Math.hypot(...out.t);
      for (let k = 0; k < 3; k++) { out.t[k] /= tLength; out.n[k] = surfaceN[k]; }
      out.b[0] = out.t[1] * out.n[2] - out.t[2] * out.n[1];
      out.b[1] = out.t[2] * out.n[0] - out.t[0] * out.n[2];
      out.b[2] = out.t[0] * out.n[1] - out.t[1] * out.n[0];
    }
    return out;
  };

  const boosts = json.boosts.map((b) => ({ node: b.node, x: b.x, w: b.w, len: b.len, s: nodeS[b.node] }));
  const jumps = json.jumps.map((j) => {
    let land = j.node + 1;
    while (isGap(land) && land < j.node + N) land++;
    return { node: j.node, airLen: j.airLen, height: j.height, s: nodeS[j.node], landS: nodeS[land % N] };
  });
  // polish 4 drop shortcuts: the open edge (fromS..toS on `side`), the landing stretch and its pad
  const routes = (json.routes || []).map((r, idx) => {
    const padS = wrap(nodeS[r.pad.node] + (r.pad.ds || 0), lengthM), pf = frameAt(padS);
    const openS0 = nodeS[r.from] + (r.open ? r.open.ds : 0), openS1 = r.open ? openS0 + r.open.len : nodeS[r.to];
    return { idx, id: r.id, kind: r.kind, side: r.side, from: r.from, to: r.to, fromS: nodeS[r.from], toS: nodeS[r.to], openS0, openS1,
      land: { from: r.land.from, to: r.land.to, fromS: nodeS[r.land.from], toS: nodeS[r.land.to + 1] },
      pad: { node: r.pad.node, x: r.pad.x, s: padS,
        p: [pf.p[0] + pf.b[0] * r.pad.x, pf.p[1] + pf.b[1] * r.pad.x, pf.p[2] + pf.b[2] * r.pad.x], n: pf.n.slice(), t: pf.t.slice() } };
  });
  const lm = json.limits;
  const courts = buildCourts(json.courts, nodeS, frameAt);
  // stones wave: fields of stepping stones ([] when the track has none), each stone with its s like a hole
  const fields = buildFields(json.fields, nodeS, frameAt);
  // B1 (CONTRACT-B1.md section 9): item boxes and pickup bubbles, s = node s + ds, wrapped
  const items = (json.items || []).map((e) => ({ kind: e.kind, node: e.node, x: e.x, ds: e.ds || 0, s: wrap(nodeS[e.node] + (e.ds || 0), lengthM) }));

  const grid = json.spawn.grid, spawnNode = json.spawn.node;
  const spawnPose = (slot = 0) => {
    const g = grid[((slot % grid.length) + grid.length) % grid.length];
    const f = frameAt(nodeS[spawnNode] + g[1]);
    const p = [f.p[0] + f.b[0] * g[0], f.p[1] + f.b[1] * g[0], f.p[2] + f.b[2] * g[0]];
    const q = qfromBasis([0, 0, 0, 1], f.b, f.n, [-f.t[0], -f.t[1], -f.t[2]]);
    return { p, q, seg: f.seg, s: wrap(nodeS[spawnNode] + g[1], lengthM), lateral: g[0] };
  };

  const track = {
    id: json.id, name: json.name, laps: json.laps, loop: true, lengthM, nodeCount: N, nodeS,
    sections, sectionAt, boosts, jumps, items, routes, courts, fields, reservoir: json.reservoir, waterSpills: json.waterSpills,
    limits: { fallY: lm.fallY, edgeGraceM: lm.edgeGraceM, edgeGraceS: lm.edgeGraceS },
    spawnPose,
    nodeFlags: (i) => nodes[((i % N) + N) % N].flags,
    nodeSurface: (i) => nodes[((i % N) + N) % N].surface,
    /** Water flow at (seg, segT) into out, see track/query.js flowAt. */
    flowAt: (seg, segT, out) => flowAt(nodes, ((seg % N) + N) % N, segT, out),
    frameAt, surfaceAt, canalProfile: profile,
    query: createQuery(sp, refN, rollAt, widthAt, nodes, courts, fields, profile),
    checklines: null,
  };
  track.checklines = buildChecklines(track, json.checkpoints);
  installForks(track, json.forks);
  let dl = null, meshes = null;
  Object.defineProperty(track, 'driveline', { enumerable: true, get: () => (dl ||= buildDriveline(track, sp)) });
  Object.defineProperty(track, 'meshes', { enumerable: true, get: () => (meshes ||= buildMeshes(track, track.driveline)) });
  return track;
}
