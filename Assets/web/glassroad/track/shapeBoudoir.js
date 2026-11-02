import { BOUDOIR_PIECES, boudoirSegmentCount } from './boudoir-layout.js';
import { broadPieces, reshapePiecesNodes } from './reshapePieces.js';

export function shapeBoudoirNodes(nodes, json) {
  // The shelf rises in one broad circuit; the wardrobe's two real drop shortcuts remain.
  const pieces = broadPieces(BOUDOIR_PIECES, { 'shelf-climb': { turn: 360, r: 145 }, 'bath-stream': { len: 600 },
    'powder-sweep': { r: 400 }, 'perfume-court': { len: 260 } });
  let node = 0;
  const lockedNodes = [];
  for (const p of BOUDOIR_PIECES) {
    const count = boudoirSegmentCount(p);
    if (p.id === 'perfume-court') for (let k = -3; k < 4; k++) lockedNodes.push(node + k);
    node += count;
  }
  // The long entry sweep must finish before the court's flat, visible cloud approach.
  const shape = reshapePiecesNodes(nodes, BOUDOIR_PIECES, pieces, boudoirSegmentCount, { lockedNodes });
  shape.forks = (json.forks || []).map(f => {
    const points = f.points.map(p => ({ ...p, p: shape.mapPoint(p.p) }));
    points[0].p = nodes[f.from].p.slice(); points.at(-1).p = nodes[f.to].p.slice();
    return { ...f, points };
  });
  return shape;
}
