/* sim/state.js - the kart state layout (FROZEN in Phase A, CONTRACT.md).
 *
 * One kart = one Float32Array of STATE_SIZE. Read and write fields by the index
 * constants below, never by position literals. The first part mirrors the local
 * fields of plan 3.2 in float form; the rest is sim-internal. Snapshots, render
 * interpolation and later ghosts are plain copies of this array.
 * Units: metres, seconds, radians, m/s. World axes: right-handed, +Y up.
 */
import { qslerp } from '../core/math.js';

const NAMES = [
  'posX', 'posY', 'posZ',             // world position of the kart origin (saucer base centre)
  'quatX', 'quatY', 'quatZ', 'quatW', // world orientation; local forward -Z, right +X, up +Y
  'velX', 'velY', 'velZ',             // world velocity, m/s
  'engineV',      // engine value, m/s (what velocity chases)
  'steerSlew',    // slewed steer, -1..1 (+1 = right)
  'driftStage',   // charge tier reached, 0..3 (0 = drifting with no tier yet, or not drifting)
  'driftDir',     // 0 not drifting, -1 left, +1 right
  'driftCharge',  // charge seconds (held time times the soft-drift modifier)
  'boostKind',    // BOOST.* below
  'boostT',       // seconds since the current boost started
  'airborne',     // 0 or 1
  'airTime',      // seconds since leaving the ground (kept until the next takeoff)
  'seg',          // node segment index: node seg -> node seg+1 (integer in float)
  'segT',         // 0..1 along that segment
  'lateral',      // metres from the centreline, + = right of travel
  'height',       // metres above the road surface along its normal (negative while dropping)
  'lap',          // completed laps
  'checkpoint',   // index into track.checkpoints of the last one crossed
  'latchSeg',     // last grounded segment, latched at takeoff (respawn anchor)
  'respawnPhase', // RESPAWN.* below
  'respawnT',     // seconds into the current respawn phase
  'edgeT',        // seconds spent past the edge grace distance with no ground
  'stalled',      // seconds of stall left (> 0 = stalled)
  'speedPct',     // forward speed / class top speed, 0..1.3
  // sim-internal
  'boostAmount',  // current boost size, fraction of top speed
  'boostHoldS',   // hold length of the current boost, s
  'hopT',         // seconds since the last hop (drives the hop gravity ease)
  'slipLowT',     // seconds the drift slip angle has stayed under the cancel threshold
  'yawRate',      // rad/s, + = turning right (camera drift bias, fx)
  's',            // arc length along the track centreline, 0..lengthM
  'sectionIdx',   // index into track.sections at s
  'grounded',     // 1 when a probe found road this tick
  'boostFromV',   // engineV when the current boost started (the rise starts here)
];

/** Field index constants, e.g. S.posX === 0. */
export const S = Object.freeze(Object.fromEntries(NAMES.map((n, i) => [n, i])));
export const FIELDS = Object.freeze(NAMES.slice());
export const STATE_SIZE = NAMES.length;

export const BOOST = Object.freeze({ NONE: 0, DRIFT1: 1, DRIFT2: 2, DRIFT3: 3, PAD: 4, LAUNCH: 5 });
export const RESPAWN = Object.freeze({ NONE: 0, PULL: 1, HOVER: 2, RELEASE: 3, GRACE: 4 });

/** A zeroed state with identity orientation. @returns {Float32Array} */
export function createState() {
  const s = new Float32Array(STATE_SIZE);
  s[S.quatW] = 1;
  return s;
}

/** dst <- src (same size). @returns {Float32Array} dst */
export function copyState(dst, src) { dst.set(src); return dst; }

/** A fresh copy. @returns {Float32Array} */
export function cloneState(src) { return Float32Array.from(src); }

/** Read [x, y, z] position into `out`. */
export function getPos(out, st) { out[0] = st[S.posX]; out[1] = st[S.posY]; out[2] = st[S.posZ]; return out; }
/** Read [x, y, z, w] orientation into `out`. */
export function getQuat(out, st) { out[0] = st[S.quatX]; out[1] = st[S.quatY]; out[2] = st[S.quatZ]; out[3] = st[S.quatW]; return out; }
/** Read [x, y, z] velocity into `out`. */
export function getVel(out, st) { out[0] = st[S.velX]; out[1] = st[S.velY]; out[2] = st[S.velZ]; return out; }
export function setPos(st, p) { st[S.posX] = p[0]; st[S.posY] = p[1]; st[S.posZ] = p[2]; return st; }
export function setQuat(st, q) { st[S.quatX] = q[0]; st[S.quatY] = q[1]; st[S.quatZ] = q[2]; st[S.quatW] = q[3]; return st; }
export function setVel(st, v) { st[S.velX] = v[0]; st[S.velY] = v[1]; st[S.velZ] = v[2]; return st; }

const qa = [0, 0, 0, 1], qb = [0, 0, 0, 1], qo = [0, 0, 0, 1];
/**
 * Render interpolation: out = b, with position, velocity, engineV, steerSlew, speedPct and
 * height lerped and orientation slerped from a toward b by alpha (0..1). Discrete fields
 * (seg, lap, phases, flags) come from b. A respawn teleport (> 20 m jump) snaps to b.
 * @returns {Float32Array} out
 */
export function lerpState(out, a, b, alpha) {
  out.set(b);
  const dx = b[S.posX] - a[S.posX], dy = b[S.posY] - a[S.posY], dz = b[S.posZ] - a[S.posZ];
  if (dx * dx + dy * dy + dz * dz > 400) return out;
  const k = 1 - alpha;
  for (const i of LERPED) out[i] = a[i] * k + b[i] * alpha;
  qslerp(qo, getQuat(qa, a), getQuat(qb, b), alpha);
  return setQuat(out, qo);
}
const LERPED = [S.posX, S.posY, S.posZ, S.velX, S.velY, S.velZ, S.engineV, S.steerSlew, S.speedPct, S.height, S.lateral];
