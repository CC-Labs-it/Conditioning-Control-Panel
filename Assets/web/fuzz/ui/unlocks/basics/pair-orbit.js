/* Owner 2026-11-08: two bouncing copies that pass close feel a pull, curl into a short spiral around each other, touch
 * and bounce apart. Pure: no DOM, no clock of its own. Bodies are centres in px with px/s velocities; the bouncer
 * (ui/unlocks/basics.js) converts to and from its own wall units.
 *
 * Calm on purpose: a small capture ring, an inset contact box, pairs only, at most MAX_PAIRS spirals at once, a cooldown
 * per pair after it ends, and a time cap that resolves a slow spiral into the bounce. Every step only turns a velocity
 * (its length is kept), and the bounce is a mirror along the contact normal, so no copy ends faster or slower. */

export const ORBIT = Object.freeze({
  CAPTURE: 1.5,      // capture when centres are closer than this times the sum of the mean half sizes
  RELEASE: 2.2,      // a pair that drifts this far apart lets go without a bounce
  INSET: 0.8,        // the contact box is 80% of the painted box, never larger
  MAX_S: 1.5,        // a spiral that has not touched by then resolves into the bounce
  COOLDOWN_S: 3.5,   // a pair that just let go is not captured again for this long
  REST_S: 1.5,       // and neither copy joins any other spiral for this long
  MAX_PAIRS: 2,      // never more than this many spirals on screen
  GAP_S: 9,          // and at least this long between any two captures, anywhere (owner 2026-11-08: one every ~2 s was too busy)
  TURN: 14,          // rad/s the pull may turn a velocity
  INWARD_0: -0.1,    // lean of the path at capture (rad; a hair outward holds the ring while they swing round), tightening to INWARD_1
  INWARD_1: 0.6,
});

const len = (x, y) => Math.hypot(x, y);
/** Mean half size of a body: the capture ring scales with it. */
export const halfSize = (b) => (b.w + b.h) / 4;

/** Capture test: close enough, both free, approaching (or not leaving). */
export function canCapture(a, b, o = ORBIT) {
  const dx = b.x - a.x, dy = b.y - a.y, d = len(dx, dy);
  if (!(d > 0) || d > o.CAPTURE * (halfSize(a) + halfSize(b))) return false;
  const closing = (b.vx - a.vx) * dx + (b.vy - a.vy) * dy;
  /* Already overlapping (a fresh copy leaving the button, or a pass under the pair cap): let it pass through. */
  return closing <= 0 && !contact(a, b, o);
}

/** Inset boxes overlap: the contact. Returns the separation axis and depth, or null. */
export function contact(a, b, o = ORBIT) {
  const ox = (a.w + b.w) / 2 * o.INSET - Math.abs(b.x - a.x);
  const oy = (a.h + b.h) / 2 * o.INSET - Math.abs(b.y - a.y);
  if (ox <= 0 || oy <= 0) return null;
  return ox < oy ? { axis: 'x', depth: ox } : { axis: 'y', depth: oy };
}

/** Turn (vx, vy) toward unit (ux, uy) by at most `max` radians, keeping its length. */
export function turnToward(vx, vy, ux, uy, max) {
  const s = len(vx, vy);
  if (!(s > 0)) return [vx, vy];
  const a = Math.atan2(vy, vx), want = Math.atan2(uy, ux);
  let d = want - a;
  d = Math.atan2(Math.sin(d), Math.cos(d));
  const t = a + Math.max(-max, Math.min(max, d));
  return [Math.cos(t) * s, Math.sin(t) * s];
}

/** One orbit step for a captured pair: each velocity leans toward the tangent of the shared centre, tipped inward by an
 *  angle that tightens with age, so the paths curl into a spiral. Only directions change. */
export function orbitStep(a, b, pair, dt, o = ORBIT) {
  const cx = (a.x + b.x) / 2, cy = (a.y + b.y) / 2;
  const k = Math.min(1, pair.age / o.MAX_S);
  const lean = o.INWARD_0 + (o.INWARD_1 - o.INWARD_0) * k;
  const c = Math.cos(lean), s = Math.sin(lean);
  for (const body of [a, b]) {
    const rx = body.x - cx, ry = body.y - cy, r = len(rx, ry) || 1;
    const nx = -rx / r, ny = -ry / r;                 // inward
    const tx = -ry / r * pair.sense, ty = rx / r * pair.sense;   // tangent, same sense for both
    [body.vx, body.vy] = turnToward(body.vx, body.vy, tx * c + nx * s, ty * c + ny * s, o.TURN * dt);
  }
}

/** The bounce: each copy mirrors its velocity along the contact normal if it is heading into the other (lengths kept),
 *  and the pair is pushed apart along the shallow axis so the inset boxes no longer overlap. */
export function bounce(a, b, hit, o = ORBIT) {
  let nx = b.x - a.x, ny = b.y - a.y;
  const d = len(nx, ny);
  if (d > 0) { nx /= d; ny /= d; } else { nx = 1; ny = 0; }
  const into = (v, sx, sy) => v.vx * sx + v.vy * sy;
  const ia = into(a, nx, ny);
  if (ia > 0) { a.vx -= 2 * ia * nx; a.vy -= 2 * ia * ny; }
  const ib = into(b, -nx, -ny);
  if (ib > 0) { b.vx += 2 * ib * nx; b.vy += 2 * ib * ny; }
  const h = hit || contact(a, b, o);
  if (h) {
    const push = h.depth / 2 + 0.5;
    if (h.axis === 'x') { const sg = b.x >= a.x ? 1 : -1; a.x -= push * sg; b.x += push * sg; }
    else { const sg = b.y >= a.y ? 1 : -1; a.y -= push * sg; b.y += push * sg; }
  }
  return { axis: h?.axis || (Math.abs(nx) >= Math.abs(ny) ? 'x' : 'y'), nx, ny, x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 };
}

/** Back to the bouncer's wall units once a spiral ends: |dx| + |dy| = 2 is what sets wall hits per second (and so the
 *  pay), and the lean stays diagonal (0.7..1.3 on x) so no copy ends up ping-ponging on one axis. */
export function snapDiag(dx, dy, lo = 0.7) {
  const sx = dx < 0 ? -1 : 1, sy = dy < 0 ? -1 : 1;
  const t = Math.abs(dx) + Math.abs(dy);
  const a = t > 0 ? Math.max(lo, Math.min(2 - lo, 2 * Math.abs(dx) / t)) : 1;
  return [sx * a, sy * (2 - a)];
}

/** The pair keeper. step(bodies, dt) turns and bounces bodies in place and returns what happened this step:
 *  [{type:'capture'|'contact'|'release', i, j, ...}]. A body with `free: false` is skipped (Gentle, hidden, mid-drag). */
export function createOrbits(o = ORBIT) {
  const pairs = [];            // { i, j, age, sense }
  const cool = new Map();      // 'i:j' (pair) or 'i' (one copy resting) -> seconds left
  let clock = 0, lastCapture = -Infinity;
  const keyOf = (i, j) => (i < j ? `${i}:${j}` : `${j}:${i}`);
  const end = (p, type, extra) => {
    pairs.splice(pairs.indexOf(p), 1);
    cool.set(keyOf(p.i, p.j), o.COOLDOWN_S);
    cool.set(String(p.i), o.REST_S); cool.set(String(p.j), o.REST_S);
    return { type, i: p.i, j: p.j, ...extra };
  };
  return {
    get pairs() { return pairs.map((p) => ({ ...p })); },
    cooling(i, j) { return (cool.get(keyOf(i, j)) || 0) > 0; },
    clear() { pairs.length = 0; cool.clear(); lastCapture = -Infinity; },
    step(bodies, dt) {
      const out = [];
      if (!(dt > 0)) return out;
      clock += dt;
      for (const [k, t] of cool) { if (t - dt <= 0) cool.delete(k); else cool.set(k, t - dt); }
      /* Live spirals first: age, turn, touch, or let go. */
      for (const p of [...pairs]) {
        const a = bodies[p.i], b = bodies[p.j];
        if (!a || !b || a.free === false || b.free === false) { out.push(end(p, 'release', { why: 'gone' })); continue; }
        p.age += dt;
        const hit = contact(a, b, o);
        if (hit || p.age >= o.MAX_S) { out.push(end(p, 'contact', bounce(a, b, hit, o))); continue; }
        if (len(b.x - a.x, b.y - a.y) > o.RELEASE * (halfSize(a) + halfSize(b))) { out.push(end(p, 'release', { why: 'far' })); continue; }
        orbitStep(a, b, p, dt, o);
      }
      if (pairs.length >= o.MAX_PAIRS || clock - lastCapture < (o.GAP_S || 0)) return out;
      /* New captures: pairs only, so a copy already spiralling (or just ended this step) is ignored. */
      const busy = new Set();
      for (const p of pairs) { busy.add(p.i); busy.add(p.j); }
      for (const e of out) { busy.add(e.i); busy.add(e.j); }
      for (let i = 0; i < bodies.length && pairs.length < o.MAX_PAIRS; i++) {
        const a = bodies[i];
        if (!a || a.free === false || busy.has(i) || cool.has(String(i))) continue;
        for (let j = i + 1; j < bodies.length; j++) {
          const b = bodies[j];
          if (!b || b.free === false || busy.has(j) || cool.has(String(j)) || cool.has(keyOf(i, j))) continue;
          if (!canCapture(a, b, o)) continue;
          const cx = (a.x + b.x) / 2, cy = (a.y + b.y) / 2;
          const L = (a.x - cx) * a.vy - (a.y - cy) * a.vx + (b.x - cx) * b.vy - (b.y - cy) * b.vx;
          pairs.push({ i, j, age: 0, sense: L < 0 ? -1 : 1, at: clock });
          busy.add(i); busy.add(j);
          out.push({ type: 'capture', i, j });
          lastCapture = clock;
          if (o.GAP_S > 0) return out;
          break;
        }
      }
      return out;
    },
  };
}
