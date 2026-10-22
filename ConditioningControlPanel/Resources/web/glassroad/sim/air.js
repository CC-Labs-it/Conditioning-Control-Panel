/* sim/air.js - take-off and landing tiers (plan section 6, "Air and landing").
 *
 * Tier by airtime: under air.lightS 0, then 1 light, 2 medium, 3 heavy. A tier with a bounce height
 * (air.bounceM) kicks the kart back up along the normal, which is a silent take-off (no airborne
 * event, the land event already carries the tier). Landing with hop held keeps the drift and its
 * charge (air.keepDriftOnLand); landing from a hop with hop held and the stick off centre starts one.
 * Polish 4: a take-off tells the trick clock (sim/trick.js; a 'lip' take-off opens the late window) and
 * a touch-down pays an armed trick after the land event (trick-land).
 */
import { S } from './state.js';
import { v3addScaled } from '../core/math.js';
import { cancelDrift, maybeStartDrift } from './drift.js';
import { noteTakeoff, trickOnLand } from './trick.js';

export function landTier(airTime, feel) {
  if (airTime < feel.get('air.lightS')) return 0;
  if (airTime < feel.get('air.mediumS')) return 1;
  if (airTime < feel.get('air.heavyS')) return 2;
  return 3;
}

/** Leave the ground. cause 'lip' | 'edge' emits airborne; null is silent (hop, bounce). */
export function takeoff(st, events, cause) {
  st[S.airborne] = 1; st[S.grounded] = 0; st[S.airTime] = 0;
  noteTakeoff(st, cause);
  if (cause) events.emit('airborne', { cause });
}

/** Touch down (the caller has already snapped P and removed the normal speed). @returns {number} tier */
export function land(st, a, feel, events, V, n, speed, dt) {
  const airTime = st[S.airTime], tier = landTier(airTime, feel);
  st[S.airborne] = 0; st[S.grounded] = 1;
  if (st[S.driftDir]) {
    if (!(feel.get('air.keepDriftOnLand') && a.hop)) cancelDrift(st, 'slip', events);
  } else if (a.hop && st[S.hopT] <= airTime + 1.5 * dt) {
    maybeStartDrift(st, a, feel, speed, events);    // this landing ends a hop: hop held + stick = drift
  }
  events.emit('land', { tier, airTime });
  trickOnLand(st, feel, events, airTime);
  const b = tier > 0 ? feel.get('air.bounceM')[tier - 1] : 0;
  if (b > 0) {
    v3addScaled(V, V, n, Math.sqrt(2 * feel.get('hop.gravityMps2') * b));
    takeoff(st, events, null);
  }
  return tier;
}
