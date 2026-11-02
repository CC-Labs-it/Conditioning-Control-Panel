// A single pose supplies both the visible machinery and its swept contact volume.
import { movingStations as threshold } from './moving/threshold.js';
import { movingStations as keep } from './moving/keep.js';
import { movingStations as mainframe } from './moving/mainframe.js';
import { movingStations as boudoir } from './moving/boudoir.js';
import { movingStations as goonworks } from './moving/goonworks.js';
import { hazardBeatAt, beatCycle, machineryRearming } from './machineryBeat.js';
import { trapStroke } from './trapArrangement.js';
import { pistonPocket, pistonPocketPose, pistonPocketContains } from './pistonPocket.js';
const authors = { threshold, keep, mainframe, boudoir, goonworks };
const clamp = v => Math.max(0, Math.min(1, v));
const ease = v => { v = clamp(v); return v * v * (3 - 2 * v); };
export const MOVING_REVISION = 4;
const geometryNumber = value => Number.isFinite(value) && !Number.isInteger(value) ? Math.round(value * 1e9) / 1e9 : value;
function canonicalGeometry(value) {
  if (typeof value === 'number') return geometryNumber(value);
  if (Array.isArray(value)) return value.map(canonicalGeometry);
  if (value && typeof value === 'object') return Object.fromEntries(Object.entries(value).map(([key,v]) => [key,canonicalGeometry(v)]));
  return value;
}

export function movingPlan(track) {
  const plan = authors[track.id]?.(track) || { traps: [], bridge: null };
  // Physics and room identity share canonical geometry, including engine-dependent frame bases.
  return canonicalGeometry({ ...plan, traps: plan.traps.map(row => {
    const f = track.frameAt(row.s), side = row.side === -1 ? -1 : 1;
    // Long visible heads and swept contact use the same footprint. Retain the outer bypass.
    const w = Math.min((row.w || 5) * 1.6, f.w / 2 - 4), travelM = Math.min(row.travelM || f.w * .33, f.w - w - 6);
    const result = { ...row, side, w, travelM, pocketClearM: track.id === 'threshold' ? 5.15 : 3,
      x: row.x ?? side * (f.w / 2 - w / 2 - .8),
      len: (row.len || 4) * 1.4, roadWidth: f.w, p: [...f.p], b: [...f.b], n: [...f.n], t: [...f.t] };
    return { ...result, pocket: pistonPocket(result) };
  }).filter(row => row.w >= 2) });
}

export function movingPose(row, timeS, beat) {
  const arranged = trapStroke(row, beat);
  const finish = pose => {
    if (!row.pocket) return pose;
    const pocket = pistonPocketPose(row, pose.extension);
    return { ...pose, ...pocket, x: pocket.headX };
  };
  if (arranged) return finish({ ...arranged, x: row.x - row.side * row.travelM * arranged.extension, active: !arranged.safe });
  const period = row.periodBeats || 2, offset = row.offsetBeats || 0;
  const safe = machineryRearming(beat, offset, period) || (beat?.beat || 0) < beatCycle(-offset, period);
  const u = beatCycle((beat?.beat || 0) + offset, period) / period;
  // The fast closing stroke ends exactly on a whole beat; the return occupies the offbeat.
  const extension = safe ? 0 : u < .35 ? 0 : u < .5 ? ease((u - .35) / .15)
    : u < .6 ? 1 : u < .8 ? 1 - ease((u - .6) / .2) : 0;
  const x = row.x - row.side * row.travelM * extension;
  return finish({ x, extension, warning: safe || (u >= .15 && u < .5), safe, active: !safe });
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
  installPistonSupport(track, plan.traps);
  const bridge = plan.bridge, fork = track.forks?.find(f => f.id === bridge?.forkId);
  if (!fork) return;
  // Align the cut to render samples so the stationary road cannot bridge an open piston gap.
  const step = 4, count = Math.ceil(fork.lengthM / step), sampleM = fork.lengthM / count;
  bridge.fromM = geometryNumber(Math.floor(fork.lengthM * bridge.fromU / sampleM) * sampleM);
  bridge.toM = geometryNumber(Math.ceil(fork.lengthM * bridge.toU / sampleM) * sampleM);
  bridge.slabs = Math.max(3, Math.ceil((bridge.toM - bridge.fromM) / 5));
  bridge.slabM = geometryNumber((bridge.toM - bridge.fromM) / bridge.slabs);
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

function installPistonSupport(track, rows) {
  const original = track.query;
  function supported(q, px, py, pz) {
    if (!q) return q;
    delete q.piston;
    if (!q.onRoad || q.forkId) return q;
    for (const row of rows) {
      const pocket = row.pocket;
      if (!pocket || !pistonPocketContains(pocket, q.s, q.lateral, track.lengthM)) continue;
      q.railed = false;
      const pose = movingPoseAt(track, row, track.moving.timeS);
      // The translated cover carries its source road profile, including a curved canal bank.
      if (q.lateral >= pocket.minX + pose.panelShift && q.lateral <= pocket.maxX + pose.panelShift) {
        if ((pose.panelShift || pose.panelY) && track.surfaceAt) {
          const source = track.surfaceAt(q.s, q.lateral - pose.panelShift);
          const position = [px, py, pz];
          q.height = position.reduce((sum, v, j) => sum + (v - source.p[j] - row.b[j] * pose.panelShift - row.n[j] * pose.panelY) * source.n[j], 0);
          for (let j = 0; j < 3; j++) { q.p[j] = position[j] - source.n[j] * q.height; q.normal[j] = source.n[j]; }
        } else if (pose.panelY) {
          q.height -= pose.panelY;
          for (let j = 0; j < 3; j++) q.p[j] += row.n[j] * pose.panelY;
        }
        return q;
      }
      const d = [px - row.p[0], py - row.p[1], pz - row.p[2]];
      const dot = axis => d[0] * axis[0] + d[1] * axis[1] + d[2] * axis[2];
      const x = dot(row.b), z = dot(row.t), h = dot(row.n);
      // A descending jump can land on the exposed crown. Approaching its side cannot snap a grounded kart up.
      if (Math.abs(x - pose.x) <= row.w / 2 && Math.abs(z) <= row.len / 2 && h >= pose.topM - .35) {
        q.height = h - pose.topM;
        for (let j = 0; j < 3; j++) { q.p[j] = [px, py, pz][j] - row.n[j] * q.height; q.normal[j] = row.n[j]; }
        q.onRoad = true; q.railed = false; q.hole = null; q.piston = row.id;
        return q;
      }
      q.onRoad = false; q.railed = false; q.hole = 'piston-pocket'; q.stone = null;
      return q;
    }
    return q;
  }
  function query(...args) { return supported(original(...args), ...args); }
  if (original.below) query.below = (...args) => { const q = supported(original.below(...args), ...args); return q?.onRoad ? q : null; };
  track.query = query;
}

// The normal drift hop stays small. A deliberate approach to an exposed head gets enough clearance to cross it.
export function pistonHopUp(track, query, speed, normalUp, velocity = null) {
  if (query.forkId || !query.onRoad || query.piston) return normalUp;
  for (const row of track.moving?.traps || []) {
    if (!row.pocket) continue;
    const pose = movingPoseAt(track, row, track.moving.timeS);
    if (pose.safe || pose.open < .99 || Math.abs(pose.x) - row.w / 2 >= row.roadWidth / 2) continue;
    const ds = ((row.s - query.s + track.lengthM * 1.5) % track.lengthM) - track.lengthM / 2;
    const along = velocity ? velocity[0] * row.t[0] + velocity[1] * row.t[1] + velocity[2] * row.t[2] : speed;
    const ahead = ds * (along < 0 ? -1 : 1);
    if (ahead < -row.len / 2 || ahead > Math.max(12, Math.abs(speed) * .55) + row.len / 2) continue;
    if (Math.abs(query.lateral - pose.x) <= row.w / 2 + .7) return Math.max(normalUp, 10.8);
  }
  return normalUp;
}
