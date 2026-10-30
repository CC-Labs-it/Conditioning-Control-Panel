/* sim/tunnel.js - Tunnel Vision, the catch-up ride for the back of the pack (owner design 2026-10-30, lane B).
 *
 * A press takes the kart off the driver: it rides a fixed line along the main road (centre, or the court band /
 * stone lane the bots would hold), hovering items.tunnelHoverM, at max(top x items.tunnelSpeedMul, leader speed +
 * items.tunnelLeadMps). The ride is kinematic: sim/racers.js calls step() instead of the kart sim, so it cannot fall,
 * and sim/items.js treats a rider as not free (no item, trap, wave, snap or tether lands). Hazards and pistons are
 * skipped by racers.js while riding. Every kart the ride passes through is launched once (the BUBBLE_RAM fling, no
 * spin, no stall) and `tunnel-ram { attacker, victim, p }` goes out.
 * The ride ends after items.tunnelS, or early at place <= items.tunnelStopPlace (it never takes the lead: near the
 * leader it holds the leader's pace). The end waits for solid railed road (rails both sides, no court, stone field,
 * jump, loop, ramp wedge or water for SOLID_AHEAD_M), the hover settles, and the kart leaves at full speed;
 * the caller grants items.tunnelGuardS of guard (onEnd). Events: tunnel-start { racer }, tunnel-ram, tunnel-end
 * { racer, reason: 'time' | 'place' | 'cancel' }. Three-free.
 */
import { S, RESPAWN } from './state.js';
import { clamp, wrap, qlook } from '../core/math.js';
import { BUBBLE_RAM } from '../core/bubble.js';
import { createCourtPaths } from './court-paths.js';
import { courtAt, fieldAt } from '../track/courts.js';
import { toLip } from '../track/ramps.js';
import { cancelDrift } from './drift.js';
import { endBoost } from './boost.js';

const HOVER_IN_S = 0.3;      // feel gap: the hover rises over this at the start
const SETTLE_S = 0.35;       // feel gap: and settles back onto the road over this at the end
const ACCEL_MPS2 = 28;       // feel gap: the ride reaches its pace at this rate
const LATERAL_MPS = 4;       // feel gap: the line moves sideways at most this fast
const SOLID_AHEAD_M = 18;    // feel gap: the exit needs this much clean railed road ahead
const LEADER_HOLD_M = 12;    // feel gap: inside this gap behind the leader the ride holds the leader's pace
const EXTEND_MAX_S = 12;     // feel gap: a safety cap on the end extension (a course always has railed road sooner)
const WET = ['stream', 'pool'];

export function createTunnel(track, feel, events, { sink = null } = {}) {
  const L = track.lengthM;
  const g = (k) => feel.get('items.' + k);
  const courts = createCourtPaths(track);
  const fr = { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] }, fs = { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] };
  const Q = [0, 0, 0, 1];
  const rides = new Map();
  const emitFor = (i, name, p) => events.emit(name, p);   // like item-fire: on the main queue, the racer in the payload
  const sd = (a, b) => { const d = wrap(a - b, L); return d > L / 2 ? d - L : d; };

  /** True when the main road at s is clean railed road (the ride may hand the kart back here). */
  function solidAt(s) {
    const f = track.frameAt(s, fs), fl = f.flags || [];
    if (!(fl.includes('rail') || (fl.includes('railL') && fl.includes('railR')))) return false;
    if (fl.includes('jump') || fl.includes('magnet')) return false;
    if (WET.includes(f.surface) || (track.nodeSurface && WET.includes(track.nodeSurface(f.seg)))) return false;
    if (courtAt(track.courts, s) || fieldAt(track.fields, s)) return false;
    for (const r of track.ramps || []) { const d = toLip(r, s, L); if (d >= -2 && d <= r.lenM + 2) return false; }
    return true;
  }
  function solidAhead(s) {
    for (let d = -3; d <= SOLID_AHEAD_M; d += 2) if (!solidAt(wrap(s + d, L))) return false;
    return true;
  }

  /** The lateral the ride aims for at s: the court band nearest the ride, the nearest stone lane, else the centre. */
  function lineX(ride, s) {
    const plan = courts.at(s);
    if (plan) {
      if (!ride.court || ride.court.plan !== plan) ride.court = courts.choose(plan, 0, ride.i, 0, ride.x);
      if (ride.court) return courts.target(ride.court, s, 0, 0);
    } else ride.court = null;
    const field = fieldAt(track.fields, wrap(s + 30, L)) || fieldAt(track.fields, s);
    if (field && field.lanes && field.lanes.length) {
      if (ride.field !== field) {
        ride.field = field;
        ride.lane = field.lanes.map(laneX).filter(Number.isFinite).reduce((b, x) => (Math.abs(x - ride.x) < Math.abs(b - ride.x) ? x : b), Infinity);
      }
      if (Number.isFinite(ride.lane)) return ride.lane;
    } else ride.field = null;
    return 0;
  }
  const laneX = (l) => (typeof l === 'number' ? l : l && Number.isFinite(l.x) ? l.x : NaN);

  /** Start a ride for racer r (on the main road only). @returns true when it started */
  function start(r) {
    if (rides.has(r.i)) return false;
    if (track.forkAtSeg?.(r.st[S.seg] | 0)) return false;
    const st = r.st, v0 = Math.hypot(st[S.velX], st[S.velY], st[S.velZ]);
    rides.set(r.i, { i: r.i, t: 0, s: st[S.s], x: st[S.lateral], v: v0, want: v0, hoverK: 0, ending: null, endT: 0,
      settle: 0, victims: new Set(), court: null, field: null, lane: NaN });
    const ev = sink ? sink(r.i) : events;
    cancelDrift(st, 'item', ev); endBoost(st, feel, ev); st[S.stalled] = 0;
    emitFor(r.i, 'tunnel-start', { racer: r.i });
    return true;
  }

  const riding = (i) => rides.has(i);

  /** The kinematic move of one rider (sim/racers.js, in place of the kart sim). */
  function step(r, dt) {
    const ride = rides.get(r.i);
    if (!ride) return;
    const st = r.st;
    ride.v += clamp(ride.want - ride.v, -ACCEL_MPS2 * dt, ACCEL_MPS2 * dt);
    ride.s = wrap(ride.s + ride.v * dt, L);
    const half = track.frameAt(ride.s, fr).w / 2;
    const aim = lineX(ride, wrap(ride.s + Math.max(6, ride.v * 0.5), L));
    ride.x = clamp(ride.x + clamp(aim - ride.x, -LATERAL_MPS * dt, LATERAL_MPS * dt), -half + 1, half - 1);
    ride.hoverK = ride.settle > 0 ? Math.max(0, 1 - ride.settle / SETTLE_S) : Math.min(1, ride.hoverK + dt / HOVER_IN_S);
    const h = g('tunnelHoverM') * ride.hoverK;
    const f = track.surfaceAt ? track.surfaceAt(ride.s, ride.x, fr) : track.frameAt(ride.s, fr);
    const ox = track.surfaceAt ? 0 : ride.x;
    for (let k = 0; k < 3; k++) {
      st[S.posX + k] = f.p[k] + f.b[k] * ox + f.n[k] * h;
      st[S.velX + k] = f.t[k] * ride.v;
    }
    qlook(Q, f.t, f.n);
    st[S.quatX] = Q[0]; st[S.quatY] = Q[1]; st[S.quatZ] = Q[2]; st[S.quatW] = Q[3];
    const seg = track.frameAt(ride.s, fs).seg, a = track.nodeS[seg], b = track.nodeS[seg + 1];
    st[S.s] = ride.s; st[S.seg] = seg; st[S.latchSeg] = seg; st[S.segT] = b > a ? clamp((ride.s - a) / (b - a), 0, 1) : 0;
    st[S.lateral] = ride.x; st[S.height] = h;
    st[S.engineV] = ride.v; st[S.speedPct] = ride.v / feel.get('speed.topMps');
    st[S.grounded] = 1; st[S.airborne] = 0; st[S.airTime] = 0; st[S.edgeT] = 0; st[S.stalled] = 0;
    st[S.respawnPhase] = RESPAWN.NONE; st[S.respawnT] = 0; st[S.steerSlew] = 0; st[S.yawRate] = 0;
  }

  /** Launch a kart the ride passes through: the BUBBLE_RAM fling, away from the ride and never off an open edge. */
  function launch(rider, o, riderMps, nearOpenEdge) {
    const a = rider.st, b = o.st, f = track.frameAt(b[S.s], fs);
    let side = Math.sign(b[S.lateral] - a[S.lateral]) || 1;
    if (nearOpenEdge && nearOpenEdge(b[S.s], g('shatterEdgeSafeM'), b[S.seg]) && side === Math.sign(b[S.lateral])) side = -side;
    const closing = Math.max(0, riderMps - Math.hypot(b[S.velX], b[S.velY], b[S.velZ]));
    const speed = Math.min(BUBBLE_RAM.maxMps, BUBBLE_RAM.pushMps + closing * 0.25);
    const vn = b[S.velX] * f.n[0] + b[S.velY] * f.n[1] + b[S.velZ] * f.n[2], lift = Math.max(0, BUBBLE_RAM.upMps - vn);
    for (let k = 0; k < 3; k++) b[S.velX + k] += f.b[k] * side * speed * 0.6 + f.t[k] * speed * 0.4 + f.n[k] * lift;
    b[S.airborne] = 1; b[S.grounded] = 0; b[S.airTime] = 0; b[S.hopT] = 0; b[S.latchSeg] = b[S.seg];
    const p = [(a[S.posX] + b[S.posX]) / 2, (a[S.posY] + b[S.posY]) / 2, (a[S.posZ] + b[S.posZ]) / 2];
    events.emit('tunnel-ram', { attacker: rider.i, victim: o.i, p });
  }

  function finish(i, r, reason, onEnd) {
    const ride = rides.get(i);
    rides.delete(i);
    if (r && ride) { r.st[S.height] = 0; r.st[S.engineV] = ride.v; }
    emitFor(i, 'tunnel-end', { racer: i, reason });
    if (r && onEnd && reason !== 'cancel') onEnd(r);
  }

  /**
   * Per tick after the standings: pace, end checks, rams. opts: { canRam(o) -> bool (who this client may launch),
   * free(o) -> bool, nearOpenEdge(s, m, seg), onEnd(r) }.
   */
  function tick(dt, list, standings, opts = {}) {
    if (!rides.size) return;
    const top = feel.get('speed.topMps'), stopPlace = g('tunnelStopPlace');
    const leaderI = standings && standings.order ? standings.order[0] : -1;
    const leader = list.find((o) => o.i === leaderI) || null;
    for (const [i, ride] of rides) {
      const r = list.find((o) => o.i === i);
      if (!r || r.finished) { finish(i, r || null, 'cancel'); continue; }
      ride.t += dt;
      const place = standings ? standings.placeOf(i) : 99;
      const lv = leader && leader.i !== i ? Math.hypot(leader.st[S.velX], leader.st[S.velY], leader.st[S.velZ]) : 0;
      let want = Math.max(top * g('tunnelSpeedMul'), lv + g('tunnelLeadMps'));
      if (!ride.ending && (place <= stopPlace || ride.t >= g('tunnelS'))) { ride.ending = place <= stopPlace ? 'place' : 'time'; ride.endT = 0; }
      if (ride.ending) {
        ride.endT += dt;
        if (leader && leader.i !== i) {
          const gap = sd(leader.st[S.s], ride.s);
          if (gap > -2 && gap < LEADER_HOLD_M) want = Math.min(want, Math.max(0, lv + (gap - LEADER_HOLD_M / 2) * 0.8));
        }
        if (ride.settle > 0 || solidAhead(ride.s) || ride.endT >= EXTEND_MAX_S) ride.settle += dt;
        if (ride.settle >= SETTLE_S) { finish(i, r, ride.ending, opts.onEnd); continue; }
      }
      ride.want = want;
      const rad = g('tunnelRamRadiusM');
      for (const o of list) {
        if (o === r || rides.has(o.i) || ride.victims.has(o.i) || (opts.free && !opts.free(o)) || (opts.canRam && !opts.canRam(o))) continue;
        const dx = o.st[S.posX] - r.st[S.posX], dy = o.st[S.posY] - r.st[S.posY], dz = o.st[S.posZ] - r.st[S.posZ];
        if (dx * dx + dy * dy + dz * dz > rad * rad) continue;
        ride.victims.add(o.i);
        launch(r, o, ride.v, opts.nearOpenEdge);
      }
    }
  }

  /** Launch a local kart hit by a remote rider (online: the victim's client applies the ram). */
  function rammedBy(rider, o, nearOpenEdge) {
    launch(rider, o, Math.hypot(rider.st[S.velX], rider.st[S.velY], rider.st[S.velZ]), nearOpenEdge);
  }

  /** Render rows: { racer, t, k (0..1 the look's strength, settles out at the end) }. */
  function rows(out = []) {
    out.length = 0;
    for (const [i, ride] of rides) out.push({ racer: i, t: ride.t, k: ride.hoverK, ending: !!ride.ending });
    return out;
  }

  function cancel(i, r = null) { if (rides.has(i)) finish(i, r, 'cancel'); }
  function reset() { rides.clear(); }

  return { start, riding, step, tick, rows, rammedBy, cancel, reset, solidAhead, count: () => rides.size };
}
