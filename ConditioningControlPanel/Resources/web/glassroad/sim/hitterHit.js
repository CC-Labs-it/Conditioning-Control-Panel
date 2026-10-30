/* sim/hitterHit.js - feel wave (2026-10-26, lane D): one hitter-hit {racer, hitter, from} for every landed item or
 * trap hit, so fx, audio and the HUD answer every hit the same way.
 *   racer  the victim's racer id
 *   hitter the attacker's racer id, or null (a trap, or a kart that hit itself)
 *   from   the hitter's bearing relative to the victim's heading in radians (0 ahead, + right, +-PI behind), or null
 * Item hits count only when they land (verdict 'hit'); a block, mercy, golden or decoy is not a hit. Trap events
 * carry no hitter. The player-only traps (keep, boudoir) name no racer: they are the player's (racer 0), the same
 * reading boot.js gives them when it turns them into a wall. Presentation only: nothing here touches the sim.
 */
import { S } from './state.js';

/** Trap events that name their racer. */
export const RACER_TRAPS = Object.freeze(['moving-trap-hit', 'goonworks-hit', 'industry-hit', 'scan-hit']);
/** Trap events that only the player's kart raises. */
export const PLAYER_TRAPS = Object.freeze(['keep-gate-hit', 'keep-chain-hit', 'boudoir-press-hit', 'boudoir-sweeper-hit', 'boudoir-mirror-hit']);

/** Bearing of point b from kart state a (radians, 0 = straight ahead, + = right). Local forward is -Z, right +X. */
export function bearingOf(a, bx, bz) {
  const qx = a[S.quatX], qy = a[S.quatY], qz = a[S.quatZ], qw = a[S.quatW];
  // forward = q * (0,0,-1), right = q * (1,0,0); only the ground plane matters
  const fx = -(2 * (qx * qz + qw * qy)), fz = -(1 - 2 * (qx * qx + qy * qy));
  const rx = 1 - 2 * (qy * qy + qz * qz), rz = 2 * (qx * qz - qw * qy);
  const dx = bx - a[S.posX], dz = bz - a[S.posZ];
  if (dx * dx + dz * dz < 1e-9) return null;
  return Math.atan2(dx * rx + dz * rz, dx * fx + dz * fz);
}

/** The hitter-hit payload for one sim event, or null when the event is not a landed hit. racers = list by id. */
export function hitterHitOf(name, p = {}, racers = null) {
  let racer, hitter = null;
  if (name === 'item-hit') {
    if (p.verdict !== 'hit') return null;
    racer = p.victim;
    if (Number.isInteger(p.by) && p.by >= 0 && p.by !== racer) hitter = p.by;
  } else if (name === 'scan-hit') {
    if (p.effect === 'boost') return null;
    racer = p.racer;
  } else if (RACER_TRAPS.includes(name)) racer = p.racer;
  else if (PLAYER_TRAPS.includes(name)) racer = Number.isInteger(p.racer) ? p.racer : 0;
  else return null;
  if (!Number.isInteger(racer)) return null;
  let from = null;
  const byId = (id) => racers ? (racers[id] && racers[id].i === id ? racers[id] : racers.find?.((r) => r && r.i === id)) : null;
  const v = byId(racer), h = hitter != null ? byId(hitter) : null;
  if (v && h && v.st && h.st) from = bearingOf(v.st, h.st[S.posX], h.st[S.posZ]);
  return { racer, hitter, from };
}

/** Listen on the bus and emit hitter-hit; getRacers() -> the racers list (may be null). @returns off() */
export function wireHitterHit(bus, getRacers = () => null) {
  const offs = ['item-hit', ...RACER_TRAPS, ...PLAYER_TRAPS].map((name) => bus.on(name, (p) => {
    const out = hitterHitOf(name, p || {}, getRacers());
    if (out) bus.emit('hitter-hit', out);
  }));
  return () => { for (const off of offs) if (typeof off === 'function') off(); };
}
