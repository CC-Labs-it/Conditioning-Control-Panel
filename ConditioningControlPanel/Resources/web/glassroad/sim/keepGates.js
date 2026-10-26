/* Deterministic Keep portcullises. Caller owns race time, pause and reset.
 * Collision is a swept footprint test, resolved before progress/checkpoints.
 * A gate never crushes a kart already underneath it when it starts lowering. */
import { S, BOOST, RESPAWN, cloneState } from './state.js';
import { clamp, wrap } from '../core/math.js';
import { yawParams, calmOf } from './steer.js';
import { makeQuery } from '../track/query.js';
import { machineryBeatAt, beatCycle, machineryRearming } from '../track/machineryBeat.js';

export const KEEP_GATE = Object.freeze({ cycleS: 9.6, liftM: 8.4, heightM: 8,
  warnStartS: 3, lowerStartS: 5, holdStartS: 5.9, riseStartS: 7.2, clearStartS: 8.4,
  halfDepthM: .55, kartRadiusM: .65, kartHeightM: 1.6, stallS: .4,
  repeatS: .9, leadM: 55, leaveM: 12 });
const ease = x => { x = clamp(x, 0, 1); return x * x * (3 - 2 * x); };

/** One phase authority for both visible mesh and collision. No wall-clock reads. */
export function keepGatePhase(gate, timeS, out = {}, beat = null) {
  if (beat?.fallback) { timeS *= beat.pace; beat = null; }
  if (beat) {
    const period = 2, offset = Math.round((gate.offsetS || 0) / beat.beatS), t = beatCycle(beat.beat + offset, period);
    // Initial offsets also wait for a clear cycle with the full warning before descent.
    const safe = machineryRearming(beat, offset, period) || beat.beat < beatCycle(-offset, period), phase = safe ? 'warn' : t < .25 ? 'clear' : t < .75 ? 'warn'
      : t < 1 ? 'lower' : t < 1.25 ? 'hold' : t < 1.5 ? 'rise' : 'clear';
    // A quick stroke lands on the beat, then retracts before the next approach.
    const liftM = phase === 'lower' ? KEEP_GATE.liftM * (1 - ease((t - .75) / .25)) : phase === 'hold' ? 0
      : phase === 'rise' ? KEEP_GATE.liftM * ease((t - 1.25) / .25) : KEEP_GATE.liftM;
    return Object.assign(out, { phase, liftM, opacity: phase === 'clear' ? .035 : .62,
      blocking: liftM < KEEP_GATE.kartHeightM, cycleS: t * beat.beatS });
  }
  const K = KEEP_GATE, t = wrap(Math.max(0, Number.isFinite(timeS) ? timeS : 0) + (gate.offsetS || 0), K.cycleS);
  let phase = 'clear', liftM = K.liftM, opacity = .035;
  if (t >= K.warnStartS && t < K.lowerStartS) {
    phase = 'warn'; opacity = .035 + .585 * ease((t - K.warnStartS) / .6);
  } else if (t >= K.lowerStartS && t < K.holdStartS) {
    phase = 'lower'; liftM *= 1 - ease((t - K.lowerStartS) / (K.holdStartS - K.lowerStartS)); opacity = .62;
  } else if (t >= K.holdStartS && t < K.riseStartS) {
    phase = 'hold'; liftM = 0; opacity = .62;
  } else if (t >= K.riseStartS && t < K.clearStartS) {
    phase = 'rise'; liftM *= ease((t - K.riseStartS) / (K.clearStartS - K.riseStartS));
    opacity = .035 + .585 * (1 - ease((t - K.riseStartS) / 1.2));
  }
  return Object.assign(out, { phase, liftM, opacity, blocking: liftM < K.kartHeightM, cycleS: t });
}

/** Expanded 2D rectangle entry, including diagonal/high-speed sweeps. */
export function gateSweep(a, b, halfWidth, halfDepth) {
  if (Math.abs(a[0]) <= halfWidth && Math.abs(a[1]) <= halfDepth) return null;
  let lo = 0, hi = 1;
  for (const [axis, half] of [[0, halfWidth], [1, halfDepth]]) {
    const d = b[axis] - a[axis];
    if (Math.abs(d) < 1e-9) { if (Math.abs(a[axis]) > half) return null; continue; }
    const t0 = (-half - a[axis]) / d, t1 = (half - a[axis]) / d;
    lo = Math.max(lo, Math.min(t0, t1)); hi = Math.min(hi, Math.max(t0, t1));
    if (lo > hi) return null;
  }
  return lo <= 1 && hi >= 0 ? lo : null;
}

export function createKeepGates(track, { gates = track.keepGates || [] } = {}) {
  const K = KEEP_GATE, L = track.lengthM;
  const rows = gates.map((g, i) => {
    const row = { id: g.id || 'keep-gate-' + i, s: g.s, x: g.x || 0, w: g.w || 14, offsetS: g.offsetS || 0 };
    if (!Number.isFinite(row.s) || !Number.isFinite(row.w) || row.w <= 0 || row.w > 14 ||
        !Number.isFinite(row.x) || !Number.isFinite(row.offsetS)) throw new Error('Invalid Keep gate ' + row.id);
    const f = track.frameAt(row.s);
    if (f.w / 2 - row.w / 2 - Math.abs(row.x) < 5 - 1e-4) throw new Error('Keep gate requires 5 m bypasses: ' + row.id);
    return { ...row, p: f.p.map((v, k) => v + f.b[k] * row.x), b: [...f.b], n: [...f.n], t: [...f.t], roadWidthM: f.w };
  }).sort((a, b) => a.s - b.s);
  const halls = [];
  for (const g of rows) {
    let h = halls[halls.length - 1];
    if (!h || g.s - h.last > 42) { h = { first: g.s, last: g.s, gates: [], width: g.roadWidthM }; halls.push(h); }
    h.last = g.s; h.gates.push(g); h.width = Math.min(h.width, g.roadWidthM);
  }
  let memory = new WeakMap(), hits = 0;
  const A = [0, 0, 0], B = [0, 0, 0], Q = makeQuery(), phase = {}, YP = {};
  function mem(r) {
    let m = memory.get(r);
    if (!m) { m = { timeS: null, after: null, contact: new Map(), hall: null, lane: 0 }; memory.set(r, m); }
    return m;
  }
  function local(out, st, g) {
    const d = [st[S.posX] - g.p[0], st[S.posY] - g.p[1], st[S.posZ] - g.p[2]];
    out[0] = d[0] * g.b[0] + d[1] * g.b[1] + d[2] * g.b[2];
    out[1] = d[0] * g.t[0] + d[1] * g.t[1] + d[2] * g.t[2];
    out[2] = d[0] * g.n[0] + d[1] * g.n[1] + d[2] * g.n[2];
  }
  function apply(r, previous, timeS, events = r?.events) {
    if (!rows.length || !Number.isFinite(timeS) || !r || r.finished || r.kind === 'remote') return null;
    const st = r.st, m = mem(r), oldTime = m.timeS ?? Math.max(0, timeS - 1 / 60);
    m.timeS = timeS;
    if (st[S.respawnPhase] !== RESPAWN.NONE || timeS < oldTime || timeS - oldTime > .25) {
      m.contact.clear(); m.after = null; return null;
    }
    const distance = travel(previous, st);
    if (distance > 25) { m.after = null; return null; }
    // Contact runs after this hook. Sweep that displacement first on the next tick, so a
    // rear bump cannot turn a blocked approach into the already-underneath exemption.
    const hit = (m.after && sweep(r, m.after, previous, oldTime, oldTime, m, events)) ||
      sweep(r, previous, st, oldTime, timeS, m, events);
    if (m.after) m.after.set(st); else m.after = cloneState(st);
    return hit;
  }
  function travel(a, b) {
    return Math.hypot(b[S.posX] - a[S.posX], b[S.posY] - a[S.posY], b[S.posZ] - a[S.posZ]);
  }
  function sweep(r, previous, end, oldTime, timeS, m, events) {
    const st = r.st, distance = travel(previous, end);
    if (distance > 25 || distance < 1e-8) return null;
    for (const g of rows) {
      const ds = wrap(previous[S.s] - g.s + L / 2, L) - L / 2;
      if (Math.abs(ds) > distance + 6) continue;
      local(A, previous, g); local(B, end, g);
      const fraction = gateSweep(A, B, g.w / 2 + K.kartRadiusM, K.halfDepthM + K.kartRadiusM);
      if (fraction === null) continue;
      const hitTime = oldTime + (timeS - oldTime) * fraction, ph = keepGatePhase(g, hitTime, phase, machineryBeatAt(track, hitTime));
      const h = A[2] + (B[2] - A[2]) * fraction;
      if (h + K.kartHeightM < ph.liftM || h > ph.liftM + K.heightM || h < -1.5) continue;
      // Resolve the first intersecting movement, preserving lateral escape and height.
      const k = Math.max(0, fraction - .02 / distance);
      for (const p of [S.posX, S.posY, S.posZ]) st[p] = previous[p] + (end[p] - previous[p]) * k;
      const along = st[S.velX] * g.t[0] + st[S.velY] * g.t[1] + st[S.velZ] * g.t[2];
      const rebound = along >= 0 ? -2 : 2;
      for (let j = 0; j < 3; j++) st[S.velX + j] += (rebound - along) * g.t[j];
      st[S.engineV] = 0;
      const fresh = timeS >= (m.contact.get(g.id) ?? -Infinity);
      if (fresh) st[S.stalled] = Math.max(st[S.stalled], K.stallS);
      st[S.boostKind] = BOOST.NONE; st[S.boostT] = 0; st[S.boostAmount] = 0; st[S.boostHoldS] = 0;
      st[S.driftDir] = 0; st[S.driftCharge] = 0; st[S.driftStage] = 0;
      const q = track.query(st[S.posX], st[S.posY], st[S.posZ], st[S.seg] | 0, Q);
      st[S.seg] = q.seg; st[S.segT] = q.segT; st[S.s] = q.s; st[S.lateral] = q.lateral; st[S.height] = q.height;
      if (fresh) {
        m.contact.set(g.id, timeS + K.repeatS); hits++;
        events?.emit('keep-gate-hit', { id: g.id, phase: ph.phase, stallS: K.stallS });
      }
      return g.id;
    }
    return null;
  }
  /** After ordinary AI.drive: plan one safe side for the entire hall, through real steering only. */
  function steer(r, actions) {
    if (!rows.length || !r || r.finished || r.kind === 'remote' || r.st[S.respawnPhase] !== RESPAWN.NONE) return false;
    const st = r.st, s = st[S.s], m = mem(r);
    const hall = halls.find(h => wrap(s - (h.first - K.leadM), L) <= h.last - h.first + K.leadM + K.leaveM);
    if (!hall) { m.hall = null; return false; }
    if (m.hall !== hall) {
      m.hall = hall;
      const side = Math.abs(st[S.lateral]) > 1 ? Math.sign(st[S.lateral]) : (r.i % 2 ? 1 : -1);
      m.lane = side * (hall.width / 2 - 2.7);
    }
    const speed = Math.hypot(st[S.velX], st[S.velY], st[S.velZ]);
    const look = clamp(speed * .65, 12, 20), f = track.frameAt(s + look), here = track.frameAt(s);
    const sideV = st[S.velX] * here.b[0] + st[S.velY] * here.b[1] + st[S.velZ] * here.b[2];
    const lane = m.lane - clamp(sideV * .45, -1.3, 1.3);
    const d = f.p.map((v, j) => v + f.b[j] * lane - st[S.posX + j]);
    const qx = st[S.quatX], qy = st[S.quatY], qz = st[S.quatZ], qw = st[S.quatW];
    const right = [1 - 2 * (qy*qy + qz*qz), 2 * (qx*qy + qw*qz), 2 * (qx*qz - qw*qy)];
    const forward = [-2 * (qx*qz + qw*qy), -2 * (qy*qz - qw*qx), -(1 - 2 * (qx*qx + qy*qy))];
    const x = d.reduce((a,v,j) => a + v * right[j],0), z = d.reduce((a,v,j) => a + v * forward[j],0);
    yawParams(r.feel, YP);
    actions.steer = clamp(Math.atan2(x, Math.max(z,.1)) * r.feel.get('ai.steerGain') / Math.max(.2, calmOf(speed,YP) * YP.user), -1, 1);
    actions.hop = false; actions.hopPressed = false;
    const cap = Math.abs(st[S.lateral] - m.lane) > 2 ? 22 : 27;
    actions.throttle = speed < cap ? 1 : 0; actions.brake = speed > cap + 3 ? .6 : 0;
    return true;
  }
  return { rows, halls, apply, steer, reset() { memory = new WeakMap(); hits = 0; },
    inspect(timeS = 0) { return { hits, gates: rows.map(g => ({ id:g.id,s:g.s,x:g.x,w:g.w,...keepGatePhase(g,timeS,{},machineryBeatAt(track,timeS)) })) }; } };
}
