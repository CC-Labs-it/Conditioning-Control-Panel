/* sim/hop.js - the hop and airborne gravity (plan section 6, "Hop").
 *
 * A hop adds hop.upMps along the road normal. Gravity starts at hop.gravityHopMps2 right after a hop
 * and eases to the rest value hop.gravityMps2 over hop.gravityEaseS; any other airborne moment uses
 * the rest value. Direction is world down, or minus the road normal on a magnet surface (kart.js).
 * Polish 4: a hop can be a trick (sim/trick.js): on a fresh pad, or (when kart.js passes the track)
 * just before a lip, jump or crest ahead. The trick event follows the hop event.
 */
import { S } from './state.js';
import { v3addScaled, v3cross, v3norm, v3dot, lerp, clamp } from '../core/math.js';
import { takeoff } from './air.js';
import { trickOnHop } from './trick.js';

const BANK_SIDE = [0, 0, 0];
/** An uphill sideways hop keeps its road-ramp velocity, with a small bank-climb carry. */
export function limitBankHop(V, normal, tangent, up) {
  v3norm(BANK_SIDE, v3cross(BANK_SIDE, tangent, normal));
  const sideSpeed = v3dot(V, BANK_SIDE), rise = sideSpeed * BANK_SIDE[1];
  if (rise > up * .35) v3addScaled(V, V, BANK_SIDE, sideSpeed * (up * .35 / rise - 1));
}

/** Hop from the ground: V gains up speed along n. Emits hop { speed }, then trick when it is one. */
export function hop(st, feel, events, V, n, speed, track = null) {
  v3addScaled(V, V, n, feel.get('hop.upMps'));
  st[S.hopT] = 0;
  takeoff(st, events, null);
  events.emit('hop', { speed });
  trickOnHop(st, feel, events, speed, track);
}

/** Gravity magnitude this tick, m/s^2. */
export function gravityMps2(st, feel) {
  const k = clamp(st[S.hopT] / feel.get('hop.gravityEaseS'), 0, 1);
  return lerp(feel.get('hop.gravityHopMps2'), feel.get('hop.gravityMps2'), k);
}
