// Swept toy contact. Caller supplies racers.clockS before checkpoint/progress updates.
import { S, BOOST, RESPAWN, cloneState } from './state.js';
import { clamp, wrap } from '../core/math.js';
import { yawParams, calmOf } from './steer.js';
import { makeQuery } from '../track/query.js';
import { GOONWORKS_TOYS as K, goonworksSetpieces, goonworksPose, goonworksLocal, apertureContains } from '../track/goonworks-setpieces.js';

const travel = (a, b) => Math.hypot(...[0, 1, 2].map(j => b[S.posX + j] - a[S.posX + j]));
export function goonworksOverlap(row, pose, p) {
  if (p.h < -1.2 || Math.abs(p.along) > row.depth / 2 + K.kartRadius) return false;
  if (row.kind === 'wheel') {
    for (const dx of [-K.kartRadius, 0, K.kartRadius]) for (const dh of [0, K.kartHeight / 2, K.kartHeight])
      if (apertureContains(row, pose, p.x + dx, p.h + dh)) return true;
    return false;
  }
  if (Math.abs(p.x - pose.x) > row.w / 2 + K.kartRadius) return false;
  if (row.kind === 'flipper') return pose.active && p.h < 1.8;
  return pose.active && p.h < pose.height && p.h + K.kartHeight > 0;
}

export function createGoonworksHazards(track, layout) {
  const rows = goonworksSetpieces(track, layout), query = makeQuery(), yp = {};
  let memory = new WeakMap(), hits = 0, launches = 0;
  function mem(r) {
    if (!memory.has(r)) memory.set(r, { timeS: null, after: null, until: new Map(), hall: null, lane: 0 });
    return memory.get(r);
  }
  function sync(st) {
    const q = track.query(st[S.posX], st[S.posY], st[S.posZ], st[S.seg] | 0, query);
    for (const key of ['seg', 'segT', 's', 'lateral', 'height']) st[S[key]] = q[key];
  }
  function bump(r, row, a, b, fraction, timeS, pose, m, events) {
    const st = r.st, distance = travel(a, b), fresh = timeS >= (m.until.get(row.id) ?? -Infinity);
    if (row.kind === 'flipper') {
      if (!fresh) return null;
      const up = row.n.reduce((v, n, j) => v + st[S.velX + j] * n, 0);
      for (let j = 0; j < 3; j++) st[S.velX + j] += row.n[j] * Math.max(0, 8.5 - up) + row.t[j] * 3;
      st[S.airborne] = 1; st[S.grounded] = 0; st[S.airTime] = 0; st[S.hopT] = 0;
      st[S.engineV] = Math.min(45, st[S.engineV] + 3); launches++;
      m.until.set(row.id, timeS + 3);
      events?.emit('goonworks-launch', { racer: r.i, id: row.id, s: row.s, kind: row.kind });
      return row.id;
    }
    // A rising tile under a stationary kart lifts it clear instead of repeatedly crushing it.
    if (row.kind === 'block' && pose.phase === 'rise' && distance < .15) {
      const h = goonworksLocal(st, row).h;
      for (let j = 0; j < 3; j++) { st[S.posX + j] += row.n[j] * Math.max(0, pose.height - h + .08); st[S.velX + j] += row.n[j] * 5; }
      st[S.airborne] = 1; st[S.grounded] = 0; st[S.airTime] = 0; st[S.hopT] = 0;
    } else {
      const f = Math.max(0, fraction - .06 / Math.max(distance, .06));
      for (let j = 0; j < 3; j++) st[S.posX + j] = a[S.posX + j] + (b[S.posX + j] - a[S.posX + j]) * f;
      const along = row.t.reduce((v, n, j) => v + st[S.velX + j] * n, 0), rebound = along >= 0 ? -1.5 : 1.5;
      for (let j = 0; j < 3; j++) st[S.velX + j] += (rebound - along) * row.t[j];
      st[S.engineV] *= .18;
      if (fresh) st[S.stalled] = Math.max(st[S.stalled], K.stallS);
    }
    st[S.boostKind] = BOOST.NONE; st[S.boostT] = 0; st[S.boostAmount] = 0; st[S.boostHoldS] = 0;
    st[S.driftDir] = 0; st[S.driftCharge] = 0; st[S.driftStage] = 0; sync(st);
    if (fresh) {
      hits++; m.until.set(row.id, timeS + K.repeatS);
      events?.emit('goonworks-hit', { racer: r.i, id: row.id, kind: row.kind, s: row.s, stallS: K.stallS });
    }
    return row.id;
  }
  function sweep(r, a, b, t0, t1, m, events) {
    const distance = travel(a, b); if (distance > 25) return null;
    for (const row of rows) {
      const ds = wrap(a[S.s] - row.s + track.lengthM / 2, track.lengthM) - track.lengthM / 2;
      if (Math.abs(ds) > distance + row.depth / 2 + 3) continue;
      const start = goonworksLocal(a, row), end = goonworksLocal(b, row);
      if (Math.min(start.h, end.h) > (row.kind === 'wheel' ? row.centerY + row.radius : row.height || row.h) + 2 || Math.max(start.h, end.h) < -1.2) continue;
      const at = f => ({ x: start.x + (end.x - start.x) * f, along: start.along + (end.along - start.along) * f, h: start.h + (end.h - start.h) * f });
      const solid = f => goonworksOverlap(row, goonworksPose(row, t0 + (t1 - t0) * f), at(f));
      // Already-inside racers can escape a closing toy. An after-contact sweep below prevents rear-bump tunnelling.
      if (row.kind !== 'flipper' && solid(0)) continue;
      const steps = Math.min(96, Math.max(2, Math.ceil((distance + Math.max(0, t1 - t0) * 12) / .3)));
      for (let i = 0; i <= steps; i++) {
        const fraction = i / steps; if (!solid(fraction)) continue;
        let lo = Math.max(0, (i - 1) / steps), hi = fraction;
        for (let j = 0; j < 8 && hi > lo; j++) { const mid = (lo + hi) / 2; if (solid(mid)) hi = mid; else lo = mid; }
        const pose = goonworksPose(row, t0 + (t1 - t0) * hi);
        return bump(r, row, a, b, hi, t1, pose, m, events);
      }
    }
    return null;
  }
  function apply(r, previous, timeS, events = r?.events) {
    if (!rows.length || !r || r.finished || r.kind === 'remote' || !Number.isFinite(timeS)) return null;
    const st = r.st, m = mem(r), oldTime = m.timeS ?? Math.max(0, timeS - 1 / 60);
    if (m.timeS === timeS) return null;
    m.timeS = timeS;
    if (st[S.respawnPhase] !== RESPAWN.NONE || timeS < oldTime || timeS - oldTime > .25 || travel(previous, st) > 25) {
      m.after = null; m.until.clear(); return null;
    }
    const hit = (m.after && sweep(r, m.after, previous, oldTime, oldTime, m, events)) || sweep(r, previous, st, oldTime, timeS, m, events);
    if (m.after) m.after.set(st); else m.after = cloneState(st);
    return hit;
  }
  function steer(r, actions) {
    if (!rows.length || !r || r.finished || r.kind === 'remote' || r.st[S.airborne] || r.st[S.respawnPhase] !== RESPAWN.NONE) return false;
    const st = r.st, m = mem(r), s = st[S.s], row = rows.find(z => z.kind !== 'flipper' && s >= z.s - 70 && s < z.toS + 12);
    if (!row) { m.hall = null; return false; }
    if (m.hall !== row.section) {
      m.hall = row.section;
      const side = Math.abs(st[S.lateral]) > 2 ? Math.sign(st[S.lateral]) : r.i % 2 ? 1 : -1;
      const hallRows = rows.filter(z => z.section === row.section);
      m.lane = side * Math.max(...hallRows.map(z => z.bypass));
    }
    const speed = Math.hypot(st[S.velX], st[S.velY], st[S.velZ]), f = track.frameAt(s + clamp(speed * .65, 14, 23));
    const lane = Math.sign(m.lane) * Math.min(Math.abs(m.lane), f.w / 2 - 3);
    const d = f.p.map((v, j) => v + f.b[j] * lane - st[S.posX + j]);
    const qx = st[S.quatX], qy = st[S.quatY], qz = st[S.quatZ], qw = st[S.quatW];
    const right = [1 - 2 * (qy * qy + qz * qz), 2 * (qx * qy + qw * qz), 2 * (qx * qz - qw * qy)];
    const forward = [-2 * (qx * qz + qw * qy), -2 * (qy * qz - qw * qx), -(1 - 2 * (qx * qx + qy * qy))];
    const x = d.reduce((v, n, j) => v + n * right[j], 0), z = d.reduce((v, n, j) => v + n * forward[j], 0);
    yawParams(r.feel, yp);
    actions.steer = clamp(Math.atan2(x, Math.max(z, .1)) * r.feel.get('ai.steerGain') / Math.max(.2, calmOf(speed, yp) * yp.user), -1, 1);
    actions.hop = false; actions.hopPressed = false;
    const cap = Math.abs(st[S.lateral] - lane) > 2 ? 21 : 27;
    actions.throttle = speed < cap ? 1 : 0; actions.brake = speed > cap + 3 ? .6 : 0;
    return true;
  }
  return { rows, apply, steer, reset() { memory = new WeakMap(); hits = 0; launches = 0; },
    inspect(timeS = 0) { return { hits, launches, toys: rows.map(row => ({ id: row.id, kind: row.kind, s: row.s, ...goonworksPose(row, timeS) })) }; } };
}
