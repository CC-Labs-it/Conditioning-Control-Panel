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
/** First packed box pays 12 seconds, each previous completion adds 15%, capped at 36 seconds. */
export const boxPrizeSeconds = (completed = 0) => 12 * Math.min(3, 1 + .15 * Math.max(0, Number.isFinite(completed) ? Math.floor(completed) : 0));

export function createToyBox(rng = Math.random) {
  const interval = boxInterval(rng);
  return { v: 2, round: 0, completed: 0, pieces: [], tray: [], placed: [], phase: 'closed', interval, nextIn: interval, windowLeft: 0, flipLeft: 0, outcome: null };
}

/** Deal only when the lid opens. Missed partial boxes are never carried forward. */
export function dealBox(completed = 0, rng = Math.random) {
  completed = Math.max(0, Math.floor(Number.isFinite(completed) ? completed : 0));
  const pieces = shuffle(PIECES, rng).slice(0, Math.min(4, completed + 2)), interval = boxInterval(rng);
  return { v: 2, round: completed, completed, pieces, tray: shuffle(pieces, rng), placed: [], phase: 'play', interval, nextIn: interval, windowLeft: BOX_WINDOW, flipLeft: 0, outcome: null };
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
  return { v: 2, round: completed, completed, pieces: [...raw.pieces], tray, placed,
    phase: raw.phase, interval: clamp(raw.interval, 15, 30), nextIn: clamp(raw.nextIn, 0, 30),
    windowLeft: clamp(raw.windowLeft, 0, BOX_WINDOW), flipLeft: clamp(raw.flipLeft, 0, BOX_FLIP), outcome: ['packed', 'miss'].includes(raw.outcome) ? raw.outcome : null };
}

/** Active seconds only. Pause and reload never advance this state, and no elapsed interval is rerolled. */
export function advanceBox(box, seconds, rng = Math.random) {
  let b = { ...box }, left = Math.max(0, Number.isFinite(seconds) ? seconds : 0), opened = false, timedOut = false;
  for (let guard = 0; guard < 1000; guard++) {
    if (b.phase === 'play' && b.windowLeft <= 0) {
      b = { ...b, phase: 'closing', flipLeft: BOX_FLIP, windowLeft: 0, pieces: [], tray: [], placed: [], outcome: 'miss' }; timedOut = true;
    }
    if (b.phase === 'closing' && b.flipLeft <= 0) b = { ...b, phase: 'closed', pieces: [], tray: [], placed: [] };
    if (b.phase === 'closed' && b.nextIn <= 0) { b = dealBox(b.completed, rng); opened = true; }
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
