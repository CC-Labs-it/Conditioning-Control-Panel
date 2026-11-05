/* track/ramps.js - jump ramps (handoff-1030/GLASSROAD-RAMPS.md, the fixed contract). No three.js.
 *
 * Track JSON `ramps: [{ id, node, x, widthM, lenM, heightM, upMps, purpose, note }]`. `node` is the LIP (top edge):
 * the wedge rises over lenM metres of road BEFORE it, from the road surface to heightM at the lip, then a short
 * back face (RAMP_BACK_M) brings it down again, so a slow or reversing kart simply drives over it. The raise is
 * applied along the road normal by the ground query (wrapQueryWithRamps), so the sim, the bots, the room and the
 * render (render/ramps.js) all ride the same wedge; track.surfaceAt and the road mesh stay the bare road.
 * The launch itself lives in the sim (sim/ramps.js): a grounded kart moving forward faster than RAMP_LAUNCH_MIN_MPS
 * whose centre crosses the lip within the width leaves with velY = max(velY, upMps).
 * Every number here is plain arithmetic on authored values: canonical across engines (course hash safe).
 */
import { wrap } from '../core/math.js';

/** Bump when the wedge shape or the launch rule changes: it is part of the online runtime course hash. */
export const RAMP_REVISION = 1;
export const RAMP_PURPOSES = Object.freeze(['shortcut', 'item', 'cut', 'air']);
/** Authoring bounds (inclusive), the contract's ranges. */
export const RAMP_LIMITS = Object.freeze({ widthM: [4, 8], lenM: [5, 9], heightM: [0.8, 1.6], upMps: [11, 16] });
/** Back face run (m) behind the lip: steep, short, and only ever driven by karts too slow to launch. */
export const RAMP_BACK_M = 1.6;
/** Course-forward speed (m/s) a grounded kart needs at the lip to launch. */
export const RAMP_LAUNCH_MIN_MPS = 8;
/** A floating item box: liftM bounds (m above the road). */
export const LIFT_LIMITS = Object.freeze([0.5, 10]);

/** The footprint edge is inclusive; a millimetre of slack keeps an edge vertex (Float32 positions) on its own wedge. */
const FOOT_EPS_M = 1e-3;
const isNum = (v) => typeof v === 'number' && Number.isFinite(v);
const inside = (v, [a, b]) => isNum(v) && v >= a && v <= b;

/** Schema checks for `ramps` (track/schema.js calls this). err/warn take a message. */
export function validateRamps(t, nodes, err, warn) {
  if (t.ramps === undefined) return;
  if (!Array.isArray(t.ramps)) { err('ramps must be an array'); return; }
  const N = nodes.length, ids = new Set();
  t.ramps.forEach((r, i) => {
    const at = 'ramps[' + i + ']';
    if (!r || typeof r !== 'object') { err(at + ' not an object'); return; }
    if (typeof r.id !== 'string' || !/^[a-z0-9-]+$/.test(r.id) || ids.has(r.id)) err(at + '.id must be a unique lowercase id');
    else ids.add(r.id);
    if (!Number.isInteger(r.node) || r.node < 0 || r.node >= N) { err(at + '.node out of range'); return; }
    for (const k of Object.keys(RAMP_LIMITS)) if (!inside(r[k], RAMP_LIMITS[k])) err(at + '.' + k + ' must be ' + RAMP_LIMITS[k].join('..'));
    if (!RAMP_PURPOSES.includes(r.purpose)) err(at + '.purpose must be one of ' + RAMP_PURPOSES.join('|'));
    if (r.note !== undefined && typeof r.note !== 'string') err(at + '.note must be a string');
    if (!isNum(r.x) || !isNum(r.widthM)) { err(at + ' needs x'); return; }
    if (Math.abs(r.x) + r.widthM / 2 > nodes[r.node].w / 2) err(at + ' leaves the road at its lip');
    // the wedge and its back face must sit on solid road: no jump gap under any of it
    for (let k = -1; k <= 1; k++) {
      const n = nodes[((r.node + k) % N + N) % N];
      if (n && Array.isArray(n.flags) && n.flags.includes('jump')) { err(at + ' sits on a jump gap'); break; }
    }
    if (nodes[r.node].flags?.includes('magnet')) err(at + ' sits on a magnet (loop) node');
  });
}

/** Schema check for one items[] entry's liftM. */
export function validateLift(e, at, err) {
  if (e.liftM !== undefined && !inside(e.liftM, LIFT_LIMITS)) err(at + '.liftM must be ' + LIFT_LIMITS.join('..') + ' m');
}

/** Runtime rows: the authored values plus lipS (the lip's arc length) and s0 (the wedge foot). */
export function buildRamps(list, nodeS, lengthM) {
  return (list || []).map((r, idx) => {
    const lipS = nodeS[r.node];
    return { idx, id: r.id, node: r.node, x: r.x, widthM: r.widthM, lenM: r.lenM, heightM: r.heightM, upMps: r.upMps,
      purpose: r.purpose, lipS, s0: wrap(lipS - r.lenM, lengthM), backM: RAMP_BACK_M };
  });
}

/** Signed arc distance from s to the lip, wrapped into (-L/2, L/2]: positive = the lip is ahead. */
export const toLip = (ramp, s, L) => { const d = wrap(ramp.lipS - s, L); return d > L / 2 ? d - L : d; };

/** The wedge height (m above the road) of one ramp at (s, lat); 0 outside its footprint. */
export function rampHeight(ramp, s, lat, L) {
  if (Math.abs(lat - ramp.x) > ramp.widthM / 2 + FOOT_EPS_M) return 0;
  const d = toLip(ramp, s, L);
  if (d >= 0 && d <= ramp.lenM) return ramp.heightM * (1 - d / ramp.lenM);
  if (d < 0 && d > -ramp.backM) return ramp.heightM * (1 + d / ramp.backM);
  return 0;
}

/** The ramp under (s, lat) and its wedge height, written into out ({ ramp, h }); out.ramp null when none. */
export function rampAt(ramps, s, lat, L, out = { ramp: null, h: 0 }) {
  out.ramp = null; out.h = 0;
  for (let i = 0; i < ramps.length; i++) {
    const h = rampHeight(ramps[i], s, lat, L);
    if (h > out.h) { out.h = h; out.ramp = ramps[i]; }
  }
  return out;
}

const HIT = { ramp: null, h: 0 };
/** Lift a query result onto a wedge in place: p moves up the normal, height drops by the wedge, `ramp`/`rampH` set. */
function lift(out, ramps, L) {
  out.ramp = null; out.rampH = 0;
  if (!out || out.forkId || !out.onRoad) return out;
  rampAt(ramps, out.s, out.lateral, L, HIT);
  if (!HIT.ramp) return out;
  const n = out.normal, h = HIT.h;
  out.p[0] += n[0] * h; out.p[1] += n[1] * h; out.p[2] += n[2] * h;
  out.height -= h;
  out.ramp = HIT.ramp; out.rampH = h;
  return out;
}

/**
 * Wrap the base ground query (and its below()) so every result over a wedge stands on it. Without ramps the
 * query is returned untouched (byte-identical behaviour for courses that have none).
 */
export function wrapQueryWithRamps(query, ramps, L) {
  if (!ramps || !ramps.length) return query;
  const wrapped = function rampQuery(px, py, pz, hintSeg, out) { return lift(query(px, py, pz, hintSeg, out), ramps, L); };
  for (const k of Object.keys(query)) wrapped[k] = query[k];
  if (query.below) wrapped.below = function rampBelow(px, py, pz, out) { const q = query.below(px, py, pz, out); return q ? lift(q, ramps, L) : q; };
  return wrapped;
}
