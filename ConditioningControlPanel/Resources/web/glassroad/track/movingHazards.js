// A single pose supplies both the visible machinery and its swept contact volume.
import { movingStations as threshold } from './moving/threshold.js';
import { movingStations as keep } from './moving/keep.js';
import { movingStations as mainframe } from './moving/mainframe.js';
import { movingStations as boudoir } from './moving/boudoir.js';
import { movingStations as goonworks } from './moving/goonworks.js';
import { hazardBeatAt, beatCycle, machineryRearming } from './machineryBeat.js';
const authors = { threshold, keep, mainframe, boudoir, goonworks };
const clamp = v => Math.max(0, Math.min(1, v));
const ease = v => { v = clamp(v); return v * v * (3 - 2 * v); };
export const MOVING_REVISION = 2;

export function movingPlan(track) {
  const plan = authors[track.id]?.(track) || { traps: [], bridge: null };
  return { ...plan, traps: plan.traps.map(row => {
    const f = track.frameAt(row.s), side = row.side === -1 ? -1 : 1;
    const w = Math.min(row.w || 5, f.w / 2 - 4), travelM = Math.min(row.travelM || f.w * .33, f.w - w - 6);
    return { ...row, side, w, travelM, x: row.x ?? side * (f.w / 2 - w / 2 - .8),
      len: row.len || 4, roadWidth: f.w, p: [...f.p], b: [...f.b], n: [...f.n], t: [...f.t] };
  }).filter(row => row.w >= 2) };
}

export function movingPose(row, timeS, beat) {
  const period = row.periodBeats || 2, offset = row.offsetBeats || 0;
  const safe = machineryRearming(beat, offset, period) || (beat?.beat || 0) < beatCycle(-offset, period);
  const u = beatCycle((beat?.beat || 0) + offset, period) / period;
  // The fast closing stroke ends exactly on a whole beat; the return occupies the offbeat.
  const extension = safe ? 0 : u < .35 ? 0 : u < .5 ? ease((u - .35) / .15)
    : u < .6 ? 1 : u < .8 ? 1 - ease((u - .6) / .2) : 0;
  const x = row.x - row.side * row.travelM * extension;
  return { x, extension, warning: safe || (u >= .15 && u < .5), safe, active: !safe };
}

export function bridgePose(bridge, timeS, index = 0, count = 1) {
  const period = bridge.periodS || 14, open = bridge.openS || 8, transition = bridge.transitionS || .8;
  const t = beatCycle(Math.max(0, timeS), period), stagger = index / Math.max(1, count - 1) * transition * .5;
  const closing = open + stagger, opening = period - transition + stagger;
  const extension = t < closing ? 1 : t < closing + transition ? 1 - ease((t - closing) / transition)
    : t < opening ? 0 : ease((t - opening) / Math.max(.1, period - opening));
  return { extension, supported: extension >= 1 - 1e-6, warning: t > open - 2 && t < open + transition * 1.5,
    remainingS: Math.max(0, open - t) };
}

export function bridgeSlabX(bridge, timeS, index, width) {
  return (index % 2 ? -1 : 1) * (width + 2) *
    (1 - bridgePose(bridge, timeS, index, bridge.slabs).extension);
}

export function installMovingSupport(track, plan) {
  track.moving = { ...plan, revision: MOVING_REVISION, timeS: 0 };
  const bridge = plan.bridge, fork = track.forks?.find(f => f.id === bridge?.forkId);
  if (!fork) return;
  // Align the cut to render samples so the stationary road cannot bridge an open piston gap.
  const step = 4, count = Math.ceil(fork.lengthM / step), sampleM = fork.lengthM / count;
  bridge.fromM = Math.floor(fork.lengthM * bridge.fromU / sampleM) * sampleM;
  bridge.toM = Math.ceil(fork.lengthM * bridge.toU / sampleM) * sampleM;
  bridge.slabs = Math.max(3, Math.ceil((bridge.toM - bridge.fromM) / 5));
  bridge.slabM = (bridge.toM - bridge.fromM) / bridge.slabs;
  fork.movingBridge = bridge;
  const frames = Array.from({ length: bridge.slabs }, (_, i) => fork.frameAt(bridge.fromM + (i + .5) * bridge.slabM));
  const supported = (q, px, py, pz) => {
    if (q?.forkId !== fork.id || q.forkS < bridge.fromM || q.forkS >= bridge.toM) return q;
    q.railed = false;
    if (!q.onRoad) return q; // Shifted machinery outside the driving corridor is not a route.
    const index = Math.min(bridge.slabs - 1, Math.floor((q.forkS - bridge.fromM) / bridge.slabM));
    // Test the actual straight slab, including its small rendered seam overlap.
    for (let i = Math.max(0, index - 1); i <= Math.min(bridge.slabs - 1, index + 1); i++) {
      const f = frames[i], dx = px - f.p[0], dy = py - f.p[1], dz = pz - f.p[2];
      const lateral = dx * f.b[0] + dy * f.b[1] + dz * f.b[2];
      const along = dx * f.t[0] + dy * f.t[1] + dz * f.t[2];
      if (Math.abs(lateral - bridgeSlabX(bridge, track.moving.timeS, i, f.w)) > (f.w + .06) / 2 ||
          Math.abs(along) > (bridge.slabM + .16) / 2) continue;
      q.height = dx * f.n[0] + dy * f.n[1] + dz * f.n[2];
      q.p[0] = px - f.n[0] * q.height; q.p[1] = py - f.n[1] * q.height; q.p[2] = pz - f.n[2] * q.height;
      for (let j = 0; j < 3; j++) q.normal[j] = f.n[j];
      return q;
    }
    q.onRoad = false; q.hole = 'piston-bridge'; q.stone = null;
    return q;
  };
  const original = track.query;
  function query(...args) { return supported(original(...args), ...args); }
  query.below = (...args) => { const q = supported(original.below(...args), ...args); return q?.onRoad ? q : null; };
  track.query = query;
}

export function movingPoseAt(track, row, timeS) { return movingPose(row, timeS, hazardBeatAt(track, timeS)); }
