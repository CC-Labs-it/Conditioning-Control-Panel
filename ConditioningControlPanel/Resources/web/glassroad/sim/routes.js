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
const memOf = (st) => { let m = MEM.get(st); if (!m) { m = { air: -1, hold: 0, clean: false, route: -1 }; MEM.set(st, m); } return m; };

/** Forget a kart's route memory (spawn, rescue). */
export function resetRoute(st) { const m = MEM.get(st); if (m) { m.air = -1; m.hold = 0; m.clean = false; m.route = -1; } }

/** The route whose open edge the kart took off from (latchSeg), or null. */
export function routeOf(track, st) {
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
export function beneath(track, st, P, out) {
  const r = routeOf(track, st);
  if (!r) return null;
  const hint = (r.land.from + r.land.to) >> 1;
  const q = track.query(P[0], P[1], P[2], hint, out);
  if (q.seg < r.land.from || q.seg > r.land.to || !q.onRoad) return null;
  if (q.height < -1.5) return null;                 // already under the landing road: it passed
  const m = memOf(st);
  if (m.air !== r.idx) m.air = r.idx;
  return r;
}

/**
 * The route's open side: a rail everywhere but the opening (openS0..openS1), and inside the opening a low
 * kerb that holds a kart on the ground, so only a hop gets through. @returns {'rail'|'kerb'|null}
 */
export function edgeAt(track, q, lat) {
  const rs = track.routes;
  if (!rs || !rs.length) return null;
  for (const r of rs) {
    if (q.seg < r.from || q.seg >= r.to || (r.side === 'L' ? lat >= 0 : lat < 0)) continue;
    return q.s >= r.openS0 && q.s <= r.openS1 ? 'kerb' : 'rail';
  }
  return null;
}

/** The kart's take-off route when query q (any probe) lies on that route's landing road, else null. */
export function onLanding(track, st, q) {
  const r = routeOf(track, st);
  return r && q.onRoad && q.seg >= r.land.from && q.seg <= r.land.to ? r : null;
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
  if (st[S.engineV] > keep) st[S.engineV] = keep;
  m.hold = gap(feel, 'route.catchHoldS'); m.clean = clean; m.air = -1; m.route = -1;
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
 * falls); the landing must be on the landing stretch.
 */
export function registeredSkip(track, prevS, s, slackM = 45) {
  const rs = track.routes, L = track.lengthM;
  if (!rs || !rs.length) return null;
  for (const r of rs) {
    const fromOk = wrap(prevS - r.fromS, L) <= wrap(r.toS - r.fromS, L) + slackM;
    const landOk = wrap(s - r.land.fromS, L) <= wrap(r.land.toS - r.land.fromS, L) + 1;
    if (fromOk && landOk) return r;
  }
  return null;
}
