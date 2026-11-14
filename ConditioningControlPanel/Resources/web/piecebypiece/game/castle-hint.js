/* ============================================================================
 * game/castle-hint.js - castling, the way lichess and chess.com read it.
 *
 * A cup player moved the rook two squares when they meant to castle, twice
 * (owner's cup thread, 2026-11-13: "I should def put a nudge there to castle").
 * Two things answer that, and both come from the moves the referee already
 * allows, so nothing here decides what is legal:
 *
 *   the hint   the king's castling square gets its own marker (kind 'castle'),
 *              and the rook it would jump gets a thin one ('castle-rook'), only
 *              while that castle is legal right now
 *   the alias  putting the king on his own rook (drag or click) plays the
 *              castle, the same move as the king's two steps
 *
 * Standard chess only: the king castles from e1/e8 with the rook on the a or h
 * file of the same rank. No three.js, so node tests it straight.
 * ==========================================================================*/

/** The rook a castle jumps: the h file for 'k', the a file for 'q'. */
export function castleRook(move) {
  const flags = String(move?.flags || '');
  const side = flags.includes('k') ? 'h' : (flags.includes('q') ? 'a' : null);
  if (!side || !move.from) return null;
  return side + String(move.from)[1];
}

/**
 * From the verbose moves of one square: every legal castle, both ways round.
 *   targets  king square -> rook square   (where the castle marker goes)
 *   rooks    rook square -> king square   (a drop on the rook plays this)
 * Empty maps for any man but a king that may castle right now.
 */
export function readCastles(moves = []) {
  const targets = new Map();
  const rooks = new Map();
  for (const m of moves || []) {
    if (!m || (m.piece && m.piece !== 'k')) continue;
    const rook = castleRook(m);
    if (!rook || !m.to) continue;
    targets.set(m.to, rook);
    rooks.set(rook, m.to);
  }
  return { targets, rooks };
}

/** Where a drop really goes: the king put on his own castling rook goes to his castle square. */
export function castleAlias(castles, to) {
  return castles && castles.rooks && castles.rooks.has(to) ? castles.rooks.get(to) : to;
}
