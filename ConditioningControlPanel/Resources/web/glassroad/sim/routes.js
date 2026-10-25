/* sim/routes.js - drop shortcuts (polish 4 ROUTES). No three.js.
 *
 * A route is a rail gap on one side of a stretch (track.routes: fromS..toS, side) over a lower landing
 * stretch (land.fromS..land.toS) further along the lap. A kart that leaves that open edge latches its
 * take-off segment (sim/kart.js); while it is airborne and off its own road, `beneath` asks the track for
 * the landing road under it (a query hinted at the landing stretch, not the latched one). Road beneath
 * holds off the edge drop; touching it lands the kart there, which moves `s` forward by the skip.
 * Nothing beneath (a miss) lets the normal edge drop and rescue run, anchored at the take-off.
 *
 * Balance: the landing stretch is a soft catch. A route landing keeps route.catchPct of top speed and
 * caps thrust there for route.catchHoldS; a clean landing (inside the pad ring, nose along the road)
 * then pays a short boost. Progress credits a skip only when it matches a registered route
 * (`registeredSkip`), so a teleport anywhere else never counts a checkpoint.
 * Per-kart route memory lives in a WeakMap on the state array (the state layout is frozen).
 */
import { S } from './state.js';
import { wrap } from '../core/math.js';
import { gap } from './gaps.js';

const MEM = new WeakMap();
const LAND_RUN_M = 20;
const landingContains = (track, r, q) => !q.forkId && q.seg >= r.land.from &&
  (q.seg <= r.land.to || wrap(q.s - r.land.toS, track.lengthM) <= LAND_RUN_M);
const memOf = (st) => { let m = MEM.get(st); if (!m) { m = { air: -1, hold: 0, clean: false, route: -1, guide: 0 }; MEM.set(st, m); } return m; };

/** Forget a kart's route memory (spawn, rescue). */
export function resetRoute(st) { const m = MEM.get(st); if (m) { m.air = -1; m.hold = 0; m.clean = false; m.route = -1; m.guide = 0; } }

/** The route whose open edge the kart took off from (latchSeg), or null. */
export function routeOf(track, st) {
  if (track.forkAtSeg?.(st[S.latchSeg])) return null;
  const rs = track.routes;
  if (!rs || !rs.length) return null;
  const n = track.nodeCount, latch = ((st[S.latchSeg] | 0) % n + n) % n;
  for (const r of rs) if (latch >= r.from - 1 && latch < r.to) return r;
  return null;
}

/**
 * Road of a registered route under an airborne kart at P, written into `out` (a track query result).
 * @returns {object|null} the route, or null when there is none or no landing road beneath
 */
export function beneath(track, st, P, out, marginM = 0) {
  const r = routeOf(track, st);
  if (!r) return null;
  const hint = (r.land.from + r.land.to) >> 1;
  // Probe at the authored landing height so an upper revolution cannot steal the query.
  const q = track.query(P[0], r.pad.p[1], P[2], hint, out);
  // A near miss can be guided in before crossing the road edge. Only real support can land it.
  if (!landingContains(track, r, q) || q.hole || (!q.onRoad && q.edgeDist < -marginM)) return null;
  q.height = (P[0] - q.p[0]) * q.normal[0] + (P[1] - q.p[1]) * q.normal[1] + (P[2] - q.p[2]) * q.normal[2];
  if (q.height < -1.5) return null;
  const m = memOf(st);
  if (m.air !== r.idx) m.air = r.idx;
  return r;
}

/**
 * The route's open side: a rail everywhere but the opening (openS0..openS1), and inside the opening a small
 * kicker lip (EDGES lane, owner 2026-10-23: the old kerb that needed a hop was never found): a kart that
 * drives over it on the ground is thrown up by route.kickUpMps (sim/kart.js) and flies on to the lower
 * road; a hop at the lip still pays the trick. slackM widens the opening at both ends (the kart's radius: a
 * body already over the opening never scrapes the rail end). @returns {'rail'|'kicker'|null}
 */
export function edgeAt(track, q, lat, slackM = 0) {
  if (q.forkId) return null;
  const rs = track.routes;
  if (!rs || !rs.length) return null;
  for (const r of rs) {
    if (q.seg < r.from - 1 || q.seg > r.to || (r.side === 'L' ? lat >= 0 : lat < 0)) continue;
    if (q.s >= r.openS0 - slackM && q.s <= r.openS1 + slackM) return 'kicker';
    if (q.seg >= r.from && q.seg < r.to) return 'rail';
  }
  return null;
}

/**
 * Polish 6 (the dare on the OUTSIDE of a tight turn): a grounded kart turning AWAY from an opening (its slewed
 * stick, or its drift, toward the other side by at least route.holdSteer) is not going for it; the lip then holds
 * it like the rail either side, so a kart that runs wide through the turn scrapes along instead of falling in.
 * A kart steering for the opening, or running straight at it, still goes through. lat = its side of the road.
 */
export function turnsAway(st, lat, feel) {
  const turn = st[S.driftDir] ? st[S.driftDir] : st[S.steerSlew];
  return turn * (lat >= 0 ? 1 : -1) <= -gap(feel, 'route.holdSteer');
}

const KF = { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] };

/** Ease the nose along the actual landing road, including curves, without changing position or speed. */
function alignLanding(track, q, F, feel, dt) {
  const f = track.frameAt(q.s, KF), t = f.t, b = f.b, n = f.n;
  const angle = Math.atan2(F[0] * b[0] + F[1] * b[1] + F[2] * b[2], F[0] * t[0] + F[1] * t[1] + F[2] * t[2]);
  const turn = Math.sign(angle) * Math.min(Math.abs(angle) * (1 - Math.exp(-dt / gap(feel, 'route.alignTcS'))), gap(feel, 'route.alignRadS') * dt);
  const c = Math.cos(turn), s = Math.sin(turn), [x, y, z] = F;
  const parallel = (n[0] * x + n[1] * y + n[2] * z) * (1 - c);
  F[0] = x * c + (n[1] * z - n[2] * y) * s + n[0] * parallel;
  F[1] = y * c + (n[2] * x - n[0] * z) * s + n[1] * parallel;
  F[2] = z * c + (n[0] * y - n[1] * x) * s + n[2] * parallel;
}

/** Finish the smooth turn after touchdown. Spawn/rescue clears it; ordinary driving has no guide. */
export function guideLanding(track, st, q, F, feel, dt) {
  const m = MEM.get(st);
  if (!m || m.guide <= 0) return;
  m.guide = Math.max(0, m.guide - dt);
  if (!q.forkId) alignLanding(track, q, F, feel, dt);
}

/**
 * The glass catch in the air: while a route's landing road is beneath a flying kart (q = its query on that
 * road), the kart's sideways speed across that road turns toward a drift back to its middle (route.airPull per
 * metre off centre, at most route.airMaxMps), changing by at most route.airAccel m/s per second, so a player
 * who aimed hard for the opening still comes down on the road while a wild exit still misses. V changes in place.
 */
export function airCatch(track, q, V, feel, dt, F = null) {
  const b = track.frameAt(q.s, KF).b, vl = V[0] * b[0] + V[1] * b[1] + V[2] * b[2];
  const m = gap(feel, 'route.airMaxMps'), want = Math.max(-m, Math.min(m, -q.lateral * gap(feel, 'route.airPull')));
  const lim = gap(feel, 'route.airAccel') * dt, k = Math.max(-lim, Math.min(lim, want - vl));
  V[0] += b[0] * k; V[1] += b[1] * k; V[2] += b[2] * k;
  if (F) alignLanding(track, q, F, feel, dt);
}
const LIP_VIEWS = new WeakMap();

/**
 * The track as a hop sees it (sim/hop.js hands it to the trick clock): with a route kicker lip added as one
 * more jump lip when this kart, sliding out toward an opening's edge at its current lateral speed, crosses
 * that edge inside the opening within lookS. Otherwise the track itself (nothing allocated on the normal line).
 * @param {object} q the kart's ground query @param {number[]} V velocity @param {number} speed forward m/s
 */
export function hopTrack(track, q, V, speed, lookS) {
  const rs = track.routes;
  if (!rs || !rs.length || !(q.edgeDist >= 0)) return track;
  for (const r of rs) {
    if (q.seg < r.from - 1 || q.seg >= r.to) continue;
    const side = r.side === 'L' ? -1 : 1;
    if (q.lateral * side <= 0) continue;
    const b = track.frameAt(q.s, KF).b, out = (V[0] * b[0] + V[1] * b[1] + V[2] * b[2]) * side;
    if (!(out > 0.3)) continue;
    const t = q.edgeDist / out;
    if (t > lookS) continue;
    const s = q.s + Math.max(0, speed) * t;
    if (s < r.openS0 - 0.5 || s > r.openS1 + 0.5) continue;
    let view = LIP_VIEWS.get(track);
    if (!view) {
      view = { lip: { s: 0, node: -1, kicker: true } };
      view.track = Object.create(track, { jumps: { value: [...(track.jumps || []), view.lip] } });
      LIP_VIEWS.set(track, view);
    }
    view.lip.s = s;
    return view.track;
  }
  return track;
}

/** The kart's take-off route when query q (any probe) lies on that route's landing road, else null. */
export function onLanding(track, st, q) {
  const r = routeOf(track, st);
  return r && q.onRoad && landingContains(track, r, q) ? r : null;
}

/** True once per airborne pass over a route's landing road: the drop is on. */
export function firstBeneath(st, r) {
  const m = memOf(st);
  if (m.route === r.idx) return false;
  m.route = r.idx;
  return true;
}

/**
 * The kart touched the landing road of route r (q = its query, V the velocity, F the nose). Applies the
 * catch and decides clean. Emits route-land { id, clean, skipM }.
 */
export function landRoute(track, st, r, q, F, feel, ev, prevS) {
  const m = memOf(st), L = track.lengthM;
  const along = F[0] * r.pad.t[0] + F[1] * r.pad.t[1] + F[2] * r.pad.t[2];
  const ds = Math.abs(wrap(q.s - r.pad.s + L / 2, L) - L / 2);
  const clean = ds <= gap(feel, 'route.cleanLenM') / 2 && Math.abs(q.lateral - r.pad.x) <= gap(feel, 'route.cleanLatM') &&
    along >= Math.cos(gap(feel, 'route.cleanDeg') * Math.PI / 180);
  const top = feel.get('speed.topMps'), keep = gap(feel, 'route.catchPct') * top;
  // polish 6: a 'gap' route (a sideways hop across to a road alongside) keeps its speed: no soft catch, no clean boost
  const soft = r.kind !== 'gap';
  if (soft && st[S.engineV] > keep) st[S.engineV] = keep;
  m.hold = soft ? gap(feel, 'route.catchHoldS') : 0; m.clean = soft && clean; m.air = -1; m.route = -1;
  m.guide = gap(feel, 'route.alignHoldS');
  const fromS = track.nodeS[st[S.latchSeg] | 0];
  st[S.latchSeg] = q.seg;                            // a landing bounce now belongs to the lower road
  ev.emit('route-land', { id: r.id, clean, skipM: Math.round(wrap(q.s - fromS, L)), s: q.s, x: q.lateral });
  return clean;
}

/** Thrust cap share while a catch holds (1 with none). Counts the hold down; returns 'boost' when a clean hold ends. */
export function catchCap(st, feel, dt) {
  const m = MEM.get(st);
  if (!m || m.hold <= 0) return 1;
  m.hold -= dt;
  if (m.hold <= 0) { m.hold = 0; return m.clean ? 'boost' : 1; }
  return gap(feel, 'route.catchPct');
}

/** Seconds of catch left (0 with none), for fx and tests. */
export function catchLeftS(st) { const m = MEM.get(st); return m ? m.hold : 0; }

/**
 * Is a forward jump of s from prevS to s the registered skip of a route? The take-off may be anywhere on
 * the open edge or up to route.takeoffSlackM past it (the kart flies on along the upper road while it
 * falls); the landing must be on the landing stretch or up to LAND_RUN_M past it (polish 6: a hard catch bounces
 * once and the flight runs on over the road before progress, which waits for the ground, judges the skip).
 */
export function registeredSkip(track, prevS, s, slackM = 45) {
  const rs = track.routes, L = track.lengthM;
  if (!rs || !rs.length) return null;
  for (const r of rs) {
    const fromOk = wrap(prevS - r.fromS, L) <= wrap(r.toS - r.fromS, L) + slackM;
    const landOk = wrap(s - r.land.fromS, L) <= wrap(r.land.toS - r.land.fromS, L) + LAND_RUN_M;
    if (fromOk && landOk) return r;
  }
  return null;
}
