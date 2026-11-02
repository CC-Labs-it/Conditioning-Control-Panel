// Trap telegraph (owner, 2026-10-27: "traps are pretty hard, telegraph when a trap is about to open or close").
// Every piston says what it is about to do through its inlays and floor lamp: clear = the course accent,
// warning = amber lit once per warning beat (steady under Gentle), the last beat before the strike = solid amber,
// closed = hot, opening = green, then clear again. The nearest trap ahead also ticks once per warning beat
// (a rising ladder, quantised to the heard beat by construction: the warning beats are the song's beats).
import { movingPoseAt } from '../track/movingHazards.js';

export const TELEGRAPH = Object.freeze({ calm: null, dim: null, warn: '#ffd45c', hot: '#ff3d7a', open: '#62db94' });
export const TICK_RANGE_M = 48;

/** The colour key for a pose from track/trapArrangement.js trapStroke (or the online fallback pose). */
export function telegraphKey(pose, gentle = false) {
  if (!pose || pose.safe) return 'calm';
  if (pose.opening || pose.phase === 'rise') return 'open';
  if (pose.extension > 0 || pose.phase === 'lower' || pose.phase === 'hold') return 'hot';
  if (Number.isFinite(pose.countdown)) {
    if (pose.countdown <= 1 || gentle) return 'warn';
    const into = Math.ceil(pose.countdown) - pose.countdown;   // 0 on each warning beat
    return into < .45 ? 'warn' : 'dim';
  }
  return pose.warning ? 'warn' : 'calm';
}

/** One tick per warning beat from the nearest piston within range. Seeks, menus and paused frames stay quiet. */
export function createTrapTicks(track, emit, rows = track.moving?.traps || []) {
  const seen = new Map();
  let lastTime = null;
  return {
    reset() { seen.clear(); lastTime = null; },
    update(timeS, position, { active = true, paused = false, gentle = false } = {}) {
      if (paused) return;
      if (!active || !Number.isFinite(timeS)) { seen.clear(); lastTime = null; return; }
      const continuous = lastTime !== null && timeS >= lastTime && timeS - lastTime < .3;
      let closest = null, closestDistance = TICK_RANGE_M, step = 0;
      for (const row of rows) {
        const pose = movingPoseAt(track, row, timeS);
        const beat = Number.isFinite(pose?.countdown) ? Math.max(1, Math.ceil(pose.countdown - 1e-6)) : null;
        const previous = seen.get(row.id);
        seen.set(row.id, beat);
        if (!continuous || beat === null || beat === previous) continue;
        const distance = Math.hypot(...row.p.map((v, i) => v + row.b[i] * row.x - position[i]));
        if (distance < closestDistance) { closest = row; closestDistance = distance; step = beat; }
      }
      lastTime = timeS;
      if (closest) emit?.('trap-tick', { id: closest.id, step, gain: (1 - closestDistance / TICK_RANGE_M) ** 2, gentle });
    },
  };
}

/** Sample gain for a tick payload: quiet, by distance, quieter under Gentle. */
export function trapTickGain(payload = {}) {
  const gain = Number.isFinite(payload.gain) ? Math.max(0, Math.min(1, payload.gain)) : 0;
  return gain * (payload.gentle ? .6 : 1);
}
