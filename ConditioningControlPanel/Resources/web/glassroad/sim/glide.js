/* Authored launch flight. Held hop opens the rotor; finite lift cannot carry into the stone field. */
import { S, RESPAWN } from './state.js';
import { getKeepLayout } from '../track/keep-layout.js';
import { getBoudoirLayout } from '../track/boudoir-layout.js';
import { getGoonworksLayout } from '../track/goonworks-layout.js';
import { aerialAt } from '../track/aerial.js';
const flights = new WeakMap(), bridges = new WeakMap();
export const glideActive = (st) => !!flights.get(st)?.active && st[S.airborne] > 0 &&
  (st[S.respawnPhase] === RESPAWN.NONE || flights.get(st)?.aerial && st[S.respawnPhase] === RESPAWN.GRACE);
export function resetGlide(st) { flights.delete(st); }
/** The forgiving phrase ride owns its rotor after the phrase hop, independent of held inputs. */
export function setPhraseGlide(st, active) {
  if (active) {
    if (!flights.get(st)?.scripted) flights.set(st, { active: true, aerial: true, scripted: true });
  } else if (flights.get(st)?.scripted) flights.delete(st);
}
function bridgeFor(track) {
  if (!['keep', 'boudoir', 'goonworks'].includes(track.id)) return null;
  if (!bridges.has(track)) bridges.set(track, track.id === 'goonworks' ? getGoonworksLayout(track).launch
    : track.id === 'boudoir' ? getBoudoirLayout(track).mirrorJump : getKeepLayout(track).drawbridge);
  return bridges.get(track);
}
/** Only an authored launch can arm a flight, including a manual hop near its lip. */
export function armGlide(st, track, query, velocity = null) {
  const aerial = !query.forkId && aerialAt(track, query.s);
  if (aerial && query.edgeDist >= 0) {
    if (flights.get(st)?.section !== aerial) {
      const from = track.frameAt(aerial.gapS0), to = track.frameAt(aerial.gapS1);
      const grade = (to.p[1] - from.p[1]) / Math.max(1, aerial.gapS1 - aerial.gapS0);
      flights.set(st, { active: false, time: 0, aerial: true, section: aerial, grade, track,
        frame: { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] }, endS: aerial.landingS0 });
      if (velocity && query.s < aerial.gapS0) velocity[1] = Math.max(velocity[1], 14);
    }
    return;
  }
  const b = bridgeFor(track);
  if (!b || query.s < b.rampS1 - 12 || query.s > b.gapS1 || query.edgeDist < 0) return;
  flights.set(st, { active: false, used: false, closed: false, time: 0, endS: b.landingS1 });
}
/** Returns gravity for this tick and applies a terminal descent speed while held. */
export function glideGravity(st, action, query, velocity, gravity, feel, dt) {
  const f = flights.get(st);
  if (!f || dt <= 0) return gravity;
  if ((st[S.respawnPhase] !== RESPAWN.NONE && !(f.aerial && st[S.respawnPhase] === RESPAWN.GRACE)) || !st[S.airborne]) { resetGlide(st); return gravity; }
  f.time += dt;
  if (f.aerial) {
    f.active = !!action.hop && f.time < 12 && query.s < f.endS && query.edgeDist >= 0;
    if (!f.active) return gravity;
    const road = f.track.frameAt(query.s, f.frame), sideLen = Math.hypot(road.b[0], road.b[2]) || 1;
    const bx = road.b[0] / sideLen, bz = road.b[2] / sideLen;
    const lateralV = velocity[0] * bx + velocity[2] * bz;
    const target = Math.max(-6, Math.min(6, -query.lateral * .9 + action.steer * 6));
    const correction = Math.max(-10 * dt, Math.min(10 * dt, target - lateralV));
    velocity[0] += bx * correction; velocity[2] += bz * correction;
    const descent = f.grade * Math.hypot(velocity[0], velocity[2]) - .18;
    const lift = Math.min(3, Math.max(0, 1.8 - query.height) * 2);
    if (velocity[1] > descent + lift) return gravity;
    velocity[1] = Math.min(descent + lift, Math.max(velocity[1], descent) + 8 * dt);
    return 0;
  }
  if (f.time >= feel.get('glide.maxS') || query.s >= f.endS || query.edgeDist < 0 || (f.used && !action.hop)) f.closed = true;
  f.active = !f.closed && !!action.hop;
  if (!f.active) return gravity;
  f.used = true;
  if (velocity[1] > 0) return gravity;
  const g = Math.min(gravity, Math.max(.01, feel.get('glide.gravityMps2')));
  velocity[1] = Math.max(velocity[1], -Math.max(.1, feel.get('glide.fallMps')) + g * dt);
  return g;
}
