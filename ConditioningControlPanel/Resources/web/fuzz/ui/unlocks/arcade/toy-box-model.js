export const PIECES = Object.freeze(['pawn', 'rook', 'knight', 'bishop', 'queen', 'king']);
const shuffle = (values, rng) => {
  const result = [...values];
  for (let i = result.length - 1; i > 0; i--) {
    const j = Math.max(0, Math.min(i, Math.floor(rng() * (i + 1))));
    [result[i], result[j]] = [result[j], result[i]];
  }
  return result;
};

export function dealBox(round = 0, rng = Math.random) {
  round = Number.isFinite(round) ? Math.max(0, Math.floor(round)) : 0;
  const pieces = shuffle(PIECES, rng).slice(0, Math.min(4, round + 2));
  return { round, pieces, tray: shuffle(pieces, rng), placed: [], phase: 'play' };
}

export function restoreBox(raw) {
  if (!raw || !Number.isInteger(raw.round) || raw.round < 0 || !Array.isArray(raw.pieces) ||
      raw.pieces.length < 2 || raw.pieces.length > 4 || new Set(raw.pieces).size !== raw.pieces.length ||
      raw.pieces.some((id) => !PIECES.includes(id))) return null;
  const placed = [...new Set(Array.isArray(raw.placed) ? raw.placed : [])].filter((id) => raw.pieces.includes(id));
  const tray = Array.isArray(raw.tray) && raw.tray.length === raw.pieces.length &&
    new Set(raw.tray).size === raw.pieces.length && raw.tray.every((id) => raw.pieces.includes(id)) ? [...raw.tray] : [...raw.pieces];
  return { round: raw.round, pieces: [...raw.pieces], tray, placed, phase: placed.length === raw.pieces.length ? 'closing' : 'play' };
}

/** Completion is an edge: a second release or a restored completed box cannot claim it again. */
export function fitPiece(box, piece, hole) {
  if (box.phase !== 'play' || piece !== hole || !box.pieces.includes(piece) || box.placed.includes(piece)) return { box, fitted: false, complete: false };
  const placed = [...box.placed, piece], complete = placed.length === box.pieces.length;
  return { fitted: true, complete, box: { ...box, placed, phase: complete ? 'closing' : 'play' } };
}
