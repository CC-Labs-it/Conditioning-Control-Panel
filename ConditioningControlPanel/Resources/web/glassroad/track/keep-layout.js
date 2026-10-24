/* The Keep's shared course stations. Rendering and gates use the loaded spline's distances. */
const RAD = Math.PI / 180;
const straight = (id, len, y, w = 24) => ({ id, len, y, w });
const corner = (id, turn, r, y) => ({ id, turn, r, y, w: 24 });
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
    straight('forecourt', 90, 0, 26), straight('outer-gate', 100, 0, 26), straight('outer-exit', 70, 0, 26),
    corner('east-ascent', 90, 65, 24), straight('high-gate', 160, 24, 26),
    corner('north-tower', 90, 65, 30), straight('crown-walk', 80, 30),
    corner('dogleg-in', 45, 60, 25), straight('west-descent', 80, 12),
    corner('dogleg-out', -45, 60, 8), straight('west-approach', 0, 6),
    corner('west-tower', 90, 65, 2), straight('moat-return', 0, 0),
    corner('south-tower', 90, 65, 0), straight('home', 60, 0, 26),
  ];
  // Only these perpendicular straights solve closure; all corners and gate halls stay authored.
  const end = walkKeep(out);
  out.find((p) => p.id === 'west-approach').len = end.x;
  out.find((p) => p.id === 'moat-return').len = end.z;
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
  const outer = find('outer-gate'), high = find('high-gate');
  const halls = [
    { id: 'outer', s0: outer.s0, s1: outer.s1, gates: [20, 48, 76].map((d) => outer.s0 + d) },
    { id: 'inner', s0: high.s0 + 40, s1: high.s0 + 140, gates: [65, 93, 121].map((d) => high.s0 + d) },
  ];
  const gates = halls.flatMap((hall, h) => hall.gates.map((s, i) => ({ id: hall.id + '-' + (i + 1), s, x: 0, w: 14, offsetS: i + h * .4 })));
  return { landmarks, halls, gates, widthM: 26, gateWidthM: 14, bypassWidthM: 6 };
}



