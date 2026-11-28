/* ============================================================================
 * game/cup-replay.js - a finished game handed in by the host page, read.
 *
 * Pure: no DOM, no window, no THREE. net/replay-seat.js plays what this decodes.
 *
 * THE HOST CONTRACT. Before boot.js runs, the host page's shim may set
 *   window.PBP_REPLAY = { record: { match_id, white_name, black_name,
 *     result ("1-0" | "0-1" | "1/2-1/2"), reason, start_fen (null = standard),
 *     moves (UCI strings), clocks?, round, board }, reel: boolean }
 * or a Promise of that object (a shim that has to fetch the record sets the
 * promise synchronously; boot waits for it and opens no door meanwhile).
 * The page then boots straight into the game on the 3D board, as a spectator:
 * no door, no lobby, no network. reel false = the whole game with controls,
 * reel true = a short highlights reel with none. Either way the page fires
 *   window 'pbp:replay-done' { detail: { match_id } }   once, at the end.
 * boot.js always sets window.PBP_CAPS = { replay: 1 }.
 * ==========================================================================*/

import { Chess } from '../vendor/chess.js';
import { fallPlan } from '../door/fall.js';

const UCI = /^[a-h][1-8][a-h][1-8][qrbn]?$/;
const text = (v, max = 40) => (typeof v === 'string' || typeof v === 'number' ? String(v).trim().slice(0, max) : '');

/** The host's object, checked. Null when it is not a replay request at all. */
export function readHostReplay(raw) {
  const r = raw && typeof raw === 'object' ? raw.record : null;
  if (!r || typeof r !== 'object' || !Array.isArray(r.moves) || !r.moves.length) return null;
  return {
    reel: raw.reel === true,
    record: {
      match_id: text(r.match_id, 80),
      white: text(r.white_name) || 'White',
      black: text(r.black_name) || 'Black',
      result: ['1-0', '0-1', '1/2-1/2'].includes(r.result) ? r.result : null,
      reason: text(r.reason).toLowerCase(),
      start_fen: typeof r.start_fen === 'string' && r.start_fen.trim() ? r.start_fen.trim() : null,
      moves: r.moves.map((m) => String(m || '').trim().toLowerCase()),
      round: text(r.round, 24),
      board: text(r.board, 24),
    },
  };
}

/**
 * UCI moves -> what the board plays. `plays` are {from, to, promotion} for the
 * referee, `steps` are chess.js's verbose moves (san, captured, flags, color).
 * A damaged tail stops at the last legal position, like door/replay.js.
 */
export function buildSteps(record) {
  let chess;
  try { chess = record.start_fen ? new Chess(record.start_fen) : new Chess(); } catch { chess = new Chess(); }
  const plays = [], steps = [];
  for (const uci of record.moves || []) {
    if (!UCI.test(uci)) break;
    const play = { from: uci.slice(0, 2), to: uci.slice(2, 4), promotion: uci[4] || undefined };
    let m = null;
    try { m = chess.move(play); } catch { m = null; }
    if (!m) break;
    plays.push(play); steps.push(m);
  }
  return { plays, steps, sans: steps.map((s) => s.san), mate: chess.isCheckmate() };
}

/** The end of the game for the bus and the result line: { result, winner, reason }. */
export function endOf(record, natural = null) {
  if (natural) return natural;
  const winner = record.result === '1-0' ? 'w' : record.result === '0-1' ? 'b' : null;
  const reason = record.reason || (winner ? 'resign' : 'draw');
  return { result: reason, winner, reason };
}

const VALUE = { p: 1, n: 3, b: 3, r: 5, q: 9, k: 0 };

/**
 * The moments a reel shows, in game order: [{ kind, ply, from, to }].
 * The board jumps to position `from` (plies played) and plays through `to`.
 *   end      the mate or the last move                       always
 *   fall     the worst graded move through its punishment    when `iq` tracks are given (door/fall.js)
 *   promote  a pawn reaching the far rank
 *   capture  the biggest captures: what came off, less what the taker lost on the next move
 * A moment closer than `spread` plies to a better one is folded into it.
 */
export function pickMoments(steps, { max = 4, lead = 3, spread = 4, iq = null } = {}) {
  const n = steps.length;
  if (!n) return [];
  const found = [{ kind: 'end', ply: n, score: 100 }];
  const sans = steps.map((s) => s.san);
  let fall = null;
  for (const me of ['w', 'b']) {
    const plan = iq ? fallPlan({ me, iq, moves: sans }) : null;
    if (plan && (!fall || plan.worst.loss > fall.worst.loss)) fall = plan;
  }
  if (fall) found.push({ kind: 'fall', ply: fall.to, from: fall.from, score: 50 });
  steps.forEach((s, i) => {
    const ply = i + 1;
    if (s.promotion) found.push({ kind: 'promote', ply, score: 20 + VALUE[s.promotion] });
    if (s.captured) {
      const back = steps[i + 1];
      const lost = back && back.captured && back.to === s.to ? VALUE[s.piece] : 0;
      const net = VALUE[s.captured] - lost;
      if (net >= 3) found.push({ kind: 'capture', ply, score: net });
    }
  });
  found.sort((a, b) => b.score - a.score || a.ply - b.ply);
  const kept = [];
  for (const m of found) {
    if (kept.length >= max) break;
    const near = kept.find((k) => Math.abs(k.ply - m.ply) < spread);
    // a moment just before a better one is not cut: the better one starts early enough to show it
    if (near) { if (m.ply < near.ply) near.from = Math.min(near.from ?? near.ply - lead, m.from ?? m.ply - 1); continue; }
    kept.push(m);
  }
  kept.sort((a, b) => a.ply - b.ply);
  let floor = 0;
  return kept.map((m) => {
    const from = Math.max(floor, Math.min(m.from ?? m.ply - lead, m.ply - 1), 0);
    floor = m.ply;
    return { kind: m.kind, ply: m.ply, from, to: m.ply };
  });
}

/** The reel's caption lines: where the game sat, who played, which move. */
export function captionOf(record, ply) {
  const num = (v) => /^\d+$/.test(v);
  const seat = [record.round && (num(record.round) ? 'Round ' + record.round : record.round),
    record.board && (num(record.board) ? 'Board ' + record.board : record.board)].filter(Boolean).join(', ');
  return { seat, names: record.white + ' vs ' + record.black, move: 'Move ' + Math.max(1, Math.ceil(ply / 2)) };
}
