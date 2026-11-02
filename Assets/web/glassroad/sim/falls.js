/* sim/falls.js - falling onto a lower deck (polish 6, stacked courses). No three.js.
 *
 * A kart in the air off its own road (an open edge, a short gap jump, a push) asks the track for the
 * nearest road beneath it (track.query.below, track/query.js). Road beneath holds off the edge drop
 * (sim/kart.js) and the kart lands there; nothing beneath keeps today's edge drop and rescue.
 * Progress is not judged here: sim/progress.js holds `s` while the kart is in the air and credits a forward
 * move only when it is a registered route (track.routes); a landing earlier in the lap simply loses ground.
 * Events: fall { s, toS } once per flight when road is first found beneath (plus edge-exit { to: 'fall' }
 * when the kart was past an edge), fall-land { s, x, fromS } on touch-down.
 * Per-kart memory lives in a WeakMap on the state array (the state layout is frozen).
 */
import { S } from './state.js';

const NEAR_SEGS = 2;          // road this many segments from the kart's own is its own stretch, never a fall
const MEM = new WeakMap();
const memOf = (st) => { let m = MEM.get(st); if (!m) { m = { on: false, fromS: 0 }; MEM.set(st, m); } return m; };

/** Forget a kart's fall (spawn, rescue). */
export function resetFall(st) { const m = MEM.get(st); if (m) m.on = false; }

/** True while a kart is falling toward a road beneath (for fx and tests). */
export const falling = (st) => { const m = MEM.get(st); return !!(m && m.on); };

/**
 * The road beneath an airborne kart at P, written into out (a query result). Emits fall once per flight.
 * @returns {boolean} true when there is road beneath
 */
export function fallBeneath(track, st, P, out, ev, wasEdge) {
  const below = track.query && track.query.below;
  if (typeof below !== 'function' || !below(P[0], P[1], P[2], out)) return false;
  // the kart's own stretch (a jump's ramp or landing, a crest) is the normal probe's business, not a fall
  const fromFork = track.forkAtSeg?.(st[S.seg]), toFork = track.forkAtSeg?.(out.seg);
  if (fromFork || toFork) {
    if (fromFork === toFork && Math.abs(out.seg - st[S.seg]) <= NEAR_SEGS) return false;
  } else {
    const n = track.nodeCount, d = (((out.seg - (st[S.seg] | 0)) % n) + n) % n;
    if (d <= NEAR_SEGS || d >= n - NEAR_SEGS) return false;
  }
  const m = memOf(st);
  if (!m.on) {
    m.on = true; m.fromS = st[S.s];
    if (ev) {
      ev.emit('fall', { s: st[S.s], toS: out.s });
      if (wasEdge) ev.emit('edge-exit', { to: 'fall' });
    }
  }
  return true;
}

/** Touch-down on the road beneath (q = its query). The latch moves to that road, so a bounce or a rescue belongs to it. */
export function fallLand(track, st, q, ev) {
  const m = memOf(st);
  st[S.latchSeg] = q.seg;
  if (ev) ev.emit('fall-land', { s: q.s, x: q.lateral, fromS: m.on ? m.fromS : st[S.s] });
  m.on = false;
}
