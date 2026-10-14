/* ============================================================================
 * smoke/fake-tape.mjs - an in-process stand-in for the spectate v2 server,
 * shared by tape-smoke and watch-smoke. It follows the real one
 * (CCP-Server proxy/pbp.js + pbp-routes.js, fe22bb5):
 *
 *   - chunk_ms / delay_ms default to the page's own constants (net/tape.js),
 *     which mirror the server's WATCH_CHUNK_MS 2000 and the 12 s delay;
 *   - seq is the match's EVENT seq: a draw offer takes one too, so the moves'
 *     seqs have holes;
 *   - a trimmed opening (`trim: k`) comes back from upto with the first k moves'
 *     seq / at / clk null, and 410 `gone` when the instant asked for is older
 *     than everything still listed;
 *   - the end rides the mating move at the SAME `at` (move first, then end);
 *   - 425 not_ready before a chunk (or bucket) is ready; one-shot 410 / 425 /
 *     429 / 503 answers per route through `st.force`;
 *   - fx records are stamped the server's way (addFxPost: at = now - ago,
 *     clamped into the turn and to at most FX_BACK back, entries before `at`
 *     dropped by d < 0).
 *
 * Every call is logged in `calls`, every POST in `posts`.
 * ==========================================================================*/

import { Chess } from '../vendor/chess.js';
import { CHUNK_MS, WATCH_DELAY_MS } from '../net/tape.js';

export const T0 = 1757400000000;
export const DELAY = WATCH_DELAY_MS;
export const CHUNK = CHUNK_MS;
export const INITIAL = 300000;
/** The server's FX_BACK_MAX_MS / FX_WRITE_MARGIN_MS. */
export const FX_BACK_MAX_MS = 10000;
export const FX_WRITE_MARGIN_MS = 3000;

const env = (status, data, error = null) => ({ ok: status >= 200 && status < 300, status, data, error });
const ERR = { 410: 'gone', 425: 'not_ready', 429: 'rate_limited', 503: 'server_error' };
const ORDER = { move: 0, fx: 1, end: 2 };

export function fakeTapeServer({ t0 = T0, delay = DELAY, chunk = CHUNK, initial = INITIAL, trim = 0 } = {}) {
  const st = { now: t0, watchers: 2, refuse: null, chunkRefuse: null, notReadyOnce: new Set(), reactions: new Map(), noT0: false, force: {}, trim };
  const chess = new Chess();
  let seq = 0;
  const moves = [];        // { seq, at, uci, clk }
  const fx = [];           // { k:'fx', at, side, ply, heat, fired }
  let end = null;          // { k:'end', at, result, reason }
  const left = { w: initial, b: initial };
  let turnAt = t0;
  const calls = [];
  const posts = [];

  function play(uci, at) {
    const side = chess.turn();
    left[side] -= (at - turnAt);
    chess.move({ from: uci.slice(0, 2), to: uci.slice(2, 4), promotion: uci[4] || 'q' });
    turnAt = at;
    moves.push({ seq: ++seq, at, uci, clk: [left.w, left.b] });
  }
  /** A draw offer: no move, but it takes a seq (the hole the page must not read as a missed move). */
  function offerDraw() { seq++; }
  function addFx(rec) { fx.push({ k: 'fx', ...rec }); }
  /**
   * A seat's POST match/:id/fx at server time `now`, stamped as fxRecord does.
   * `from` = the turn's start (defaults to the last move), `to` = its end (now while it runs).
   */
  function addFxPost({ now, ago, side, ply, heat = [], fired = [], from = turnAt, to = now }) {
    const back = Math.min(FX_BACK_MAX_MS, Math.max(0, delay - FX_WRITE_MARGIN_MS));
    const rawAt = now - ago;
    const hi = Math.min(now, to);
    if (hi < now - back) return null;
    const at = Math.min(hi, Math.max(rawAt, from, now - back));
    const shift = at - rawAt;
    const span = hi - at;
    const dtOf = (v) => { const d = Math.round(Number(v) - shift); return Number.isFinite(d) && d >= 0 && d <= span ? d : null; };
    const h = heat.map((x) => [dtOf(x[0]), x[1]]).filter((x) => x[0] !== null);
    const f = fired.map((x) => [dtOf(x[0]), ...x.slice(1)]).filter((x) => x[0] !== null);
    if (!h.length && !f.length) return null;
    const rec = { k: 'fx', at, side, ply, heat: h, fired: f };
    fx.push(rec);
    return rec;
  }
  /** The end; `at` defaults to the last move's (a mate rides its move). */
  function finish(at, result, reason) { seq++; end = { k: 'end', at: at === undefined ? moves[moves.length - 1].at : at, result, reason }; }

  /** Balances at server time T, the side to move discounted to T (frozen at the end). */
  function clocksAt(T) {
    const before = moves.filter((m) => m.at < T);
    const last = before[before.length - 1];
    const turn = before.length % 2 === 0 ? 'w' : 'b';
    const base = last ? { w: last.clk[0], b: last.clk[1] } : { w: initial, b: initial };
    const since = last ? last.at : t0;
    const stop = end && end.at < T ? end.at : T;
    base[turn] = Math.max(0, base[turn] - (stop - since));
    return { w_ms: base.w, b_ms: base.b, turn };
  }
  const endOfChunk = (n) => t0 + (n + 1) * chunk;
  const ready = (n) => endOfChunk(n) <= st.now - delay;
  const readyChunk = () => Math.floor((st.now - delay - t0) / chunk) - 1;
  /** A one-shot forced answer for a route ('head' | 'upto' | 'chunk' | 'reactions' | 'react'). */
  function forced(route) {
    const f = st.force[route];
    if (!f) return null;
    delete st.force[route];
    const error = f.error || ERR[f.status] || 'server_error';
    return env(f.status, { error }, error);
  }

  const api = {
    calls, posts,
    async watchHead(id) {
      calls.push('head');
      const f = forced('head'); if (f) return f;
      if (st.refuse) return env(st.refuse.status, { error: st.refuse.error }, st.refuse.error);
      const data = {
        ok: true, match_id: id, chunk_ms: chunk, delay_ms: delay, server_now_ms: st.now,
        ready_chunk: readyChunk(), watchers: st.watchers, status: end && end.at <= st.now ? 'done' : 'live',
        players: { w: { display_name: 'velvet', rating: 1301 }, b: { display_name: 'moth', rating: null } },
        time_control: { initial_ms: initial, increment_ms: 0 },
      };
      if (!st.noT0) data.t0_ms = t0;
      return env(200, data);
    },
    async watchUpto(id, n) {
      calls.push('upto:' + n);
      const f = forced('upto'); if (f) return f;
      if (st.chunkRefuse) return env(st.chunkRefuse.status, { error: st.chunkRefuse.error }, st.chunkRefuse.error);
      if (!ready(n)) return env(425, { error: 'not_ready' }, 'not_ready');
      const to = endOfChunk(n);
      const listed = moves.slice(st.trim);
      // trimmed past the instant asked for: nothing still listed is older than it
      if (st.trim && !(listed.length && listed[0].at < to)) return env(410, { error: 'gone' }, 'gone');
      const out = {
        n, to_ms: to,
        moves: moves.filter((m, i) => i < st.trim || m.at < to).map((m, i) => (i < st.trim ? { seq: null, at: null, uci: m.uci, clk: null } : m)),
        clocks: clocksAt(to),
      };
      if (end && end.at < to) out.end = { at: end.at, result: end.result, reason: end.reason };
      return env(200, out);
    },
    async watchChunk(id, n) {
      calls.push('chunk:' + n);
      const f = forced('chunk'); if (f) return f;
      if (st.chunkRefuse) return env(st.chunkRefuse.status, { error: st.chunkRefuse.error }, st.chunkRefuse.error);
      if (st.notReadyOnce.has(n)) { st.notReadyOnce.delete(n); return env(425, { error: 'not_ready' }, 'not_ready'); }
      if (!ready(n)) return env(425, { error: 'not_ready' }, 'not_ready');
      const from = t0 + n * chunk;
      const to = from + chunk;
      const inside = (e) => e.at >= from && e.at < to;
      const events = [
        ...moves.filter(inside).map((m) => ({ k: 'move', ...m })),
        ...fx.filter(inside),
        ...(end && inside(end) ? [end] : []),
      ].sort((a, b) => (a.at - b.at) || (ORDER[a.k] - ORDER[b.k]));
      return env(200, { n, from_ms: from, to_ms: to, events });
    },
    async reactions(id, b) {
      calls.push('reactions:' + b);
      const f = forced('reactions'); if (f) return f;
      if (b >= Math.floor(st.now / 3000)) return env(425, { error: 'not_ready' }, 'not_ready');
      return env(200, { b, r: st.reactions.get(b) || {} });
    },
    async react(id, counts) {
      const f = forced('react');
      posts.push({ route: 'react', counts, refused: !!f });
      return f || env(204, {});
    },
    async postFx(id, body) { posts.push({ route: 'fx', body }); return env(204, {}); },
  };
  return { st, api, play, offerDraw, addFx, addFxPost, finish, clocksAt, chess, moves, t0, delay, chunk };
}
