/* sim/tunnel.js - Tunnel Vision, the catch-up ride for the last places (owner design 2026-10-30, lane B; rules 2026-11-01).
 *
 * A press takes the kart off the driver: it rides a fixed line along the main road (centre, or the court band /
 * stone lane the bots would hold), hovering items.tunnelHoverM. The ride is kinematic: sim/racers.js calls step()
 * instead of the kart sim, so it cannot fall, and sim/items.js treats a rider as not free (no item, trap, wave, snap
 * or tether lands). Hazards and pistons are skipped by racers.js while riding, and sim/contact.js skips a rider's pairs.
 *
 * Target: the nearest kart ahead in places 1 to items.tunnelTargetPlaces, recomputed as places change until the ride
 * locks on. Pace: the speed that arrives inside items.tunnelMaxS, never below max(top x items.tunnelSpeedMul, leader
 * speed + items.tunnelLeadMps) until it brakes into the target, so a big gap rides faster and a small one arrives sooner.
 * Every other kart the ride passes through is launched once (the BUBBLE_RAM fling, no spin, no stall) and
 * `tunnel-ram { attacker, victim, p }` goes out; the target never is.
 * Landing: the ride locks beside the target, one kart width to the side and just behind it. Once the target is on the
 * ground on solid railed road (rails both sides, no court, stone field, jump, loop, ramp wedge, water or fork for
 * SOLID_AHEAD_M) the hover settles and the kart is handed back at the target's speed, closing sideways by
 * items.tunnelLandDvMps: contact.js makes a normal bump of that. Unsafe road waits, so a landing can pass tunnelMaxS.
 * With no target (all finished, or the rest are riding or being rescued) it runs tunnelMaxS at the floor pace and leaves
 * at full speed. It never takes the lead: step() stays behind the leader, and a ride from behind items.tunnelStopPlace
 * ends at that place or better; one that started there ends only at the lead.
 * The caller grants items.tunnelGuardS of guard (onEnd). Events: tunnel-start { racer }, tunnel-ram, tunnel-end
 * { racer, reason: 'target' | 'time' | 'place' | 'cancel' }. Three-free.
 */
import { S, RESPAWN } from './state.js';
import { clamp, wrap, qlook } from '../core/math.js';
import { BUBBLE_RAM } from '../core/bubble.js';
import { createCourtPaths } from './court-paths.js';
import { courtAt, fieldAt } from '../track/courts.js';
import { toLip } from '../track/ramps.js';
import { cancelDrift } from './drift.js';
import { endBoost } from './boost.js';
import { STEP_MAX_M } from './progress.js';

const HOVER_IN_S = 0.3;      // feel gap: the hover rises over this at the start
const SETTLE_S = 0.35;       // feel gap: and settles back onto the road over this at the end
const ACCEL_MPS2 = 28;       // feel gap: the ride reaches its pace at this rate (a bigger gap reaches it in UP_S)
const UP_S = 1;              // feel gap: the longest the ride takes to reach its closing speed
const LATERAL_MPS = 4;       // feel gap: the line moves sideways at most this fast
const LAND_LATERAL_MPS = 14; // feel gap: and this fast on the way to the target's side
const APPROACH_S = 1.2;      // feel gap: the move to the target's side starts this far out, in seconds at the current speed
const SOLID_AHEAD_M = 18;    // feel gap: the exit needs this much clean railed road ahead
const LEADER_HOLD_M = 12;    // feel gap: inside this gap behind the leader a ride with no target holds the leader's pace
const EXTEND_MAX_S = 12;     // feel gap: a safety cap past tunnelMaxS (a course always has railed road sooner)
const LAND_RESERVE_S = 0.6;  // feel gap: arrive this long before tunnelMaxS, for the settle and the last sideways move
const MIN_T_S = 0.05;        // feel gap: a ride past its time still arrives over this
const ARRIVE_M = 2;          // feel gap: this close along the road the ride locks on
const BEHIND_M = 0.3;        // feel gap: the lock sits this far behind the target, so it never swaps places
const FOLLOW_PER_S = 6;      // feel gap: the lock closes along-road error at this rate
const SIDE_SPARE_M = 0.25;   // feel gap: beside the target, the air left between the two until the settle
const LAND_OVERLAP_M = 0.1;  // feel gap: the settle ends this far inside contact range, so the next contact pass bumps
const SIDE_NEAR_M = 0.5;     // feel gap: the settle starts once the ride is this close to its place at the target's side
const RAM_MIN_CLOSING_MPS = 12;   // feel gap: slower than this past a kart is no ram (braking into a pack, a lock)
const SLICE_M = STEP_MAX_M * 2 / 3;   // a ride tick longer than this goes in slices, so progress.js credits its lap lines
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
  const speedOf = (o) => Math.hypot(o.st[S.velX], o.st[S.velY], o.st[S.velZ]);
  const onFork = (o) => !!track.forkAtSeg?.(o.st[S.seg] | 0);
  const racing = (o) => !o.finished && !(o.meta && o.meta.left) && (o.st[S.respawnPhase] === RESPAWN.NONE || o.st[S.respawnPhase] === RESPAWN.GRACE);
  const isRider = (o) => rides.has(o.i) || (o.fxv && o.fxv.effect === 'tunnel');

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

  /** The nearest kart ahead of r in places 1 to tunnelTargetPlaces still racing and not riding itself, or null. */
  function targetOf(r, list, standings) {
    const me = standings.progressOf(r.i), top = g('tunnelTargetPlaces');
    let best = null, bestGap = Infinity;
    for (const o of list) {
      if (o === r || !racing(o) || isRider(o) || standings.placeOf(o.i) > top) continue;
      const d = standings.progressOf(o.i) - me;
      if (d > 0 && d < bestGap) { best = o; bestGap = d; }
    }
    return best;
  }

  /** Start a ride for racer r (on the main road only). @returns true when it started */
  function start(r) {
    if (rides.has(r.i)) return false;
    if (track.forkAtSeg?.(r.st[S.seg] | 0)) return false;
    const st = r.st, v0 = Math.hypot(st[S.velX], st[S.velY], st[S.velZ]);
    rides.set(r.i, { i: r.i, t: 0, s: st[S.s], prevS: st[S.s], x: st[S.lateral], v: v0, want: v0, up: ACCEL_MPS2, down: ACCEL_MPS2,
      hoverK: 0, ending: null, endT: 0, settle: 0, victims: new Set(), court: null, field: null, lane: NaN,
      tgt: null, chasing: null, brake: 0, lock: false, side: 0, aim: null, land: 0, limit: Infinity, place0: 0 });
    const ev = sink ? sink(r.i) : events;
    cancelDrift(st, 'item', ev); endBoost(st, feel, ev); st[S.stalled] = 0;
    emitFor(r.i, 'tunnel-start', { racer: r.i });
    return true;
  }

  const riding = (i) => rides.has(i);

  /** How many step() calls this tick takes: one until the ride outruns a driving-sized step. */
  function slicesFor(r, dt) {
    const ride = rides.get(r.i);
    return ride ? clamp(Math.ceil(Math.max(ride.v, ride.want) * dt / SLICE_M), 1, 32) : 1;
  }

  /** The kinematic move of one rider (sim/racers.js, in place of the kart sim). */
  function step(r, dt) {
    const ride = rides.get(r.i);
    if (!ride) return;
    const st = r.st;
    ride.v += clamp(ride.want - ride.v, -ride.down * dt, ride.up * dt);
    const adv = Math.min(ride.v * dt, Math.max(0, ride.limit));   // never past the target, never into the lead
    ride.limit -= adv;
    ride.s = wrap(ride.s + adv, L);
    const half = track.frameAt(ride.s, fr).w / 2;
    const aim = ride.aim !== null ? ride.aim : lineX(ride, wrap(ride.s + Math.max(6, ride.v * 0.5), L));
    const rate = (ride.aim !== null ? LAND_LATERAL_MPS : LATERAL_MPS) * dt;
    ride.x = clamp(ride.x + clamp(aim - ride.x, -rate, rate), -half + 1, half - 1);
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
    const closing = Math.max(0, riderMps - speedOf(o));
    const speed = Math.min(BUBBLE_RAM.maxMps, BUBBLE_RAM.pushMps + closing * 0.25);
    const vn = b[S.velX] * f.n[0] + b[S.velY] * f.n[1] + b[S.velZ] * f.n[2], lift = Math.max(0, BUBBLE_RAM.upMps - vn);
    for (let k = 0; k < 3; k++) b[S.velX + k] += f.b[k] * side * speed * 0.6 + f.t[k] * speed * 0.4 + f.n[k] * lift;
    b[S.airborne] = 1; b[S.grounded] = 0; b[S.airTime] = 0; b[S.hopT] = 0; b[S.latchSeg] = b[S.seg];
    const p = [(a[S.posX] + b[S.posX]) / 2, (a[S.posY] + b[S.posY]) / 2, (a[S.posZ] + b[S.posZ]) / 2];
    events.emit('tunnel-ram', { attacker: rider.i, victim: o.i, p });
  }

  /** Did this tick's move (fromS to toS) ride through o? Fast enough to count, and swept: a fast tick is many metres long. */
  function rams(rider, o, fromS, toS) {
    if (speedOf(rider) - speedOf(o) < RAM_MIN_CLOSING_MPS) return false;
    const rad = g('tunnelRamRadiusM');
    const dx = o.st[S.posX] - rider.st[S.posX], dy = o.st[S.posY] - rider.st[S.posY], dz = o.st[S.posZ] - rider.st[S.posZ];
    if (dx * dx + dy * dy + dz * dz <= rad * rad) return true;
    if (onFork(o) || Math.abs(o.st[S.lateral] - rider.st[S.lateral]) > rad) return false;
    const moved = wrap(toS - fromS, L), past = sd(o.st[S.s], fromS);
    return moved > rad && past >= 0 && past <= moved;
  }

  function finish(i, r, reason, onEnd) {
    const ride = rides.get(i);
    if (r && ride && reason === 'target') {
      // at the target's speed, inside contact range and closing sideways: the next contact.js pass makes the bump.
      // The aim moved after this tick's step, so the last centimetres are taken here.
      ride.settle = SETTLE_S; ride.want = ride.v; ride.up = ride.down = 0; ride.limit = 0;
      ride.aim = ride.land; ride.x = ride.land;
      step(r, 0);
      // closing is measured against the target, which may be steering away
      const f = track.frameAt(ride.s, fs), o = ride.tgt.st;
      const away = o[S.velX] * f.b[0] + o[S.velY] * f.b[1] + o[S.velZ] * f.b[2], lat = away - ride.side * g('tunnelLandDvMps');
      for (let k = 0; k < 3; k++) r.st[S.velX + k] = f.t[k] * ride.v + f.b[k] * lat;
    }
    rides.delete(i);
    if (r && ride) { r.st[S.height] = 0; r.st[S.engineV] = ride.v; }
    emitFor(i, 'tunnel-end', { racer: i, reason });
    if (r && onEnd && reason !== 'cancel') onEnd(r);
  }

  /** The pace, the side and the settle while a target exists. @returns true when the ride landed */
  function chase(ride, r, t, floor, dt, standings, onEnd) {
    const i = ride.i, gapM = standings.progressOf(t.i) - standings.progressOf(i), vt = speedOf(t);
    if (!ride.lock && gapM <= ARRIVE_M) ride.lock = true;
    // beside the target, on the side with room
    const half = track.frameAt(ride.s, fs).w / 2, contactM = 2 * feel.get('contact.radiusM');
    const beside = contactM + SIDE_SPARE_M, tx = t.st[S.lateral];
    let side = ride.side || (ride.x >= tx ? 1 : -1);
    if (Math.abs(tx + side * beside) > half - 1) side = -side;
    ride.side = side;
    const solid = solidAt(ride.s), near = ride.lock || gapM <= ride.v * APPROACH_S;
    ride.aim = near && solid ? tx + side * beside : null;
    if (!ride.lock) {
      // a closing speed that arrives in the time left and brakes on the way in: closing c for T - c / A, then at A down to 0
      // the brake is set once per target (and raised only when the ride falls behind): set every tick it never starts
      const T = Math.max(MIN_T_S, g('tunnelMaxS') - LAND_RESERVE_S - ride.t);
      if (ride.chasing !== t) { ride.chasing = t; ride.brake = Math.max(g('tunnelBrakeMps2'), 8 * gapM / (T * T)); }
      const A = ride.brake = Math.max(ride.brake, 2.2 * gapM / (T * T));
      const c = A * (T - Math.sqrt(Math.max(0, T * T - 2 * gapM / A)));
      ride.want = Math.min(Math.max(floor, vt + c), vt + Math.sqrt(2 * A * Math.max(0, gapM - BEHIND_M)));
      ride.up = Math.max(ACCEL_MPS2, c / UP_S); ride.down = A;
      ride.settle = 0;
      return false;
    }
    ride.want = Math.max(0, vt + clamp((gapM - BEHIND_M) * FOLLOW_PER_S, -20, 20));
    ride.up = ride.down = Infinity;
    const air = t.st[S.airborne] || !t.st[S.grounded];
    const safe = !air && !onFork(t) && solidAhead(ride.s) && Math.abs(ride.x - (tx + side * beside)) <= SIDE_NEAR_M;
    ride.settle = safe ? ride.settle + dt : 0;
    if (ride.settle > 0) {
      const k = clamp(ride.settle / SETTLE_S, 0, 1), inside = Math.sqrt(Math.max(0, (contactM - LAND_OVERLAP_M) ** 2 - BEHIND_M * BEHIND_M));
      ride.aim = tx + side * (beside + (inside - beside) * k);
      ride.land = tx + side * inside;
    }
    if (ride.settle >= SETTLE_S) { finish(i, r, 'target', onEnd); return true; }
    if (ride.t >= g('tunnelMaxS') + EXTEND_MAX_S) { finish(i, r, 'time', onEnd); return true; }   // no safe road: no bump
    return false;
  }

  /**
   * Per tick after the standings: pace, end checks, rams. opts: { canRam(o) -> bool (who this client may launch),
   * free(o) -> bool, nearOpenEdge(s, m, seg), onEnd(r) }.
   */
  function tick(dt, list, standings, opts = {}) {
    if (!rides.size) return;
    const top = feel.get('speed.topMps'), stopPlace = g('tunnelStopPlace'), maxS = g('tunnelMaxS');
    const leaderI = standings && standings.order ? standings.order[0] : -1;
    const leader = list.find((o) => o.i === leaderI) || null;
    for (const [i, ride] of rides) {
      const r = list.find((o) => o.i === i);
      if (!r || r.finished) { finish(i, r || null, 'cancel'); continue; }
      ride.t += dt;
      const place = standings ? standings.placeOf(i) : 99;
      if (!ride.place0) ride.place0 = place;
      const rival = leader && leader.i !== i && !leader.finished ? leader : null;
      const lv = rival ? speedOf(rival) : 0;
      const floor = Math.max(top * g('tunnelSpeedMul'), lv + g('tunnelLeadMps'));
      // a locked target that finished, fell or dropped out of the top places hands over to the next one
      if (ride.lock && (!ride.tgt || !racing(ride.tgt) || isRider(ride.tgt) || (standings && standings.placeOf(ride.tgt.i) > g('tunnelTargetPlaces')))) { ride.lock = false; ride.tgt = null; ride.settle = 0; ride.aim = null; }
      if (!ride.lock) ride.tgt = standings && !ride.ending ? targetOf(r, list, standings) : null;
      if (!ride.ending && place <= (ride.place0 <= stopPlace ? 1 : stopPlace)) { ride.ending = 'place'; ride.tgt = null; ride.lock = false; ride.aim = null; ride.settle = 0; }
      const t = ride.tgt;
      if (t) {
        if (chase(ride, r, t, floor, dt, standings, opts.onEnd)) continue;
      } else {
        // no target: the floor pace for the time, then the old exit
        ride.aim = null; ride.up = ride.down = ACCEL_MPS2;
        let want = floor;
        if (!ride.ending && ride.t >= maxS) { ride.ending = 'time'; ride.endT = 0; }
        if (ride.ending) {
          ride.endT += dt;
          if (rival) {
            const gap = sd(rival.st[S.s], ride.s);
            if (gap > -2 && gap < LEADER_HOLD_M) want = Math.min(want, Math.max(0, lv + (gap - LEADER_HOLD_M / 2) * 0.8));
          }
          if (ride.settle > 0 || solidAhead(ride.s) || ride.endT >= EXTEND_MAX_S) ride.settle += dt;
          if (ride.settle >= SETTLE_S) { finish(i, r, ride.ending, opts.onEnd); continue; }
        }
        ride.want = want;
      }
      // never past the target, never into the lead (step() spends this)
      ride.limit = Infinity;
      if (standings) {
        const me = standings.progressOf(i), room = (o) => standings.progressOf(o.i) - me - BEHIND_M + speedOf(o) * dt;
        if (t) ride.limit = Math.min(ride.limit, room(t));
        if (rival) ride.limit = Math.min(ride.limit, room(rival));
      }
      if (!ride.lock) {
        for (const o of list) {
          if (o === r || o === t || rides.has(o.i) || ride.victims.has(o.i) || (opts.free && !opts.free(o)) || (opts.canRam && !opts.canRam(o))) continue;
          if (!rams(r, o, ride.prevS, ride.s)) continue;
          ride.victims.add(o.i);
          launch(r, o, ride.v, opts.nearOpenEdge);
        }
      }
      ride.prevS = ride.s;
    }
  }

  /** Launch a local kart hit by a remote rider (online: the victim's client applies the ram). */
  function rammedBy(rider, o, nearOpenEdge) {
    launch(rider, o, speedOf(rider), nearOpenEdge);
  }

  /** Render rows: { racer, t, k (0..1 the look's strength, settles out at the end) }. */
  function rows(out = []) {
    out.length = 0;
    for (const [i, ride] of rides) out.push({ racer: i, t: ride.t, k: ride.hoverK, ending: !!ride.ending || ride.settle > 0 });
    return out;
  }

  function cancel(i, r = null) { if (rides.has(i)) finish(i, r, 'cancel'); }
  function reset() { rides.clear(); }

  return { start, riding, step, slicesFor, tick, rows, rams, targetOf, rammedBy, cancel, reset, solidAhead, count: () => rides.size };
}
