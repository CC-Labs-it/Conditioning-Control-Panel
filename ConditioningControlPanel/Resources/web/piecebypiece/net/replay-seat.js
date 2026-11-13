/* ============================================================================
 * net/replay-seat.js - a seat in the stands for a game that is already over.
 *
 * The same driver shape as net/watch.js, so the switch in net/online.js puts it
 * in the chair and the board, the HUD, the sound and the capture replays are
 * told nothing new. What differs from the stands:
 *
 *   NO NETWORK. The whole game is in the record the host page handed over
 *   (game/cup-replay.js). No tape, no lobby, no heartbeat.
 *
 *   NO TIMERS. The plies are paced off update(dt), the page's own frame loop,
 *   and wait for the men to land and for a capture's panels to finish.
 *   `held()` (the pause card) stops everything.
 *
 *   BOTH SIDES ARE GRADED, like the stands: `seats` is empty, `graded` is both.
 *
 * Whole game: play(), pause(), seek(n), step(+1 | -1). Reel: startReel() jumps
 * between the picked moments and holds the last frame. `onDone` is called once.
 * ==========================================================================*/

import { createRules } from '../game/rules.js';
import { createClock } from '../game/clock.js';
import { buildSteps, endOf, pickMoments, captionOf } from '../game/cup-replay.js';

export const PACE = Object.freeze({ gap: 1.1, first: 0.9, cut: 0.9, hold: 2.2, reducedGap: 1.5 });

export function createReplaySeat({ bus, board, record, held = () => false, reduced = () => false, onDone = () => {}, onChange = () => {} }) {
  const rules = createRules();
  const clock = createClock({ perSideMs: 0 });   // untimed: nothing ticks, the capture replays run in full
  const pieces = board.pieces;
  const { plays, steps, sans } = buildSteps(record);
  const total = plays.length;
  const players = { w: { name: record.white, rating: null }, b: { name: record.black, rating: null } };

  let i = 0;                 // plies on the board
  let over = null;
  let playing = false;
  let wait = 0;
  let done = false;
  let disposed = false;
  let reel = null;           // { moments, at, phase: 'play' | 'hold' }
  let caption = null;

  const settling = () => !!(board.anim?.busy?.() || board.director?.holding?.() || board.director?.active?.());
  const changed = () => { try { onChange(state()); } catch { /* the bar is optional */ } };
  const state = () => ({ i, total, playing, over, san: i ? sans[i - 1] : '', caption, reel: !!reel });

  function deal() {
    bus.emit('local', { sides: [], graded: ['w', 'b'], mode: 'replay', players: { w: players.w, b: players.b } });
    bus.emit('players', { white: players.w, black: players.b, me: null, opponent: null, watching: true });
  }

  function finish() {
    if (over || i < total) return;
    over = endOf(record, rules.result());
    if (!reel) playing = false;   // a reel still owes its last hold
    bus.emit('gameover', { result: over.result, winner: over.winner });
  }

  function fireDone() {
    if (done) return;
    done = true;
    try { onDone(record.match_id); } catch { /* the host's business */ }
  }

  /** One ply forward, the way net/watch.js plays one. */
  function playNext() {
    const p = plays[i];
    const played = p ? rules.move(p.from, p.to, p.promotion || 'q') : null;
    if (!played) return false;
    i += 1;
    if (played.capturedSquare && played.capturedSquare !== played.to) pieces.remove(played.capturedSquare);
    pieces.move(played.from, played.to);
    const hop = rules.rookHop(played);
    if (hop) pieces.move(hop.from, hop.to);
    if (played.promotion) pieces.setPosition(rules.position());
    if (played.captured) bus.emit('capture', { by: played.side, piece: played.captured, square: played.capturedSquare, victimSide: played.capturedSide });
    if (played.check) { board.buzzCheck?.(played.to); bus.emit('check', { side: played.turn }); }
    if (i >= total) finish();
    else bus.emit('turn', { side: played.turn, ply: i, clocks: clock.snapshot(), total: 0 });
    changed();
    return true;
  }

  /** Jump to the position after `n` plies: no animation, nothing replayed. */
  function seek(n) {
    const next = Math.max(0, Math.min(total, Math.trunc(Number(n) || 0)));
    board.anim?.skip?.();
    board.director?.drop?.();
    if (record.start_fen) { try { rules.chess.load(record.start_fen); } catch { rules.chess.reset(); } } else rules.chess.reset();
    for (let k = 0; k < next; k++) rules.move(plays[k].from, plays[k].to, plays[k].promotion || 'q');
    i = next;
    pieces.setPosition(rules.position());
    if (over) { over = null; deal(); }   // back from the end: the HUD is dealt again
    bus.emit('resync', { seq: i, ply: i, fen: rules.fen(), turn: rules.turn() });
    if (i >= total) finish();
    else bus.emit('turn', { side: rules.turn(), ply: i, clocks: clock.snapshot(), total: 0 });
    changed();
  }

  function play() {
    if (reel || disposed) return;
    if (i >= total) seek(0);
    playing = true; wait = PACE.first;
    changed();
  }
  function pause() { if (!reel && playing) { playing = false; changed(); } }

  function startReel(options) {
    const moments = pickMoments(steps, options);
    if (!moments.length) { fireDone(); return; }
    reel = { moments, at: -1, phase: 'hold' };
    playing = true; wait = 0;
  }

  function reelTick() {
    const m = reel.moments[reel.at];
    if (reel.phase === 'play' && i < m.to) { playNext(); wait = reduced() ? PACE.reducedGap : PACE.gap; return; }
    if (reel.phase === 'play') { reel.phase = 'hold'; wait = PACE.hold; return; }
    if (reel.at + 1 >= reel.moments.length) { playing = false; fireDone(); changed(); return; }
    reel.at += 1; reel.phase = 'play';
    const next = reel.moments[reel.at];
    caption = captionOf(record, next.ply);
    seek(next.from);
    wait = PACE.cut;
  }

  function update(dt) {
    if (disposed || !playing || held()) return;
    if (settling()) { wait = Math.max(wait, 0.25); return; }
    wait -= dt;
    if (wait > 0) return;
    if (reel) { reelTick(); return; }
    if (!playNext()) { playing = false; changed(); return; }
    wait = reduced() ? PACE.reducedGap : PACE.gap;
    if (i >= total) fireDone();
  }

  function start() {
    pieces.setPosition(rules.position());
    try { board.setSide('w', true); } catch { /* no rig */ }
    changed();   // nothing on the bus yet: the board's opening rise plays until the first move
  }

  return {
    get rules() { return rules; },
    clock,
    start,
    update,
    tryMove: () => null,
    takeBack: () => null,
    canPick: () => false,
    legalTargets: () => [],
    resign() {},
    record: () => ({ moves: sans.slice(0, i), plies: i, result: over, clocks: null, me: null }),
    plies: () => i,
    turn: () => rules.turn(),
    isOver: () => !!over,
    result: () => over,
    autoRemaining: () => 0,
    get seats() { return []; },
    get graded() { return ['w', 'b']; },
    isWatch: true,
    isReplay: true,
    watchers: () => 0,
    players: () => ({ w: players.w, b: players.b }),
    dispose() { disposed = true; playing = false; },

    deal, play, pause, seek, startReel, state,
    step(d) { pause(); if (d > 0 && i < total) { board.anim?.skip?.(); playNext(); if (i >= total) fireDone(); } else if (d < 0) seek(i - 1); },
    toggle() { if (playing) pause(); else play(); },
  };
}
