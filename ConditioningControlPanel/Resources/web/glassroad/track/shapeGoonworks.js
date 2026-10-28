// Broad sweeps retain the source node identities used by scenery, flights and shortcuts.
import { GOONWORKS_PIECES, goonworksPieceLength, goonworksSegmentCount, walkGoonworks } from './goonworks-layout.js';

const RAD = Math.PI / 180;
const smooth = u => u * u * (3 - 2 * u);
export const GOONWORKS_SHAPE_REVISION = 1;

export function shapeGoonworksNodes(nodes) {
  const pieces = GOONWORKS_PIECES.map(piece => ({ ...piece }));
  for (const piece of pieces) {
    if (!piece.turn) continue;
    if (piece.id === 'ribbon-braid') Object.assign(piece, { turn: 360, r: 330 });
    // Keep the full extra revolution: both drop shortcuts land one turn ahead.
    else if (piece.id === 'fold-descent') Object.assign(piece, { r: 220, rEnd: 260 });
    else piece.r = 140;
  }
  // Separate the widened fold from the rising ribbon, with a useful straight runout.
  pieces.find(piece => piece.id === 'pinball-table').len = 660;
  pieces.find(piece => piece.id === 'leapfrog-approach').len = 170;
  const end = walkGoonworks(pieces);
  pieces.find(piece => piece.id === 'home-straight').len -= end.x;
  pieces.find(piece => piece.id === 'finish-crossing').len -= end.z;
  const plan = walkGoonworks(pieces);
  let node = 0;
  for (let i = 0; i < pieces.length; i++) {
    const piece = pieces[i], start = plan.starts[i];
    const count = goonworksSegmentCount(GOONWORKS_PIECES[i]), length = goonworksPieceLength(piece);
    for (let k = 0; k < count; k++) {
      const u = k / count, sign = Math.sign(piece.turn || 0);
      // Keep the flat hall's original end spacing beside the raised launch ramp.
      const distance = piece.id === 'pinball-table'
        ? GOONWORKS_PIECES[i].len * u + (length - GOONWORKS_PIECES[i].len) * smooth(Math.max(0, Math.min(1, (k - 2) / (count - 4))))
        : u * length;
      const heading = start.heading + (piece.turn || 0) * RAD * u;
      const grow = sign * ((piece.rEnd || piece.r) - piece.r) * smooth(u);
      const point = nodes[node++].p;
      point[0] = piece.turn ? start.x + sign * piece.r * (Math.sin(heading) - Math.sin(start.heading)) + grow * Math.sin(heading)
        : start.x + Math.cos(start.heading) * distance;
      point[2] = piece.turn ? start.z + sign * piece.r * (Math.cos(start.heading) - Math.cos(heading)) - grow * Math.cos(heading)
        : start.z + Math.sin(start.heading) * distance;
    }
  }
}
