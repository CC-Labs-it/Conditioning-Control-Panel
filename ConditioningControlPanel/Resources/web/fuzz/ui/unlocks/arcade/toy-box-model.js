export const PIECES = Object.freeze(['pawn', 'rook', 'knight', 'bishop', 'queen', 'king']);
export const BOX_WINDOW = 9.5;
export const BOX_FLIP = 1.1;
const clamp = (n, lo, hi) => Math.max(lo, Math.min(hi, Number.isFinite(n) ? n : lo));
const shuffle = (values, rng) => {
  const result = [...values];
  for (let i = result.length - 1; i > 0; i--) {
    const j = Math.floor(clamp(rng(), 0, .999999999) * (i + 1));
    [result[i], result[j]] = [result[j], result[i]];
  }
  return result;
};
export const boxInterval = (rng = Math.random) => 15 + clamp(rng(), 0, 1) * 15;
/** Seconds of the player's rate the first packed box pays. Toy Box arrives around minute 50 now (owner 2026-11-10: "make
 *  the toy box pay more"), so it is twice the old 12 s. It still pays inside the side budget at its weight. */
export const BOX_PRIZE_BASE = 24;
/** First packed box pays BOX_PRIZE_BASE seconds, each previous completion adds 15%, capped at three times that. */
export const boxPrizeSeconds = (completed = 0) => BOX_PRIZE_BASE * Math.min(3, 1 + .15 * Math.max(0, Number.isFinite(completed) ? Math.floor(completed) : 0));

/* Journey ranks (owner 2026-11-10), read live at each deal: 1 Golden Piece, 2 Big Box. Outside Journey both are off. */
export const GOLD_MULT = 2, BIG_BOX_STAY = 1.3, BIG_BOX_MULT = 1.5;
export const BIG_WINDOW = BOX_WINDOW * BIG_BOX_STAY;
export const toyBoxRank = (state) => Math.max(0, Math.floor(Number(state?.store?.journey?.ranks?.toy_box) || 0));
export const boxPerks = (rank = 0) => ({ gold: rank >= 1, big: rank >= 2 });
/** What this box's prize is multiplied by: Golden Piece doubles it once the gold piece is in, Big Box pays 1.5x. */
export const boxPrizeMult = (box) => (box?.gold && box.placed?.includes(box.gold) ? GOLD_MULT : 1) * (box?.big ? BIG_BOX_MULT : 1);
/** The full open window of this box, for the meter. */
export const boxWindow = (box) => (box?.big ? BIG_WINDOW : BOX_WINDOW);

export function createToyBox(rng = Math.random) {
  const interval = boxInterval(rng);
  return { v: 2, round: 0, completed: 0, pieces: [], tray: [], placed: [], phase: 'closed', interval, nextIn: interval, windowLeft: 0, flipLeft: 0, outcome: null };
}

/** Deal only when the lid opens. Missed partial boxes are never carried forward. */
export function dealBox(completed = 0, rng = Math.random, perks = null) {
  completed = Math.max(0, Math.floor(Number.isFinite(completed) ? completed : 0));
  const pieces = shuffle(PIECES, rng).slice(0, Math.min(4, completed + 2)), interval = boxInterval(rng);
  const box = { v: 2, round: completed, completed, pieces, tray: shuffle(pieces, rng), placed: [], phase: 'play', interval, nextIn: interval, windowLeft: BOX_WINDOW, flipLeft: 0, outcome: null };
  // Extra draws only with a rank, so a run without ranks deals exactly the boxes it always did.
  if (perks?.gold) box.gold = pieces[Math.min(pieces.length - 1, Math.floor(clamp(rng(), 0, .999999999) * pieces.length))];
  if (perks?.big) { box.big = true; box.windowLeft = BIG_WINDOW; }
  return box;
}

export function restoreBox(raw) {
  if (!raw || !Number.isInteger(raw.round) || raw.round < 0 || !Array.isArray(raw.pieces) ||
      raw.pieces.length > 4 || new Set(raw.pieces).size !== raw.pieces.length || raw.pieces.some((id) => !PIECES.includes(id))) return null;
  const placed = [...new Set(Array.isArray(raw.placed) ? raw.placed : [])].filter((id) => raw.pieces.includes(id));
  const tray = Array.isArray(raw.tray) && raw.tray.length === raw.pieces.length && new Set(raw.tray).size === raw.pieces.length && raw.tray.every((id) => raw.pieces.includes(id)) ? [...raw.tray] : [...raw.pieces];
  if (raw.v !== 2) {
    if (raw.pieces.length < 2) return null;
    const packed = placed.length === raw.pieces.length, completed = raw.round + Number(packed);
    return { v: 2, round: completed, completed, pieces: [...raw.pieces], tray, placed, phase: packed ? 'closing' : 'play', interval: 22.5, nextIn: 22.5, windowLeft: packed ? 0 : BOX_WINDOW, flipLeft: packed ? BOX_FLIP : 0, outcome: packed ? 'packed' : null };
  }
  if (!['closed', 'play', 'closing'].includes(raw.phase)) return null;
  if (raw.phase === 'play' && raw.pieces.length < 2) return null;
  const completed = Number.isInteger(raw.completed) && raw.completed >= 0 ? raw.completed : raw.round;
  const big = raw.big === true, gold = raw.pieces.includes(raw.gold) ? raw.gold : null;
  return { v: 2, round: completed, completed, pieces: [...raw.pieces], tray, placed,
    phase: raw.phase, interval: clamp(raw.interval, 15, 30), nextIn: clamp(raw.nextIn, 0, 30),
    windowLeft: clamp(raw.windowLeft, 0, big ? BIG_WINDOW : BOX_WINDOW), flipLeft: clamp(raw.flipLeft, 0, BOX_FLIP), outcome: ['packed', 'miss'].includes(raw.outcome) ? raw.outcome : null,
    ...(gold ? { gold } : {}), ...(big ? { big } : {}) };
}

/** Active seconds only. Pause and reload never advance this state, and no elapsed interval is rerolled.
 *  `perks` (boxPerks of the live rank) shape the next box dealt. */
export function advanceBox(box, seconds, rng = Math.random, perks = null) {
  let b = { ...box }, left = Math.max(0, Number.isFinite(seconds) ? seconds : 0), opened = false, timedOut = false;
  for (let guard = 0; guard < 1000; guard++) {
    if (b.phase === 'play' && b.windowLeft <= 0) {
      b = { ...b, phase: 'closing', flipLeft: BOX_FLIP, windowLeft: 0, pieces: [], tray: [], placed: [], outcome: 'miss' }; timedOut = true;
    }
    if (b.phase === 'closing' && b.flipLeft <= 0) b = { ...b, phase: 'closed', pieces: [], tray: [], placed: [] };
    if (b.phase === 'closed' && b.nextIn <= 0) { b = dealBox(b.completed, rng, perks); opened = true; }
    if (left <= 0) break;
    const boundary = b.phase === 'play' ? b.windowLeft : b.phase === 'closing' ? b.flipLeft : b.nextIn;
    const dt = Math.min(left, boundary);
    b.nextIn = Math.max(0, b.nextIn - dt);
    if (b.phase === 'play') b.windowLeft = Math.max(0, b.windowLeft - dt);
    if (b.phase === 'closing') b.flipLeft = Math.max(0, b.flipLeft - dt);
    left -= dt;
  }
  return { box: b, opened, timedOut };
}

/** Completion is an edge: a second release or a restored completed box cannot claim it again. */
export function fitPiece(box, piece, hole) {
  if (box.phase !== 'play' || box.windowLeft <= 0 || piece !== hole || !box.pieces.includes(piece) || box.placed.includes(piece)) return { box, fitted: false, complete: false };
  const placed = [...box.placed, piece], complete = placed.length === box.pieces.length, completed = box.completed + Number(complete);
  return { fitted: true, complete, box: { ...box, placed, completed, round: completed,
    phase: complete ? 'closing' : 'play', windowLeft: complete ? 0 : box.windowLeft, flipLeft: complete ? BOX_FLIP : 0, outcome: complete ? 'packed' : null } };
}
