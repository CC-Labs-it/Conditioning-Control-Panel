/* sim/ramps.js - the jump ramp launch (handoff-1030/GLASSROAD-RAMPS.md). Three-free, deterministic.
 *
 * The wedge itself is ground: the track query stands a kart on it (track/ramps.js wrapQueryWithRamps), so a kart
 * rides up it like any slope. At the lip a GROUNDED kart moving forward along the course faster than
 * RAMP_LAUNCH_MIN_MPS whose centre crosses the lip within |lat - x| <= widthM / 2 leaves the ground with
 * velY = max(velY, upMps), keeping its speed; sim/kart.js turns that into a 'lip' take-off (so the trick late window
 * and the online LIP pulse work as at any lip) and emits `ramp { id, purpose, upMps, s, x, speed }` (sim/racers.js
 * adds `racer`). Reverse or slow crossings just drive over the wedge and its short back face.
 * A hop pressed on the wedge just before the lip (the trick's early window) is still launched: a kart that left the
 * wedge by a hop within RAMP_HOP_GRACE_S gets the same velY floor when its centre passes the lip.
 */
import { S } from './state.js';
import { toLip, RAMP_LAUNCH_MIN_MPS } from '../track/ramps.js';

/** A hop off the wedge this recently (s) still takes the lip's launch. */
export const RAMP_HOP_GRACE_S = 0.35;
/** Longest arc step (m) a single tick may cover and still count as crossing a lip (guards the lap seam). */
const MAX_STEP_M = 20;

/**
 * The ramp whose lip the kart crossed this tick (qa = the query before the move, qb = after), or null.
 * Only the main road: a fork or route deck never launches. dt > 0.
 */
export function rampCrossed(track, qa, qb, dt) {
  const ramps = track.ramps;
  if (!ramps || !ramps.length || qa.forkId || qb.forkId || !(dt > 0)) return null;
  const L = track.lengthM;
  for (let i = 0; i < ramps.length; i++) {
    const r = ramps[i], d0 = toLip(r, qa.s, L), d1 = toLip(r, qb.s, L), step = d0 - d1;
    if (!(d0 > 0 && d1 <= 0) || step > MAX_STEP_M) continue;
    if (step / dt <= RAMP_LAUNCH_MIN_MPS) continue;              // slow or reverse: drive over it
    const lat = qa.lateral + (qb.lateral - qa.lateral) * (d0 / step);
    if (Math.abs(lat - r.x) <= r.widthM / 2) return r;
  }
  return null;
}

/**
 * A floating item (items[].liftM > 0): is the kart's centre within rad of the box point on every axis of the box's
 * own frame (v = the item view row: p at liftM above the road, t, b, n)? A grounded kart under it never is.
 */
export function liftedNear(st, v, rad) {
  const dx = st[S.posX] - v.p[0], dy = st[S.posY] - v.p[1], dz = st[S.posZ] - v.p[2];
  const along = (a) => Math.abs(dx * a[0] + dy * a[1] + dz * a[2]) <= rad;
  return along(v.t) && along(v.b) && along(v.n);
}

/** Ballistic air time (s) over flat ground for a launch speed: 2 * upMps / g. */
export const rampAirS = (upMps, g = 28) => 2 * upMps / g;
