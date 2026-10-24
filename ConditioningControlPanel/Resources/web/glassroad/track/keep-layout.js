/* Shared authored Keep stations. Distances always come from the loaded spline. */
const RAD = Math.PI / 180;
const straight = (id, len, y, w = 28, extra = {}) => ({ id, len, y, w, ...extra });
const corner = (id, turn, r, y, extra = {}) => ({ id, turn, r, y, w: 28, ...extra });
export const keepPieceLength = (p) => p.turn ? Math.abs(p.turn) * RAD * p.r : p.len;
export const keepSegmentCount = (p) => Math.max(1, Math.round(keepPieceLength(p) / 10));
export function walkKeep(pieces) {
  let x = 0, z = 0, heading = 0;
  const starts = [];
  for (const p of pieces) {
    starts.push({ x, z, heading });
    if (p.turn) {
      const sign = Math.sign(p.turn), next = heading + p.turn * RAD;
      x += sign * p.r * (Math.sin(next) - Math.sin(heading));
      z += sign * p.r * (Math.cos(heading) - Math.cos(next));
      heading = next;
    } else { x += Math.cos(heading) * p.len; z += Math.sin(heading) * p.len; }
  }
  return { x, z, heading, starts };
}
function pieces() {
  const out = [
    straight('forecourt', 90, 0, 30), straight('outer-gate', 120, 0, 30), straight('outer-exit', 90, 0, 30),
    corner('east-ascent', 90, 80, 4), corner('tower-spiral', -720, 70, 68, { w: 26, linear: true, spiral: true }),
    straight('tower-bridge', 60, 68, 26, { widenTo: 34 }), straight('chain-hall', 180, 68, 34), straight('high-gate', 160, 68, 30),
    corner('north-tower', 90, 90, 74), straight('crown-walk', 80, 74),
    corner('dogleg-in', 45, 75, 62), straight('west-descent', 120, 28),
    corner('dogleg-out', -45, 75, 18), straight('west-approach', 0, 14),
    corner('west-tower', 90, 90, 12), straight('moat-bridge', 110, 12, 30),
    straight('sluice', 40, 2, 30, { surface: 'stream', flow: [3, 0] }),
    straight('moat-pool', 55, 2, 30, { surface: 'pool' }), straight('moat-return', 0, 0),
    corner('south-tower', 90, 90, 0), straight('home', 80, 0, 30),
  ];
  // These perpendicular straights alone solve closure; the spiral and challenge halls never move.
  const end = walkKeep(out);
  out.find((p) => p.id === 'west-approach').len = end.x;
  out.find((p) => p.id === 'moat-return').len = end.z;
  if (out.some((p) => !p.turn && p.len < 20)) throw Error('Keep closure straight too short');
  return out;
}
export const KEEP_PIECES = Object.freeze(pieces().map(Object.freeze));
export function getKeepLayout(track) {
  if (track.id !== 'keep') return null;
  let node = 0;
  const landmarks = KEEP_PIECES.map((p) => {
    const from = node; node += keepSegmentCount(p);
    return { id: p.id, node: from, to: node, s0: track.nodeS[from],
      s1: node < track.nodeCount ? track.nodeS[node] : track.lengthM, widthM: p.w };
  });
  const find = (id) => landmarks.find((p) => p.id === id);
  const outer = find('outer-gate'), high = find('high-gate'), chain = find('chain-hall');
  const halls = [
    { id: 'outer', s0: outer.s0, s1: outer.s1, gates: [20, 56, 92].map((d) => outer.s0 + d) },
    { id: 'inner', s0: high.s0 + 40, s1: high.s0 + 140, gates: [65, 93, 121].map((d) => high.s0 + d) },
  ];
  const gates = halls.flatMap((hall, h) => hall.gates.map((s, i) => ({ id: hall.id + '-' + (i + 1), s, x: 0, w: 14, offsetS: i + h * .4 })));
  const chains = [40, 76, 112, 148].map((d, i) => ({ id: 'chain-' + (i + 1), s: chain.s0 + d, x: 0, w: 10, offsetS: i * 1.1 }));
  const towers = [{ x: 450, z: 80, radiusM: 55, baseY: -20, topY: 84 }];
  const bounds = { minX: Infinity, maxX: -Infinity, minZ: Infinity, maxZ: -Infinity, minY: 0, maxY: 0 };
  for (let s = 0; s < track.lengthM; s += 10) {
    const f = track.frameAt(s), margin = f.w / 2;
    bounds.minX = Math.min(bounds.minX, f.p[0] - margin); bounds.maxX = Math.max(bounds.maxX, f.p[0] + margin);
    bounds.minZ = Math.min(bounds.minZ, f.p[2] - margin); bounds.maxZ = Math.max(bounds.maxZ, f.p[2] + margin);
    bounds.minY = Math.min(bounds.minY, f.p[1]); bounds.maxY = Math.max(bounds.maxY, f.p[1]);
  }
  return { landmarks, halls, gates, chains, towers, bounds, widthM: 30, gateWidthM: 14, bypassWidthM: 8 };
}


