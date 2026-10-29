import { S, RESPAWN } from '../sim/state.js';

/** Judge physical travel on the current road, never mapped lap deltas on stacked roads or forks. */
export function createDirectionWarning(track) {
  const f = { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] };
  let backwardsS = 0, visible = false;
  return {
    update(st, dt, active = true) {
      if (!active || !st[S.grounded] || st[S.respawnPhase] !== RESPAWN.NONE || st[S.stalled] > 0) {
        backwardsS = 0; visible = false; return false;
      }
      if (!(dt > 0)) return visible;
      const t = (track.pathFrameAt ? track.pathFrameAt(st[S.s], st[S.seg], f) : track.frameAt(st[S.s], f)).t;
      const along = st[S.velX] * t[0] + st[S.velY] * t[1] + st[S.velZ] * t[2];
      const speed = Math.hypot(st[S.velX], st[S.velY], st[S.velZ]);
      const backwards = along < -3 && along < -speed * 0.5;
      backwardsS = backwards ? backwardsS + Math.min(dt, 0.1) : 0;
      visible = backwards && (visible || backwardsS >= 0.8);
      return visible;
    },
  };
}
