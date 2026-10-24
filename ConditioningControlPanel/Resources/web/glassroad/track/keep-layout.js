/* Shared authored Keep stations. Distances always come from the loaded spline. */
const RAD = Math.PI / 180;
const straight = (id, len, y, w = 30, extra = {}) => ({ id, len, y, w, ...extra });
const corner = (id, turn, r, y, extra = {}) => ({ id, turn, r, y, w: 30, ...extra });
export const keepPieceLength = (p) => p.turn ? Math.abs(p.turn) * RAD * p.r : p.len;
export const keepSegmentCount = (p) => Math.max(1, Math.round(keepPieceLength(p) / 10));
export function walkKeep(pieces) {
  let x = 0, z = 0, heading = 0; const starts = [];
  for (const p of pieces) {
    starts.push({ x, z, heading });
    if (p.turn) {
      const sign = Math.sign(p.turn), next = heading + p.turn * RAD;
      x += sign * p.r * (Math.sin(next) - Math.sin(heading));
      z += sign * p.r * (Math.cos(heading) - Math.cos(next)); heading = next;
    } else { x += Math.cos(heading) * p.len; z += Math.sin(heading) * p.len; }
  }
  return { x, z, heading, starts };
}
function pieces() {
  const out = [
    straight('forecourt', 90, 0), straight('outer-gate', 120, 0), straight('outer-exit', 60, 0),
    corner('cellblock-in', 45, 65, 3), straight('cellblock-approach', 90, 3), corner('cellblock-out', -45, 65, 3),
    straight('cell-gallery', 100, 3), corner('east-ascent', 90, 90, 4),
    corner('tower-spiral', -1260, 75, 116, { w: 26, linear: true, spiral: true }),
    straight('tower-bridge', 70, 116, 26, { widenTo: 34 }), corner('tower-exit', 90, 75, 116),
    straight('east-battlement', 90, 116, 34, { edge: 'L' }), corner('east-corner', 90, 75, 116),
    straight('chain-hall', 180, 116, 34), straight('high-gate', 160, 116), straight('north-approach', 180, 116),
    corner('north-tower', 810, 90, 16, { linear: true }), straight('crown-walk', 120, 16, 34, { edge: 'L' }),
    corner('dogleg-in', 45, 75, 16), straight('west-descent', 120, 20), corner('dogleg-out', -45, 75, 14),
    straight('gallows-courtyard', 160, 14, 34), corner('courtyard-exit', -45, 75, 16),
    straight('outer-rampart', 140, 16, 34, { edge: 'L' }), corner('rampart-turn', 45, 75, 16),
    corner('north-rampart-in', -45, 75, 16), corner('serpentine-in', 45, 80, 22), straight('north-rampart-traverse', 30, 22, 32), corner('serpentine-cross', -90, 80, 22), straight('serpentine-return', 30, 22, 32), corner('serpentine-out', 45, 80, 16), corner('north-rampart-out', 45, 75, 16),
    straight('west-approach', 320, 16, 32), corner('west-tower', 90, 90, 14),
    straight('bridge-approach', 80, 14, 34), straight('drawbridge-ramp', 40, 24, 34, { kick: true }),
    straight('drawbridge-gap', 20, 8, 34, { jump: true, linear: true }), straight('drawbridge-landing', 60, 8, 34),
    straight('stones-approach', 70, 2, 32), straight('stones', 270, 2, 32, { field: true }), straight('stones-exit', 20, 2, 32),
    straight('sluice', 40, 2, 30, { surface: 'stream', flow: [3, 0] }),
    straight('moat-pool', 55, 2, 30, { surface: 'pool' }), straight('moat-return', 0, 0),
    corner('south-tower', 90, 90, 0), straight('home', 80, 0),
  ];
  const end = walkKeep(out);
  out.find((p) => p.id === 'west-approach').len += end.x;
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
    return { id: p.id, node: from, to: node, s0: track.nodeS[from], s1: node < track.nodeCount ? track.nodeS[node] : track.lengthM, widthM: p.w };
  });
  const find = (id) => landmarks.find((p) => p.id === id), outer = find('outer-gate'), high = find('high-gate'), chain = find('chain-hall');
  const halls = [
    { id: 'outer', s0: outer.s0, s1: outer.s1, gates: [20, 56, 92].map((d) => outer.s0 + d) },
    { id: 'inner', s0: high.s0 + 40, s1: high.s0 + 140, gates: [65, 93, 121].map((d) => high.s0 + d) },
  ];
  const gates = halls.flatMap((hall, h) => hall.gates.map((s, i) => ({ id: hall.id + '-' + (i + 1), s, x: 0, w: 14, offsetS: i + h * .4 })));
  const chains = [40, 76, 112, 148].map((d, i) => ({ id: 'chain-' + (i + 1), s: chain.s0 + d, x: 0, w: 10, offsetS: i * 1.1 }));
  const start = walkKeep(KEEP_PIECES).starts[KEEP_PIECES.findIndex((p) => p.id === 'tower-spiral')];
  const towers = [{ x: start.x + 75, z: start.z, radiusM: 59, baseY: -25, topY: 142 }];
  const castleBlocks = [
    { id: 'main-prison', x: 710, z: 397, w: 300, d: 140, baseY: -16, topY: 78 },
    { id: 'west-cellblock', x: 175, z: 340, w: 400, d: 220, baseY: -16, topY: 62 },
    { id: 'prison-spine', x: 475, z: 310, w: 120, d: 300, baseY: -16, topY: 72 },
    { id: 'east-cellblock', x: 930, z: 397, w: 100, d: 160, baseY: -16, topY: 82 },
  ];
  const brokenEdges = KEEP_PIECES.filter((p) => p.edge).map((p) => ({ id: p.id, s0: find(p.id).s0 + 20, s1: find(p.id).s1 - 20, side: p.edge }));
  const ramp = find('drawbridge-ramp'), gap = find('drawbridge-gap'), landing = find('drawbridge-landing');
  const drawbridge = { rampS0: ramp.s0, rampS1: gap.s0, gapS0: gap.s0, gapS1: landing.s0, landingS0: landing.s0, landingS1: landing.s1, widthM: 34, riseM: 10, gapM: 20 };
  const architecture = { cellblocks: castleBlocks,
    halls: ['outer-gate', 'cell-gallery', 'chain-hall', 'high-gate'].map((id) => ({ id, s0: find(id).s0, s1: find(id).s1, heightM: 24, wingM: 28 })),
    courts: [{ id: 'gallows', x: 520, z: 510, w: 170, d: 65, baseY: 14, wallM: 12 }],
    ruins: [{ id: 'collapsed-north-wing', x: 475, z: 590, w: 140, d: 55, baseY: 14, topY: 42 }],
  };
  const stoneFields = [{ id: 'keep-stones', s0: find('stones').s0, s1: find('stones').s1, lanes: [-8, 0, 8], widthM: 32, platformWidthM: 28, platformLengthM: 26, gapM: 4.5 }];
  const bounds = { minX: Infinity, maxX: -Infinity, minZ: Infinity, maxZ: -Infinity, minY: 0, maxY: 0 };
  for (let s = 0; s < track.lengthM; s += 10) {
    const f = track.frameAt(s), margin = f.w / 2;
    bounds.minX = Math.min(bounds.minX, f.p[0] - margin); bounds.maxX = Math.max(bounds.maxX, f.p[0] + margin);
    bounds.minZ = Math.min(bounds.minZ, f.p[2] - margin); bounds.maxZ = Math.max(bounds.maxZ, f.p[2] + margin);
    bounds.minY = Math.min(bounds.minY, f.p[1]); bounds.maxY = Math.max(bounds.maxY, f.p[1]);
  }
  return { landmarks, halls, gates, chains, towers, castleBlocks, architecture, brokenEdges, drawbridge, stoneFields, bounds, widthM: 30, gateWidthM: 14, bypassWidthM: 8 };
}
