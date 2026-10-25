import { S, BOOST, RESPAWN } from './state.js';
import { movingPoseAt } from '../track/movingHazards.js';
import { yawParams, calmOf } from './steer.js';
const dot = (a, b) => a.reduce((sum, v, i) => sum + v * b[i], 0);
const local = (st, row) => {
  const d = row.p.map((v, j) => st[S.posX + j] - v);
  return { x: dot(d, row.b), z: dot(d, row.t), h: dot(d, row.n) };
};
export function movingOverlap(row, pose, p) {
  return pose.active && Math.abs(p.x - pose.x) <= row.w / 2 + .7
    && Math.abs(p.z) <= row.len / 2 + .7 && p.h > -1 && p.h < 3.2;
}

export function createMovingHazards(track) {
  const rows = track.moving?.traps || [], yp = {};
  let memory = new WeakMap(), spins = new WeakMap(), hits = 0;
  function apply(r, previous, timeS, events = r.events, dt = 1 / 60) {
    if (!r || r.kind === 'remote' || r.finished || !Number.isFinite(timeS)) return null;
    const st = r.st, old = memory.get(r), t0 = old?.timeS ?? Math.max(0, timeS - dt);
    if (old?.timeS === timeS) return null;
    memory.set(r, { timeS, until: old?.until ?? -1 });
    if (timeS < t0 || timeS - t0 > .25 || st[S.respawnPhase] !== RESPAWN.NONE) { spins.delete(r); return null; }
    if (timeS < (old?.until ?? -1) || track.forkAtSeg?.(st[S.seg])) return null;
    const distance = Math.hypot(...[0, 1, 2].map(j => st[S.posX + j] - previous[S.posX + j]));
    if (distance > 25) return null;
    for (const row of rows) {
      const ds = ((previous[S.s] - row.s + track.lengthM * 1.5) % track.lengthM) - track.lengthM / 2;
      if (Math.abs(ds) > distance + row.len + 4) continue;
      const a = local(previous, row), b = local(st, row);
      const at = f => ({ x: a.x + (b.x - a.x) * f, z: a.z + (b.z - a.z) * f, h: a.h + (b.h - a.h) * f });
      const solid = f => movingOverlap(row, movingPoseAt(track, row, t0 + (timeS - t0) * f), at(f));
      // An already caught racer can always leave, including when a head retracts.
      if (solid(0) && !solid(1)) continue;
      const steps = Math.min(256, Math.max(4, Math.ceil((distance + row.travelM * (timeS - t0) * 18) / .2)));
      let hit = null;
      for (let i = 0; i <= steps; i++) if (solid(i / steps)) { hit = i / steps; break; }
      if (hit === null) continue;
      // Spin in place and shed speed. No sideways impulse can throw a racer off the bypass.
      for (let j = 0; j < 3; j++) st[S.velX + j] *= .28;
      st[S.engineV] *= .28; st[S.stalled] = Math.max(st[S.stalled], .55);
      st[S.boostKind] = BOOST.NONE; st[S.boostT] = st[S.boostAmount] = st[S.boostHoldS] = 0;
      st[S.driftDir] = st[S.driftCharge] = st[S.driftStage] = 0;
      memory.set(r, { timeS, until: timeS + 1.1 }); spins.set(r, { start: timeS, side: -row.side }); hits++;
      events?.emit('moving-trap-hit', { racer: r.i, id: row.id, kind: row.kind, s: row.s, stallS: .55 });
      events?.emit('stall', { kind: 'moving-trap', durationS: .55 });
      return row.id;
    }
    return null;
  }
  function steer(r, actions) {
    if (r.finished || r.kind === 'remote' || r.st[S.respawnPhase] !== RESPAWN.NONE || track.forkAtSeg?.(r.st[S.seg]) || r.st[S.airborne]) return false;
    const row = rows.find(row => { const d = (row.s - r.st[S.s] + track.lengthM) % track.lengthM; return d < 65; });
    if (!row) return false;
    const st = r.st, speed = Math.hypot(st[S.velX], st[S.velY], st[S.velZ]);
    const f = track.frameAt(st[S.s] + Math.max(12, Math.min(20, speed * .65))), here = track.frameAt(st[S.s]);
    const sideV = dot([st[S.velX], st[S.velY], st[S.velZ]], here.b);
    const halfWidth = Math.min(row.roadWidth, here.w, f.w) / 2;
    const target = -row.side * Math.max(0, halfWidth - 3.5) - Math.max(-1, Math.min(1, sideV * .45));
    const d = f.p.map((v, j) => v + f.b[j] * target - st[S.posX + j]);
    const x = st[S.quatX], y = st[S.quatY], z = st[S.quatZ], w = st[S.quatW];
    const right = [1 - 2 * (y * y + z * z), 2 * (x * y + w * z), 2 * (x * z - w * y)];
    const forward = [-2 * (x * z + w * y), -2 * (y * z - w * x), -(1 - 2 * (x * x + y * y))];
    yawParams(r.feel, yp);
    actions.steer = Math.max(-1, Math.min(1, Math.atan2(dot(d, right), Math.max(.1, dot(d, forward)))
      * r.feel.get('ai.steerGain') / Math.max(.2, calmOf(speed, yp) * yp.user)));
    actions.hop = actions.hopPressed = false;
    const cap = Math.abs(target - st[S.lateral]) > 2 ? 22 : 28;
    actions.throttle = speed < cap ? 1 : 0; actions.brake = speed > cap + 3 ? .6 : 0;
    return true;
  }
  // Item modifiers rebuild spinRad every step. Compose the trap spin afterward.
  function decorate(racers, timeS) {
    for (const r of racers) {
      const spin = spins.get(r);
      if (!spin) continue;
      if (r.kind === 'remote' || r.st[S.respawnPhase] !== RESPAWN.NONE || timeS < spin.start || timeS - spin.start >= .8) { spins.delete(r); continue; }
      const u = (timeS - spin.start) / .8;
      if (r.fxv) r.fxv.spinRad += spin.side * Math.PI * 2 * (1 - (1 - u) ** 2);
    }
  }
  return { apply, steer, decorate, reset() { memory = new WeakMap(); spins = new WeakMap(); hits = 0; }, inspect(timeS = 0) {
    return { hits, traps: rows.map(row => ({ id: row.id, s: row.s, ...movingPoseAt(track, row, timeS) })),
      bridge: track.moving?.bridge };
  } };
}
