/* sim/hop.js - the hop and airborne gravity (plan section 6, "Hop").
 *
 * A hop adds hop.upMps along the road normal. Gravity starts at hop.gravityHopMps2 right after a hop
 * and eases to the rest value hop.gravityMps2 over hop.gravityEaseS; any other airborne moment uses
 * the rest value. Direction is world down, or minus the road normal on a magnet surface (kart.js).
 */
import { S } from './state.js';
import { v3addScaled, lerp, clamp } from '../core/math.js';
import { takeoff } from './air.js';

/** Hop from the ground: V gains up speed along n. Emits hop { speed }. */
export function hop(st, feel, events, V, n, speed) {
  v3addScaled(V, V, n, feel.get('hop.upMps'));
  st[S.hopT] = 0;
  takeoff(st, events, null);
  events.emit('hop', { speed });
}

/** Gravity magnitude this tick, m/s^2. */
export function gravityMps2(st, feel) {
  const k = clamp(st[S.hopT] / feel.get('hop.gravityEaseS'), 0, 1);
  return lerp(feel.get('hop.gravityHopMps2'), feel.get('hop.gravityMps2'), k);
}
