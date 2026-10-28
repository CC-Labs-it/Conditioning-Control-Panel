import { KEEP_PIECES, keepSegmentCount } from './keep-layout.js';
import { broadPieces, reshapePiecesNodes } from './reshapePieces.js';

export function shapeKeepNodes(nodes) {
  // One broad ascent replaces three tight repeated circles. Descending shortcut turns stay intact.
  const pieces = broadPieces(KEEP_PIECES, { 'tower-spiral': { turn: -540, r: 145 },
    // A longer home run pays another boost train without borrowing the next bend's braking room.
    home: { len: 180 } });
  return reshapePiecesNodes(nodes, KEEP_PIECES, pieces, keepSegmentCount);
}
