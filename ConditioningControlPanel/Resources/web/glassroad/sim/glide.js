/* Keep drawbridge flight. Held hop opens the rotor; finite lift cannot carry into the stone field. */
import { S, RESPAWN } from './state.js';
import { getKeepLayout } from '../track/keep-layout.js';
const flights = new WeakMap(), bridges = new WeakMap();
export const glideActive = (st) => !!flights.get(st)?.active && st[S.airborne] > 0 && st[S.respawnPhase] === RESPAWN.NONE;
export function resetGlide(st) { flights.delete(st); }
function bridgeFor(track) {
  if (track.id !== 'keep') return null;
  if (!bridges.has(track)) bridges.set(track, getKeepLayout(track).drawbridge);
  return bridges.get(track);
}
/** Only the raised drawbridge can arm a flight, including a manual hop near its lip. */
export function armGlide(st, track, query) {
  const b = bridgeFor(track);
  if (!b || query.s < b.rampS1 - 12 || query.s > b.gapS1 || query.edgeDist < 0) return;
  flights.set(st, { active: false, used: false, closed: false, time: 0, endS: b.landingS1 });
}
/** Returns gravity for this tick and applies a terminal descent speed while held. */
export function glideGravity(st, action, query, velocity, gravity, feel, dt) {
  const f = flights.get(st);
  if (!f || dt <= 0) return gravity;
  if (st[S.respawnPhase] !== RESPAWN.NONE || !st[S.airborne]) { resetGlide(st); return gravity; }
  f.time += dt;
  if (f.time >= feel.get('glide.maxS') || query.s >= f.endS || query.edgeDist < 0 || (f.used && !action.hop)) f.closed = true;
  f.active = !f.closed && !!action.hop;
  if (!f.active) return gravity;
  f.used = true;
  if (velocity[1] > 0) return gravity;
  const g = Math.min(gravity, Math.max(.01, feel.get('glide.gravityMps2')));
  velocity[1] = Math.max(velocity[1], -Math.max(.1, feel.get('glide.fallMps')) + g * dt);
  return g;
}
