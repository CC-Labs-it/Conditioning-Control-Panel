// Player-selected views and one brief mirror dolly. Music energy never drives this camera.
import { S, RESPAWN } from '../sim/state.js';
import { clamp, smoothstep } from '../core/math.js';

export const MIRROR_SHOT = Object.freeze({ inS: .35, holdS: .45, outS: .65, pull: .32 });
export const mirrorAhead = (s, from, length) => {
  const d = ((s - from) % length + length) % length;
  return d > length / 2 ? d - length : d;
};
export const mirrorOpening = (row, s, length, songS = NaN) => {
  const near = clamp((24 - mirrorAhead(row.s, s, length)) / 12, 0, 1);
  return row.params?.cameraCloseUp === true && Number.isFinite(songS)
    ? near * smoothstep(row.startS - .35, row.startS + .05, songS) : near;
};

export function createCameraViews() {
  const pose = { eye: [0, 0, 0], look: [0, 0, -1], up: [0, 1, 0], vFov: 60 };
  const seen = new Set();
  let selected = 'chase', elapsed = Infinity, epoch = null, lastSongS = NaN, first = true;
  function reset() { seen.clear(); elapsed = Infinity; epoch = null; lastSongS = NaN; first = true; }
  function toggle() { selected = selected === 'chase' ? 'first' : 'chase'; elapsed = Infinity; return selected; }
  function update(rig, kart, dt, { active = true, rearView = false, manual = false, gentle = false,
    cues = [], revision = null, songS = NaN, track = null } = {}) {
    if (!first && !(dt > 0)) return pose;
    first = false;
    if (epoch !== revision || (Number.isFinite(songS) && songS < lastSongS - .1)) {
      seen.clear(); elapsed = Infinity; epoch = revision;
    }
    lastSongS = songS;
    const rescue = kart[S.respawnPhase] !== RESPAWN.NONE && kart[S.respawnPhase] !== RESPAWN.GRACE;
    const fork = track?.forkAtSeg?.(kart[S.seg]);
    const blocked = !active || gentle || rearView || manual || rescue || kart[S.airborne] > .5 || !!fork;
    if (blocked) elapsed = Infinity;
    if (Number.isFinite(songS) && track && !fork) {
      for (const cue of cues) {
        if (cue.kind !== 'PROP' || cue.params?.variant !== 'mirror' || cue.params.cameraCloseUp !== true || !cue.locked || !cue.frame || cue.state === 'miss') continue;
        const ahead = mirrorAhead(cue.s, kart[S.s], track.lengthM);
        const key = cue.id + ':' + cue.startS;
        if (seen.has(key) || ahead > 24 || ahead < 0 || !(mirrorOpening(cue, kart[S.s], track.lengthM, songS) > 0)) continue;
        // Nearby lap distance alone is insufficient on stacked roads and shortcut branches.
        const p = cue.frame.p, n = cue.frame.n;
        const delta = [kart[S.posX] - p[0], kart[S.posY] - p[1], kart[S.posZ] - p[2]];
        if (Math.hypot(...delta) > 40 || Math.abs(delta.reduce((v, x, i) => v + x * n[i], 0)) > 5) continue;
        seen.add(key);
        if (seen.size > 128) seen.delete(seen.values().next().value);
        if (!blocked) elapsed = 0;
      }
    }
    let shot = 0;
    if (Number.isFinite(elapsed)) {
      elapsed += Math.max(0, Math.min(.1, dt));
      const { inS, holdS, outS } = MIRROR_SHOT;
      shot = smoothstep(0, inS, elapsed) * (1 - smoothstep(inS + holdS, inS + holdS + outS, elapsed));
      if (elapsed >= inS + holdS + outS) elapsed = Infinity;
    }
    for (const k of ['eye', 'look', 'up']) for (let i = 0; i < 3; i++) pose[k][i] = rig[k][i];
    pose.vFov = rig.vFov;
    const f = rig.rf, u = rig.ru;
    if (active && selected === 'first' && !rearView) {
      // Mounted ahead of the driver's face: retain the kart in mirrors and its real shadow.
      for (let i = 0; i < 3; i++) {
        pose.eye[i] = kart[S.posX + i] + u[i] * .95 + f[i] * .75;
        pose.look[i] = pose.eye[i] + f[i] * 20;
        pose.up[i] = u[i];
      }
      rig.glance.apply(pose.eye, pose.look, pose.up);
    }
    if (active && rearView) {
      const distance = Math.hypot(...rig.eye.map((v, i) => v - rig.anchor[i]));
      for (let i = 0; i < 3; i++) {
        pose.eye[i] = rig.anchor[i] + f[i] * distance + u[i] * .6;
        pose.look[i] = rig.anchor[i]; pose.up[i] = u[i];
      }
    } else if (shot > 0) {
      for (let i = 0; i < 3; i++) {
        const closeEye = rig.eye[i] + (rig.anchor[i] - rig.eye[i]) * MIRROR_SHOT.pull;
        pose.eye[i] += (closeEye - pose.eye[i]) * shot;
        if (selected === 'first') pose.eye[i] += u[i] * .8 * Math.sin(Math.PI * shot);
        pose.look[i] += (rig.look[i] - pose.look[i]) * shot;
        pose.up[i] += (rig.up[i] - pose.up[i]) * shot;
      }
      pose.vFov -= 3 * shot;
    }
    return pose;
  }
  return { update, reset, toggle, get selected() { return selected; }, get mirrorActive() { return Number.isFinite(elapsed); } };
}
