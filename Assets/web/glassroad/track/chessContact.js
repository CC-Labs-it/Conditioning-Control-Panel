/* The drawn size of the approved chess pieces, so a head's contact never outgrows its picture.
 * Order is render/chessTrapIds.js: rook, knight, queen. tests/hit-sizes.test.mjs measures the GLBs against these. */

// art/manifest.json sizeM, the bounds render/chessTrap.js fits to its envelope.
export const CHESS_UNIT_M = Object.freeze([[.54293, 1.00012, .4509], [.50758, 1, .45488], [.34201, 1.16957, .60064]]);

// A hanging thumper as drawn (fit to 4 x 5.5 x 4, stretched 1.15 / 1.35 / 1.15, crown down): half extents of its
// lowest 1.21 m, the only part a kart on the road can touch.
export const THUMPER_CONTACT = Object.freeze([Object.freeze({ x: .93, z: .99 }), Object.freeze({ x: .82, z: .91 }), Object.freeze({ x: .52, z: .82 })]);

/** Half extents of a side-mounted head as drawn: chessTrap lays the piece on its side (x and y swap), .92 of the tightest fit. */
export function sideHeadContact(index, w, len, height = 3.2) {
  const [ex, ey, ez] = CHESS_UNIT_M[index % CHESS_UNIT_M.length];
  const k = Math.min(w / ey, height / ex, len / ez) * .92;
  return { x: ey * k / 2, z: ez * k / 2 };
}
