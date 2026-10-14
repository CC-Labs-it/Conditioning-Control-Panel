/* ============================================================================
 * ui/ladder-plan.js - the ladder's numbers and words, pure (no DOM, no clock).
 *
 * The server keeps the Elo (contract section 5): GET /v2/pbp/ladder is the top
 * 50 by name, GET /v2/pbp/ladder/me is your own rank, rating, games and best.
 * This file turns those answers, and a finished online game's rating_delta,
 * into what the door shows: the chip, the list, and the end card's roll with
 * its +N / -N stamp and a "new best" stamp. ui/ladder.js draws it.
 * ==========================================================================*/

export const LADDER_TUNING = Object.freeze({
  start: 1200,          // every account starts here (server ELO_START)
  ratedMinPly: 10,      // under this a finished game is not rated (server RATED_MIN_PLY)
  rollMs: 900,          // the end card's number rolls this long
  rollTicks: 6,         // at most this many tick sounds along the roll
  stampDelayMs: 80,     // the +N stamp lands this long after the roll does
  bestDelayMs: 520,     // and "new best" this long after that
  cacheMs: 60000,       // the top list is cached a minute (the server says the same)
});
const L = LADDER_TUNING;

const int = (v) => (v === null || v === undefined || v === '' || !Number.isFinite(Number(v)) ? null : Math.round(Number(v)));

/** GET ladder/me's answer, or null for nothing usable. */
export function normMe(d) {
  if (!d || typeof d !== 'object') return null;
  const rating = int(d.rating);
  const rated = d.rated === true && rating !== null;
  return {
    rated,
    rating: rated ? rating : null,
    rank: rated ? int(d.rank) : null,
    games: Math.max(0, int(d.games) || 0),
    best: rated ? Math.max(rating, int(d.best) || 0) : null,
    total: Math.max(0, int(d.total) || 0),
  };
}

/** GET ladder's answer: rows by rank, names only. */
export function normTop(d) {
  const rows = (d && Array.isArray(d.rows) ? d.rows : []).map((r) => ({
    rank: int(r && r.rank),
    name: String((r && (r.display_name || r.name)) || 'Player').slice(0, 32),
    rating: int(r && r.rating),
    games: Math.max(0, int(r && r.games) || 0),
  })).filter((r) => r.rank !== null && r.rating !== null);
  return { rows, total: Math.max(rows.length, int(d && d.total) || 0) };
}

/** The chip beside your name: "1248", "unrated", or nothing before the server answers. */
export function chipText(me) {
  if (!me) return '';
  return me.rated ? String(me.rating) : 'unrated';
}

/** "rank 17 of 312", or "" when there is no rank to say. */
export function rankText(me) {
  if (!me || !me.rated || me.rank === null) return '';
  return me.total ? `rank ${me.rank} of ${me.total}` : `rank ${me.rank}`;
}

/**
 * Mark your own row. The list carries names only, so it is yours when the rank
 * and rating are the ones ladder/me gave you, and the name agrees when known.
 */
export function markMine(rows, me, myName) {
  const name = String(myName || '').trim().toLowerCase();
  return (rows || []).map((r) => ({
    ...r,
    mine: !!(me && me.rated && r.rank === me.rank && r.rating === me.rating && (!name || r.name.toLowerCase() === name)),
  }));
}

/**
 * The end card's rating beat for one finished online game.
 *   delta   this seat's rating_delta (null when the server said unrated)
 *   before  ladder/me from before the game (normMe), when we had it
 *   after   ladder/me after it, when it came back
 *   plies, reason   for the "why it was not rated" line
 * -> { rated, from, to, delta, stamp, tone, newBest, rankFrom, rankTo, note }
 */
export function endPlan({ delta = null, before = null, after = null, plies = 0, reason = '' } = {}) {
  let d = int(delta);
  // An ending that came without the delta: the two reads still tell it, if a game was counted between them.
  if (d === null && before && after && before.rated && after.rated && after.games > before.games) d = after.rating - before.rating;
  if (d === null) {
    const why = String(reason || '').toLowerCase() === 'abandon' ? 'abandoned games are not rated'
      : (Number(plies) || 0) < L.ratedMinPly ? 'too short to rate' : 'not rated';
    return { rated: false, from: null, to: null, delta: null, stamp: '', tone: 'flat', newBest: false, rankFrom: null, rankTo: null, note: why };
  }
  const to = after && after.rated ? after.rating : ((before && before.rated ? before.rating : L.start) + d);
  const from = to - d;
  const prevBest = before && before.rated ? (before.best ?? before.rating) : L.start;
  const newBest = d > 0 && to > prevBest && (!after || !after.rated || after.best === null || after.best <= to);
  const rankFrom = before && before.rated ? before.rank : null;
  const rankTo = after && after.rated ? after.rank : null;
  return {
    rated: true, from, to, delta: d,
    stamp: d > 0 ? `+${d}` : d < 0 ? `-${Math.abs(d)}` : '+0',
    tone: d > 0 ? 'up' : d < 0 ? 'down' : 'flat',
    newBest, rankFrom, rankTo,
    note: rankTo !== null && rankFrom !== null && rankTo !== rankFrom ? `rank ${rankFrom} to ${rankTo}`
      : rankTo !== null ? `rank ${rankTo}` : '',
  };
}

/** The rolling number at t in 0..1: ease-out cubic, whole points. */
export function rollValue(from, to, t) {
  const k = Math.min(1, Math.max(0, Number(t) || 0));
  const e = 1 - Math.pow(1 - k, 3);
  return Math.round(from + (to - from) * e);
}

/* ---- the Ladder screen as a board (2026-10-14 polish pass) ----------------
 * The top three stand on a podium (2nd, 1st, 3rd, left to right, the winner
 * tallest), the rest are the list. Your own rating card rolls up to your
 * number from where this page last showed it (from the 1200 start the first
 * time). The arrival plays once per visit; the podium's sounds only on the
 * first visit of a session (repetition shrinks the party, playbook section 3). */
export const BOARD_TUNING = Object.freeze({
  podiumAtMs: 120,      // the podium starts rising this long after the screen lands
  stepMs: 340,          // one podium step's rise (THUD)
  stepGapMs: 110,       // between steps: third, then second, then first
  rowGapMs: 26,         // between list rows
  rowCap: 12,           // rows past this land with the twelfth
  youAtMs: 260,         // your card's roll starts this long in
  youRollMs: 900,       // and lasts this long
  scrollAtMs: 560,      // your row is scrolled to once the rows have mostly landed
  freshMs: 1500,        // a repaint this soon after a mount continues the same arrival
});

/** Split the ladder into a podium (display order 2nd, 1st, 3rd) and the rest. */
export function podiumOf(rows) {
  const list = Array.isArray(rows) ? rows : [];
  const top = list.slice(0, 3).map((r, i) => ({ ...r, place: i + 1 }));
  const order = [top[1], top[0], top[2]].filter(Boolean);
  return { podium: order, rest: list.slice(3) };
}

/** When podium place 1..3 lands, ms after the screen: third first, the winner last. */
export function podiumAt(place) {
  const B = BOARD_TUNING;
  const k = 3 - Math.max(1, Math.min(3, place | 0));   // place 3 -> 0, place 1 -> 2
  return B.podiumAtMs + k * B.stepGapMs;
}

/** When list row i lands, ms after the screen (just behind the podium). */
export function rowAt(i) {
  const B = BOARD_TUNING;
  return B.podiumAtMs + 2 * B.stepGapMs + Math.min(Math.max(0, i | 0), B.rowCap) * B.rowGapMs;
}

/** Your card's roll: from what this page last showed, else from the start. Null = nothing to roll. */
export function youRoll(me, seen = null) {
  if (!me || !me.rated || me.rating === null) return null;
  const to = me.rating;
  const from = Number.isFinite(seen) ? seen : L.start;
  return from === to ? null : { from, to };
}

/** Where along the roll the tick sounds go (0..1), at most rollTicks, none for a flat game. */
export function tickTimes(from, to) {
  const n = Math.min(L.rollTicks, Math.abs((to | 0) - (from | 0)));
  return Array.from({ length: n }, (_, i) => (i + 1) / (n + 1));
}
