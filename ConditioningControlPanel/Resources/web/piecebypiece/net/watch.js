/* ============================================================================
 * net/watch.js - a seat in the stands: one public online game, watched.
 *
 * THE SAME SHAPE AS net/match.js and game/hotseat.js (rules, clock, start,
 * update, tryMove, takeBack, canPick, legalTargets, resign, record, plies, turn,
 * isOver, result), so the driver switch in net/online.js can put it in the chair
 * and nothing downstream - the board, the camera, the HUD, the sound - is told
 * it changed. What differs, and why:
 *
 *   NOBODY HERE MOVES. canPick is always false and tryMove always refused, so
 *   drag.js never lifts a man. No resign, no draw, no claim_timeout and no seat
 *   heartbeat: a spectator is not a seat and the server never counts him as one.
 *
 *   LATE, ON PURPOSE. Spectate v2 (2026-10-14): the game arrives as a TAPE
 *   (net/tape.js), immutable 2 s chunks of everything older than the server's
 *   delay (12 s), played back on a delayed timeline. This file only applies
 *   what the tape hands it: a snapshot at the join (and at every resync), then
 *   each move at the moment it was played, the end, and the turn player's
 *   Distraction (fx-heat / fx-fire / fx-soon on the bus, which ramp/ replays
 *   with the spectator's OWN pictures; the player's media never travels).
 *
 *   BOTH SIDES ARE GRADED. `seats` is empty (nobody here owns a colour) and
 *   `graded` is both, which game/iq.js reads off the `local` event: the stands
 *   see both players' IQ, worked out on this page from the move list. No IQ
 *   ever travels over the wire, and a player never sees the opponent's.
 *
 *   THE STANDS TALK BACK, IN PRESETS. `watch-react` {id} from the HUD goes out
 *   batched (net/tape.js), the crowd's counts come back as `watch-reactions`.
 *   `watch-seat` {seat} turns the camera to that side. `watch-info` carries the
 *   delay, the watcher count, both players and the seat, on the join and on
 *   every change. Watchers stay nameless: counts only.
 *
 *   A GAME THAT CLOSES SAYS SO. A refusal the server words (the game stopped
 *   being watchable, it is gone, or the route is off) emits `watch-closed`;
 *   the door takes the player back to the tables.
 * ==========================================================================*/

import { createRules } from '../game/rules.js';
import { formatClock } from '../game/clock.js';
import { parseUci, TICK_MS } from './match.js';
import { createTape, WATCH_DELAY_MS } from './tape.js';
import * as defaultApi from './api.js';

export { WATCH_DELAY_MS };

const SIDES = ['w', 'b'];
const other = (side) => (side === 'w' ? 'b' : 'w');
const sideWord = (s) => (s === 'w' ? 'white' : 'black');

/** A seat's player off the wire -> { name, rating }. */
export function playerOf(p) {
  if (!p) return null;
  if (typeof p === 'string') return { name: p, rating: null };
  const r = Number(p.rating);
  return { name: String(p.display_name || p.name || 'someone'), rating: (p.rating === null || p.rating === undefined || !Number.isFinite(r)) ? null : Math.round(r) };
}

/** The end of a watched game in plain words: no "you" in it, the stands won nothing. */
export function watchLine(end) {
  if (!end) return '';
  const who = end.winner ? sideWord(end.winner) : '';
  const loser = end.winner ? sideWord(other(end.winner)) : '';
  if (end.result === 'checkmate') return 'checkmate, ' + who + ' wins';
  if (end.result === 'flag') return loser + "'s flag fell, " + who + ' wins';
  if (end.result === 'resign') return loser + ' resigned, ' + who + ' wins';
  if (end.result === 'abandon') return loser + ' left, ' + who + ' wins';
  if (end.result === 'stalemate') return 'stalemate, a draw';
  return 'a draw by ' + String(end.reason || 'agreement').replace(/_/g, ' ');
}

/**
 * @param {object} o
 * @param {object} o.bus, o.board  the page's bus and board, as net/match.js takes them
 * @param {object} [o.hud]         {w, b, status} elements
 * @param {string} o.matchId
 * @param {object} [o.initial]     a GET watch answer we already have (the door's)
 * @param {object} [o.api]         net/api.js, swappable for tests
 * @param {() => number} [o.now]   monotonic clock, for the tape's timeline
 * @param {boolean} [o.autoStart=true] run the tape on its own timers (tests drive _tick by hand)
 * @param {() => number} [o.random] the jitter draw (tests pin it)
 */
export function createWatchSession({
  bus, board, hud = null, matchId, initial = null,
  api = defaultApi, now = null, autoStart = true, random = undefined,
}) {
  if (!matchId) throw new Error('createWatchSession: matchId is required');
  const rules = createRules();
  const pieces = board.pieces;

  let over = null;
  let closed = null;            // the reason the stands were shut, once they are
  let disposed = false;
  let ticker = null;
  let watchers = 0;
  let players = { w: null, b: null };
  let lastLooks = '';            // the last looks frame sent, so a steady head stays quiet
  let seat = 'w';               // which side the camera watches from
  let sided = false;            // the camera is set once; after that the swap is the watcher's own
  let adopted = false;          // watch-info waits for the tape's first head, so behindMs is right from the first one
  const unbind = [];

  const tape = createTape({
    matchId, api, now, timers: autoStart, random,
    on: {
      state: applyState,
      move: (it) => { const m = parseUci(it.uci); return !!(m && playMove(m.from, m.to, m.promotion)); },
      end: (end) => finish(end),
      heat: (p) => bus.emit('fx-heat', p),
      fire: (p) => bus.emit('fx-fire', p),
      soon: (p) => bus.emit('fx-soon', p),
      head: noteHead,
      closed: (why) => close(why),
      reactions: (counts) => bus.emit('watch-reactions', { counts }),
      net: (p) => bus.emit('net', p),
    },
  });
  const clock = tape.clock;

  /* ---------------------------------------------------------------- paint */

  function clocks() { const s = clock.snapshot(); return { w: s.w, b: s.b, total: s.total }; }

  function paint() {
    if (!hud) return;
    const s = clock.snapshot();
    if (hud.w) { hud.w.textContent = formatClock(s.w); hud.w.classList?.toggle('on', s.active === 'w' && !over); }
    if (hud.b) { hud.b.textContent = formatClock(s.b); hud.b.classList?.toggle('on', s.active === 'b' && !over); }
    if (!hud.status || hud.status.dataset?.hudOwned) return;
    if (over) hud.status.textContent = watchLine(over);
    else hud.status.textContent = rules.inCheck() ? 'check' : sideWord(rules.turn()) + ' to move';
  }

  function tick() {
    if (over || disposed) return;
    bus.emit('clock', clock.snapshot());
    paint();
  }

  function stopTicker() { if (ticker) { clearInterval(ticker); ticker = null; } }

  function finish(end) {
    if (over || !end) return;
    over = end;
    clock.stop();
    stopTicker();
    paint();
    bus.emit('gameover', { result: end.result, winner: end.winner === undefined ? null : end.winner });
  }

  function close(reason) {
    if (closed || disposed) return;
    closed = reason || 'closed';
    stopTicker();
    bus.emit('watch-closed', { matchId, reason: closed });
  }

  /* ------------------------------------------------------------- the info */

  function info() {
    return { delayMs: tape.delayMs(), behindMs: tape.lagMs(), watchers, players: { w: players.w, b: players.b }, seat };
  }

  function noteHead(data) {
    let changed = !adopted;
    adopted = true;
    const n = Math.max(0, Math.trunc(Number(data && data.watchers) || 0));
    if (n !== watchers) { watchers = n; changed = true; bus.emit('watchers', { count: n }); }
    // Both seats' looks ride the head too (contract 4): the stands see what the players wear.
    if (data && data.looks && typeof data.looks === 'object') {
      const l = { white: data.looks.white || null, black: data.looks.black || null };
      if (JSON.stringify(l) !== lastLooks) { lastLooks = JSON.stringify(l); bus.emit('looks', l); }
    }
    const p = (data && data.players && typeof data.players === 'object') ? data.players : {};
    const next = { w: playerOf(p.w), b: playerOf(p.b) };
    if (JSON.stringify(next) !== JSON.stringify(players)) {
      players = next;
      changed = true;
      bus.emit('players', { white: next.w, black: next.b, me: null, opponent: null, watching: true });
    }
    if (changed) bus.emit('watch-info', info());
  }

  function setSeat(next, instant) {
    if (next !== 'w' && next !== 'b') return;
    const moved = next !== seat || !sided;
    seat = next;
    sided = true;
    if (moved) { try { board.setSide(seat, !!instant); } catch { /* no rig */ } }
    if (adopted) bus.emit('watch-info', info());
  }

  /* ------------------------------------------------------------- the men */

  /** A ply onto the board, the way net/match.js plays the opponent's. Null when it is not legal here. */
  function playMove(from, to, promotion) {
    const played = rules.move(from, to, promotion || 'q');
    if (!played) return null;
    if (played.capturedSquare && played.capturedSquare !== to) pieces.remove(played.capturedSquare);
    pieces.move(from, to);
    const hop = rules.rookHop(played);
    if (hop) pieces.move(hop.from, hop.to);
    if (played.promotion) pieces.setPosition(rules.position());
    if (played.captured) {
      bus.emit('capture', { by: played.side, piece: played.captured, square: played.capturedSquare, victimSide: played.capturedSide });
    }
    if (played.check) {
      if (board.buzzCheck) board.buzzCheck(played.to);
      bus.emit('check', { side: played.turn });
    }
    // a mate or a stalemate ends it on the board as it lands; the tape's end item
    // (with the server's reason) then finds the game already over
    const end = rules.result();
    if (end) finish(end);
    else bus.emit('turn', { side: played.turn, ply: rules.ply(), clocks: clocks(), total: clock.snapshot().total });
    paint();
    return played;
  }

  /** The tape's snapshot: rebuild the board from the move list. False when it will not replay. */
  function applyState(st) {
    if (disposed) return false;
    let rebuilt = true;
    try {
      rules.chess.reset();
      for (const uci of st.moves || []) {
        const m = parseUci(uci);
        if (!m || !rules.move(m.from, m.to, m.promotion || 'q')) { rebuilt = false; break; }
      }
    } catch { rebuilt = false; }
    pieces.setPosition(rules.position());
    if (!sided) setSeat(seat, true);
    if (!rebuilt) return false;
    if (st.end) { paint(); return true; }   // the tape calls finish() next
    bus.emit('resync', { seq: st.seq, ply: rules.ply(), fen: rules.fen(), turn: rules.turn() });
    bus.emit('turn', { side: rules.turn(), ply: rules.ply(), clocks: clocks(), total: clock.snapshot().total });
    if (st.heat) bus.emit('fx-heat', st.heat);
    paint();
    return true;
  }

  /* ------------------------------------------------------------ lifecycle */

  function record() {
    let moves = [];
    try { moves = rules.chess.history(); } catch { moves = []; }
    const s = clock.snapshot();
    return {
      moves, plies: moves.length,
      result: over ? { result: over.result, winner: over.winner === undefined ? null : over.winner, reason: over.reason || null } : null,
      clocks: { w: s.w, b: s.b, total: s.total }, me: null,
    };
  }

  function start() {
    pieces.setPosition(rules.position());
    setSeat(seat, true);
    paint();
    unbind.push(bus.on('watch-react', (p) => { if (p && typeof p.id === 'string') tape.react(p.id); }));
    unbind.push(bus.on('watch-seat', (p) => setSeat(p && p.seat, false)));
    if (!ticker && !disposed && !over && autoStart) { ticker = setInterval(tick, TICK_MS); if (ticker.unref) ticker.unref(); }
    return tape.start(initial);
  }

  function dispose() {
    if (disposed) return;
    disposed = true;
    stopTicker();
    tape.dispose();
    for (const off of unbind.splice(0)) { try { off(); } catch { /* gone */ } }
  }

  return {
    rules,
    clock,
    start,
    update() {},
    tryMove: () => null,
    takeBack: () => null,
    canPick: () => false,
    legalTargets: () => [],
    resign() {},
    record,
    plies: () => rules.ply(),
    turn: () => rules.turn(),
    isOver: () => !!over,
    result: () => over,
    autoRemaining: () => 0,

    /** Nobody here owns a colour. */
    get seats() { return []; },
    /** Both sides are graded for the stands (game/iq.js reads `graded` off the local event). */
    get graded() { return SIDES.slice(); },
    isWatch: true,
    matchId,
    watchers: () => watchers,
    /** The server's delay (12 s by default). */
    delayMs: () => tape.delayMs(),
    /** How far behind the live board the stands really play: delay + one chunk + the fetch lead. */
    behindMs: () => tape.lagMs(),
    players: () => ({ w: players.w, b: players.b }),
    seat: () => seat,
    info,
    /** A preset reaction from the stands (same as emitting watch-react). */
    react: (id) => tape.react(id),
    closed: () => closed,
    seq: () => rules.ply(),
    status: () => (over ? 'over' : closed ? 'closed' : 'live'),
    dispose,

    _tape: tape,
    _tick: () => { tape.tick(); tick(); },
  };
}
