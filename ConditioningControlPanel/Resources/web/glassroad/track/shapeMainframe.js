import { MAINFRAME_PIECES } from './mainframe-pieces.js';
import { broadPieces, pieceLength, reshapePiecesNodes } from './reshapePieces.js';

export function shapeMainframeNodes(nodes) {
  const pieces = broadPieces(MAINFRAME_PIECES, { 'bus-runout': { len: 140 }, 'bus-return-turn': { r: 210 } });
  return reshapePiecesNodes(nodes, MAINFRAME_PIECES, pieces,
    p => Math.round(pieceLength(p) / (p.rEnd ? 10.8 : 10)),
    { lockedNodes: [...Array.from({ length: 12 }, (_, i) => 53 + i),
      ...Array.from({ length: 14 }, (_, i) => 481 + i)],
    sampleU: (p, u) => {
      if (p.id === 'bus-runout') return u * 100 / p.len;
      if (p.id !== 'bus-descent' || u <= .92) return u;
      const t = (u - .92) / .08;
      return u + .5 * (1 - u) * t * t * (3 - 2 * t);
    } });
}
