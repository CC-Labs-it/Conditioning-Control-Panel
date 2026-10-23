/* dev/hook.js - dev-only scripted drives for headless checks (?dev=1, polish 5 wave 1). No three.js, no DOM.
 *
 * Boot builds the hook only when the page url carries ?dev=1 (devAllowed) and never online; without the query
 * nothing here is imported into the frame loop's hands and the sim runs exactly as before. The api hangs on
 * window.__glassroad.dev (not enumerable):
 *   dev.drift({ holdS, release })   the player is put on the outside lane just before the next bend ahead,
 *                                   already drifting into it at tier 2 with tier 3 a few ticks away; the script
 *                                   holds hop and counter-steers, then lets go after holdS (default 1 s), at the
 *                                   bend end or near the inside rail: a tier 3 drift boost. release: false ends
 *                                   the script there with the slide still charged (the next hands decide).
 *   dev.drop({ leadM, speedPct, yawDeg })  the player is put leadM before the shortcut's opening (polish 6: the
 *                                   dare, side L of the coil's first turn), on its open side, nose yawDeg out and
 *                                   steered at the landing pad: kicker, flight, the glass catch on the lower road
 *                                   (route-land). Hands go back on the catch, a miss or a timeout.
 *   dev.stop()                      hands back at once.
 *   dev.status                      { kind, phase, ok, reason, s, tier, land: { clean, skipM } | null, events: [] }
 * A placement keeps the lap, sets the last checkpoint before the new spot (no lap credit for the jump) and
 * snaps the camera. Pure helpers (nextBend, placeKart, lastLineBefore, steerTo) are tested in
 * tests/sim-race-dev.test.mjs.
 */
import { S, RESPAWN, setPos, setQuat, setVel, copyState } from '../sim/state.js';
import { qfromBasis, qrotate, clamp, wrap } from '../core/math.js';
import { startDrift } from '../sim/drift.js';
import { resetRoute } from '../sim/routes.js';

/** True when the page search string asks for the dev hook (?dev=1). */
export function devAllowed(search) {
  try { return new URLSearchParams(String(search || '')).get('dev') === '1'; } catch (e) { return false; }
}

const frame = () => ({ p: [0, 0, 0], t: [0, 0, 1], n: [0, 1, 0], b: [1, 0, 0], w: 0 });
const F0 = frame(), F1 = frame();
const dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];

/** Signed heading change from s to s + spanM, radians (+ = a right turn: the tangent swings toward b). */
export function turnAt(track, s, spanM) {
  const a = track.frameAt(s, F0), b = track.frameAt(s + spanM, F1);
  // project the far tangent onto the near road plane, then read its angle against t (ahead) and b (right)
  const along = dot(b.t, a.t), right = dot(b.t, a.b);
  return Math.atan2(right, along);
}

/**
 * The first bend ahead of fromS: { s (entry), endS, dir (+1 right, -1 left), deg } or null.
 * A bend = heading change over spanM of at least minDeg; it ends where the change drops under half that.
 */
export function nextBend(track, fromS, { minAheadM = 20, spanM = 24, minDeg = 18, stepM = 4, maxScanM = 900 } = {}) {
  const L = track.lengthM, lim = minDeg * Math.PI / 180;
  for (let d = minAheadM; d <= Math.min(maxScanM, L); d += stepM) {
    const s = fromS + d, yaw = turnAt(track, s, spanM);
    if (Math.abs(yaw) < lim) continue;
    const dir = yaw > 0 ? 1 : -1;
    let e = s, most = Math.abs(yaw);
    while (e - s < 400) {
      e += stepM;
      const y = turnAt(track, e, spanM);
      if (Math.sign(y) !== dir || Math.abs(y) < lim / 2) break;
      most = Math.max(most, Math.abs(y));
    }
    return { s: wrap(s, L), endS: wrap(e + spanM * 0.5, L), dir, deg: Math.round(most * 180 / Math.PI) };
  }
  return null;
}

/** Index of the last checkline at or before s (0 before the first one past the start). */
export function lastLineBefore(track, s) {
  const lines = track.checklines || [];
  let k = 0;
  for (let i = 0; i < lines.length; i++) if (lines[i].s <= s) k = i;
  return k;
}

/**
 * Put a kart state on the road at s, lat metres right of the centreline, nose along the road (or yawDeg off it,
 * + = right), moving that way at speedMps. Keeps the lap; clears drift, boost, air and respawn. @returns {Float32Array} st
 */
export function placeKart(st, track, feel, s, lat, speedMps, yawDeg = 0) {
  const L = track.lengthM, sw = wrap(s, L), f = track.frameAt(sw, frame());
  const lap = st[S.lap];
  const c = Math.cos(yawDeg * Math.PI / 180), sn = Math.sin(yawDeg * Math.PI / 180);
  const t = [0, 1, 2].map((k) => f.t[k] * c + f.b[k] * sn), b = [0, 1, 2].map((k) => f.b[k] * c - f.t[k] * sn);
  st.fill(0);
  setPos(st, [f.p[0] + f.b[0] * lat, f.p[1] + f.b[1] * lat, f.p[2] + f.b[2] * lat]);
  setQuat(st, qfromBasis([0, 0, 0, 1], b, f.n, [-t[0], -t[1], -t[2]]));
  setVel(st, [t[0] * speedMps, t[1] * speedMps, t[2] * speedMps]);
  st[S.engineV] = speedMps;
  st[S.speedPct] = speedMps / feel.get('speed.topMps');
  st[S.seg] = f.seg; st[S.latchSeg] = f.seg; st[S.s] = sw; st[S.lateral] = lat;
  st[S.grounded] = 1; st[S.hopT] = 1e3;
  st[S.sectionIdx] = track.sectionAt(sw);
  st[S.lap] = lap; st[S.checkpoint] = lastLineBefore(track, sw);
  resetRoute(st);
  return st;
}

const FWD = [0, 0, -1], RIGHT = [1, 0, 0], q = [0, 0, 0, 1], fw = [0, 0, 0], rt = [0, 0, 0], TF = frame();
/** Stick (-1..1) that points the kart at the road point (s, lat). gain scales the heading error. */
export function steerTo(st, track, s, lat, gain = 2.2) {
  const f = track.frameAt(s, TF);
  q[0] = st[S.quatX]; q[1] = st[S.quatY]; q[2] = st[S.quatZ]; q[3] = st[S.quatW];
  qrotate(fw, q, FWD); qrotate(rt, q, RIGHT);
  const d = [f.p[0] + f.b[0] * lat - st[S.posX], f.p[1] + f.b[1] * lat - st[S.posY], f.p[2] + f.b[2] * lat - st[S.posZ]];
  return clamp(Math.atan2(dot(d, rt), Math.max(dot(d, fw), 0.1)) * gain, -1, 1);
}

/** Forward distance from a to b along a closed lap of length L, 0..L. */
const ahead = (a, b, L) => wrap(b - a, L);

/**
 * @param {{ track, feel, bus, player: () => object, race: () => object, emit: (n: string, p: object) => void,
 *   onPlace?: () => void, refuse?: () => string|null }} o
 */
export function createDevHook({ track, feel, bus, player, race, emit, onPlace = () => {}, refuse = () => null }) {
  const L = track.lengthM;
  const status = { kind: null, phase: 'idle', ok: true, reason: '', s: 0, tier: 0, land: null, events: [] };
  let job = null;
  const note = (name, p) => { if (job && status.events.length < 24) status.events.push(name + (p ? ' ' + p : '')); };
  const offs = bus ? [
    bus.on('drift-tier', ({ tier }) => { status.tier = tier; note('drift-tier', tier); }),
    bus.on('drift-boost', ({ tier }) => { note('drift-boost', tier); if (job && job.kind === 'drift') end('boost', true); }),
    bus.on('drift-cancel', ({ reason }) => { note('drift-cancel', reason); if (job && job.kind === 'drift') end('cancel ' + reason, false); }),
    bus.on('route-land', ({ clean, skipM }) => { status.land = { clean: !!clean, skipM }; note('route-land', clean ? 'clean' : 'rough'); if (job && job.kind === 'drop') end('caught', true); }),
    bus.on('drop', () => { note('drop'); if (job && job.kind === 'drop') end('missed', false); }),
  ] : [];

  function end(phase, ok) {
    status.phase = phase; status.ok = ok; job = null;
  }
  function begin(kind) {
    const why = refuse();
    const r = race();
    const no = why || (!r || r.phase !== 'racing' ? 'not racing' : null);
    delete status.bend; delete status.route;
    Object.assign(status, { kind, phase: no ? 'refused' : 'placed', ok: !no, reason: no || '', tier: 0, land: null, events: [] });
    return !no;
  }
  function put(s, lat, speed, yawDeg = 0) {
    const P = player();
    placeKart(P.st, track, feel, s, lat, speed, yawDeg);
    copyState(P.prev, P.st); copyState(P.view, P.st);
    if (P.progress && P.progress.reset) {
      // progress keeps its own last s: a reset puts it here, then the checkpoint and lap come back
      const lap = P.st[S.lap], cp = P.st[S.checkpoint];
      P.progress.reset(P.st); P.st[S.lap] = lap; P.st[S.checkpoint] = cp;
    }
    onPlace();
  }

  const api = {
    get status() { return status; },
    /** Tier 3 drift on the next bend ahead. */
    drift({ holdS = 0, release = true } = {}) {
      if (!begin('drift')) return status;
      const P = player(), top = feel.get('speed.topMps');
      const bend = nextBend(track, P.st[S.s]);
      if (!bend) { end('no bend', false); return status; }
      // the slide turns harder than Threshold's long bends, so it starts on the outside lane and lets go
      // before the inside rail (holdS, else 1 s, else the inside edge, whichever comes first)
      const half = track.frameAt(bend.s, frame()).w / 2;
      put(bend.s - 2, -bend.dir * half * 0.6, top * 0.92);
      const st = P.st, tiers = feel.get('drift.tierS');
      startDrift(st, bend.dir, { emit }, 'hop');
      st[S.driftStage] = 2;
      emit('drift-tier', { tier: 2, dir: bend.dir });
      st[S.driftCharge] = tiers[2] - 0.05;
      job = { kind: 'drift', t: 0, bend, half, holdS: holdS > 0 ? holdS : 1, release: release !== false };
      Object.assign(status, { phase: 'drifting', s: Math.round(bend.s), bend: { s: Math.round(bend.s), endS: Math.round(bend.endS), dir: bend.dir, deg: bend.deg } });
      return status;
    },
    /** The shortcut (polish 6: the dare, a gap off the outside of the coil's first turn): a cut, kicker, flight, catch. */
    drop({ leadM = 3, speedPct = 0.83, yawDeg = 35 } = {}) {
      if (!begin('drop')) return status;
      const r = track.routes && track.routes[0];
      if (!r) { end('no route', false); return status; }
      const side = r.side === 'L' ? -1 : 1, top = feel.get('speed.topMps');
      const half = track.frameAt(r.openS0, frame()).w / 2;
      // a few metres before the opening, already cutting out toward it (the line a brave player takes)
      put(r.openS0 - leadM, side * half * 0.25, top * speedPct, side * yawDeg);
      job = { kind: 'drop', t: 0, r, side, half, aimS: r.pad.s, aimX: r.pad.x || 0, flew: false };
      Object.assign(status, { phase: 'approach', s: Math.round(r.openS0 - leadM), route: r.id });
      return status;
    },
    stop() { if (job) end('stopped', true); },
    dispose() { offs.forEach((off) => off()); job = null; },
  };

  /** One sim step: script the player's hands while a job runs (a = the hands the sim is about to read). */
  function apply(a, dt) {
    if (!job) return false;
    const P = player(), st = P.st;
    job.t += dt;
    status.s = Math.round(st[S.s]);
    a.brake = 0; a.item = false; a.hopPressed = false;
    if (st[S.respawnPhase] !== RESPAWN.NONE) { end('missed', false); return false; }
    if (job.kind === 'drift') {
      const b = job.bend, past = ahead(b.s, st[S.s], L) >= ahead(b.s, b.endS, L) && ahead(b.s, st[S.s], L) < L / 2;
      const inside = st[S.lateral] * b.dir > job.half - 1.8;
      const due = job.t >= job.holdS || past || inside;
      a.throttle = 1;
      a.steer = steerTo(st, track, st[S.s] + 14, -b.dir * job.half * 0.3, 2.4);
      a.hop = !(job.release && due && job.t >= 0.25);
      if (!job.release && due) end('held', true);
      else if (job.t > 6) end('timeout', false);
      return true;
    }
    // drop: on the ground aim at the landing pad across the gap; in the air the glass catch steers
    const airborne = st[S.airborne] > 0;
    if (airborne) { job.flew = true; status.phase = 'flight'; }
    a.throttle = 1; a.hop = false;
    a.steer = airborne ? 0 : job.flew ? steerTo(st, track, st[S.s] + 14, 0) : steerTo(st, track, job.aimS, job.aimX, 1.6);
    if (job.t > 8) end('timeout', false);
    return true;
  }

  return { api, apply, get active() { return !!job; } };
}
