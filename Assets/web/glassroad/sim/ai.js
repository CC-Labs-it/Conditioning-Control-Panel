/* sim/ai.js - the EMI bots (CONTRACT-B1.md section 3).
 *
 * Each bot drives the real sim through the same Actions a player makes; it never moves itself, so it
 * cannot cheat through a wall. Steering is pure pursuit on a point ahead of the kart (ai.lookAheadS of
 * speed, at least lookAheadMinM) at the bot's own line: a lateral offset (lineSpreadM), a slow wander,
 * a corner cut toward the inside, a shift around slower karts and traps, clamped clear of open edges by
 * openEdgeMarginM. Throttle lifts and brakes where the bend ahead asks more yaw than the kart has.
 * Hop drifts on long bends (never near an open edge or a jump), held at least driftMinS, released for
 * the boost. The rubber band is soft and only on top speed. mods.blind adds steer noise and lifts the
 * throttle. Items through the ITEMS API (input.item edge, input.back), less often than people.
 * Personalities and the few numbers feel.json lacks sit in GAPS below (reported as feel gaps). No three.js.
 * Water (wave 1): the track's flow and stream / pool kinds are sampled per 2 m at build from track.query; the
 * line aims ai.water.leadS seconds of lateral flow against the push, takes an inside or outside line on the
 * stream bend (per persona and seed), holds a chosen lane from ai.water.lipLeadM before the chute with a
 * small wheel down it, never drifts on water, never counts a pool as stuck (no rescue there).
 * Stepping stones (wave 2): a field plan next to the court plan, one per row of track.fields. ai.stones.commitM
 * before a field the bot picks the lane nearest its own lateral (the persona's inside bias pulls a near tie to the
 * centre lane), eases onto it and holds that x through the field: no lane change, wander, drift, steer noise or
 * lift from holdLeadM before the field to its end (vmax never under ai.stones.minMps), no steer in the air over a
 * gap, the plan dropped leaveM after the field.
 */
import { S, BOOST, RESPAWN } from './state.js';
import { clamp, lerp, wrap, damp, smoothstep } from '../core/math.js';
import { applyBoost } from './boost.js';
import { gap } from './gaps.js';
import { ITEMS } from './itemTable.js';
import { bendSpeed, yawParams, calmOf } from './steer.js';
import { createCourtPaths } from './court-paths.js';
import { makeQuery } from '../track/query.js';
import { stoneIn, stoneLaneSpan } from '../track/courts.js';

const YP = {};                       // steering law numbers, read once per bot per tick

// feel gap: AI numbers feel.json does not carry yet (read through feel first)
const GAPS = {
  'ai.railMarginM': 1.3,       // keep this far off a railed edge
  'ai.cornerMargin': 0.8,      // share of the kart's yaw rate the bot plans a bend with
  'ai.jumpGuardM': 45,         // no hops, drifts or brakes this far before a jump lip
  'ai.driftCoolS': 1.2,        // gap between two drifts
  'ai.driftMaxS': 3.2,         // longest drift
  'ai.stuckS': 1.6,            // slower than stuckPct this long while racing: back out
  'ai.stuckPct': 0.08,
  'ai.reverseS': 0.9,
  'ai.rubberTcS': 1.5,         // rubber band ease
  'ai.driftLeadS': 0.6,        // a drift looks this far ahead at its own sideways speed
  'ai.driftIntoDeg': 40,       // charging steers into the drift until the nose is this far in
  'ai.driftMaxDeg': 55,        // and lets go past this
  'ai.driftWideM': 4,          // how wide a bot sets up for a drift
  'ai.driftBankTolDeg': 4,     // bank change allowed over a drift
  'ai.routeChance': 0.2,      // polish 4: chance per lap a tier 2 bot tries a drop shortcut (tier 1: x routeLowMul)
  'ai.routeLowMul': 0,
  'ai.routeHopLeadM': 3,       // hops this far before the opening, once the line has it at the kerb
  'ai.routeLeadM': 70,         // starts lining up for the open edge this far before it
  'ai.routeAimAheadM': 25,    // aims at a point this far past the landing pad: a straight line off the open edge
  'ai.routeHopEdgeM': 2.2,     // hops the open edge's kerb once this close to it
  'ai.routeLineM': 2.8,          // the line runs this far to the open side of the landing pad
  'ai.trickOpenM': 120,        // polish 6: no trick (its landing boost) with an open edge this close ahead
  'ai.padRiskM': 140,          // polish 6: a pad with an open bend this close after it
  'ai.padRiskPct': 0.85,       // that bend's planned speed under this share of top: the bots steer round the pad
  // water (wave 1): the bots read the track's flow (query.flow, sampled per 2 m at build) and its stream / pool nodes
  'ai.water.leadS': 0.8,       // aim this many seconds of the lateral flow ahead against the push
  'ai.water.lineM': 3.5,       // the inside or outside line on a stream bend sits this far off the centre
  'ai.water.lipLeadM': 40,     // hold the chute lane from this far before the lip
  'ai.water.laneM': 3.5,       // the chute lanes: left / centre / right, this far apart
  'ai.water.chuteSteerMax': 0.3,   // the wheel stays within this on the chute
  'ai.water.bendDegPerM': 0.25,    // a stream run turning more than this on average is the stream bend
  'ai.water.chuteDownDeg': 3,      // a straight stream run dropping more than this on average is the chute
  // stepping stones (wave 2): a field plan next to the court plan, one per row of track.fields ([] = none)
  'ai.stones.approachM': 60,   // the line starts easing toward the nearest safe lane this far before a field (the court's LEAD)
  'ai.stones.blendM': 15,      // the line is fully on the (provisional) lane this far into the approach: 45 m of straight running before the deck edge
  'ai.stones.commitM': 25,     // the lane nearest the kart's own lateral is confirmed this far before a field (a late commit, as the court's)
  'ai.stones.minMps': 16,      // the planned speed never drops under this inside the field and holdLeadM before it
  'ai.stones.laneHoldS': 1.5,  // a picked lane is held at least this long; before the field a bot knocked more than 2 m off it may pick again, inside never
  'ai.stones.holdLeadM': 20,   // no drift, steer noise or lift from this far before the field to its end
  'ai.stones.leaveM': 6,       // the plan is dropped this far after the field
  'ai.stones.settleS': 0.45,   // the lane target sits short of the lane by this much of the sideways speed: the bot arrives with the nose straight
};
// feel.get throws on a missing key, and a feel never gains keys (set refuses them), so a miss is
// remembered per feel: no throw per bot per tick.
const MISSING = new WeakMap();
const fg = (feel, k) => {
  const miss = MISSING.get(feel);
  if (miss && miss.has(k)) return GAPS[k];
  try { return feel.get(k); } catch (e) {
    if (miss) miss.add(k); else MISSING.set(feel, new Set([k]));
    return GAPS[k];
  }
};

// feel gap: personalities (multipliers on the ai.* numbers; inside = chance of the inside line on the stream bend)
export const PERSONAS = Object.freeze([
  { id: 'steady', spread: 0.5, cut: 0.8, drift: 0.5, tier: 1, item: 0.8, wander: 0.6, inside: 0.75 },
  { id: 'drifter', spread: 0.8, cut: 1.0, drift: 1.0, tier: 2, item: 0.9, wander: 0.8, inside: 0.35 },
  { id: 'thrower', spread: 0.7, cut: 0.9, drift: 0.6, tier: 1, item: 1.4, wander: 1.0, inside: 0.5 },
  { id: 'cutter', spread: 1.0, cut: 1.3, drift: 0.8, tier: 2, item: 1.0, wander: 0.7, inside: 0.9 },
]);

/** Seeded 0..1 generator (mulberry32). */
export function rng(seed) {
  let a = (seed >>> 0) || 1;
  return () => {
    a = (a + 0x6D2B79F5) >>> 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

const STEP = 2;                      // m between table samples
const FR = { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] }, FR0 = { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] };
const NO_TRAPS = Object.freeze([]);
const RT_AIM = [0, 0, 0];

/** The live traps as rows with s and x: the items' cheap query when it has one, else its view. */
function trapsOf(items) {
  if (!items) return NO_TRAPS;
  if (typeof items.trapRows === 'function') return items.trapRows() || NO_TRAPS;
  const v = typeof items.view === 'function' ? items.view() : null;
  return (v && v.traps) || NO_TRAPS;
}

export function createAi(track, feel, { seed = 1, forkChoice = null } = {}) {
  const courts = createCourtPaths(track);
  const L = track.lengthM, N = Math.ceil(L / STEP);
  // per 2 m: signed bend (deg/m, + right), half width, open left / right, gap
  const bend = new Float32Array(N), half = new Float32Array(N), openL = new Uint8Array(N), openR = new Uint8Array(N), gapAt = new Uint8Array(N), roll = new Float32Array(N);
  {
    const A = { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] }, B = { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] };
    const W = 8;
    for (let k = 0; k < N; k++) {
      const s = k * STEP;
      track.frameAt(s - W / 2, A); track.frameAt(s + W / 2, B);
      // the turn in the road's own plane (polish 6): a loop or a crest pitches the road but is no bend to steer or brake for
      const d = A.t[0] * B.t[0] + A.t[1] * B.t[1] + A.t[2] * B.t[2];
      const side = B.t[0] * A.b[0] + B.t[1] * A.b[1] + B.t[2] * A.b[2];
      bend[k] = Math.atan2(side, Math.abs(d)) * 180 / Math.PI / W;
      const f = track.frameAt(s, A), fl = f.flags || [];
      half[k] = f.w / 2; roll[k] = f.roll || 0;
      const rail = fl.includes('rail');
      openL[k] = rail || fl.includes('railL') ? 0 : 1;
      openR[k] = rail || fl.includes('railR') ? 0 : 1;
      gapAt[k] = fl.includes('jump') ? 1 : 0;
    }
  }
  const at = (s) => (Math.floor(wrap(s, L) / STEP)) % N;
  const lips = (track.jumps || []).map((j) => j.s);
  const lipAhead = (s, m) => lips.some((ls) => wrap(ls - s, L) <= m);
  const openAhead = (s, m) => { for (let d = 0; d <= m; d += STEP) { const k = at(s + d); if (openL[k] || openR[k]) return true; } return false; };
  // boost pads followed (within ai.padRiskM) by an open bend tighter than ai.padRiskPct of top: the bots skip them
  const padRisk = (track.boosts || []).map((pd) => {
    let risky = false;
    if (typeof track.frameAt === 'function') {
      yawParams(feel, YP);
      const lim = fg(feel, 'ai.padRiskPct') * feel.get('speed.topMps');
      for (let d = 0; d <= fg(feel, 'ai.padRiskM') && !risky; d += STEP) {
        const k = at(pd.s + d);
        if ((openL[k] || openR[k]) && bendSpeed(bend[k], YP, fg(feel, 'ai.cornerMargin')) < lim) risky = true;
      }
    }
    return { s: pd.s, x: pd.x, w: pd.w, risky };
  });

  // water (wave 1): the ground's flow and kind per 2 m, read once from the track query on the centreline
  // (a track with no flow yet, or no query, reads as dry); then the stream runs, each classed once:
  // 'bend' (the stream bend, side + right), 'chute' (a straight stream dropping away), 'stream' (flat), 'pool'
  const flowA = new Float32Array(N), flowL = new Float32Array(N), surf = new Uint8Array(N), down = new Float32Array(N);
  const runs = [];
  if (typeof track.query === 'function' && typeof track.frameAt === 'function') {
    const Q = makeQuery(), F = { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] };
    for (let k = 0; k < N; k++) {
      const f = track.frameAt(k * STEP, F);
      down[k] = Math.asin(clamp(-f.t[1], -1, 1)) * 180 / Math.PI;
      const q = track.query(f.p[0], f.p[1], f.p[2], f.seg, Q);
      if (!q) continue;
      surf[k] = q.surface === 'stream' ? 1 : q.surface === 'pool' ? 2 : 0;
      const fl = surf[k] === 1 && Array.isArray(q.flow) ? q.flow : null;
      flowA[k] = fl && Number.isFinite(fl[0]) ? fl[0] : 0;
      flowL[k] = fl && Number.isFinite(fl[1]) ? fl[1] : 0;
    }
    let k0 = 0;
    while (k0 < N && surf[k0] !== 0) k0++;          // start on dry ground so a run across s = 0 stays whole
    if (k0 < N) for (let i = 0; i < N;) {
      const k = (k0 + i) % N, kind = surf[k];
      if (!kind) { i++; continue; }
      let n = 0, sumBend = 0, sumDown = 0;
      while (n < N - i && surf[(k0 + i + n) % N] === kind) { const kk = (k0 + i + n) % N; sumBend += bend[kk]; sumDown += down[kk]; n++; }
      const s0 = wrap(k * STEP, L), meanBend = sumBend / n, meanDown = sumDown / n;
      const cls = kind === 2 ? 'pool' : Math.abs(meanBend) >= fg(feel, 'ai.water.bendDegPerM') ? 'bend'
        : meanDown >= fg(feel, 'ai.water.chuteDownDeg') ? 'chute' : 'stream';
      runs.push({ kind: cls, s0, s1: s0 + n * STEP, side: cls === 'bend' ? Math.sign(meanBend) : 0 });
      i += n;
    }
  }
  /** The water run under s (lead metres before it count too), or null. */
  function runAt(s, lead = 0) {
    for (let i = 0; i < runs.length; i++) { const r = runs[i], d = wrap(s - r.s0 + lead, L); if (d < r.s1 - r.s0 + lead) return r; }
    return null;
  }

  // stepping stones (wave 2): one plan per field (track.fields, [] or undefined = none). The lanes are sorted and
  // kept 1 m inside the road at the field start (the outer margins are void); the stones themselves sit on the lanes
  // by construction, so inside a plan the lane is the line and the open-edge band is not consulted.
  const fields = (track.fields || []).filter((f) => f && Array.isArray(f.lanes) && f.lanes.length && Number.isFinite(f.fromS) && Number.isFinite(f.toS) && f.toS > f.fromS)
    .map((f) => {
      const hw = typeof track.frameAt === 'function' ? track.frameAt(f.fromS, FR0).w / 2 : Infinity;
      const lanes = f.lanes.map((x) => clamp(x, -hw + 1, hw - 1)).sort((a, b) => a - b);
      let centre = lanes[0];
      for (const x of lanes) if (Math.abs(x) < Math.abs(centre)) centre = x;
      // the safe lanes (a safe line always): the deck has no lip, so the entry void must be short enough to coast over
      // at the design speed without the kick (snap height), every gap along the lane and the exit void within the kick's
      // reach (stones.clearGapM); a field where no lane qualifies keeps them all
      const feelOr = (k, d) => { try { return feel.get(k); } catch (e) { return d; } };
      const clearM = feelOr('stones.clearGapM', 4) + 0.5, v = feelOr('stones.minClearMps', 14);
      const entryM = Math.sqrt(2 * gap(feel, 'kart.snapM') / feelOr('hop.gravityMps2', 28)) * v;
      const safe = f.lanes.filter((lx) => {
        const rows = (f.stones || []).filter((st) => st && !st.spin && Number.isFinite(st.s))
          .map((st) => stoneLaneSpan(st, lx, st.shape === 'hex' ? 0.7 : 0)).filter(Boolean).sort((a, b) => a[0] - b[0]);
        if (!rows.length) return false;
        if (rows[0][0] - f.fromS > entryM) return false;
        let end = rows[0][1];
        for (let i = 1; i < rows.length; i++) { if (rows[i][0] - end > clearM) return false; end = Math.max(end, rows[i][1]); }
        return f.toS - end <= clearM;
      }).map((x) => clamp(x, -hw + 1, hw - 1));
      return { field: f, fromS: f.fromS, toS: f.toS, lanes, centre, safe: safe.length ? safe : lanes };
    });
  /** The field plan covering s, from approachM before its start to leaveM after its end, or null. */
  function fieldAt(s) {
    const lead = fg(feel, 'ai.stones.approachM'), tail = fg(feel, 'ai.stones.leaveM');
    for (let i = 0; i < fields.length; i++) { const p = fields[i]; if (wrap(s - (p.fromS - lead), L) <= p.toS - p.fromS + lead + tail) return p; }
    return null;
  }
  /** The safe lane nearest x; the persona's inside bias pulls a near tie toward the centre lane. */
  function pickLane(p, x, persona) {
    let best = p.safe[0], bd = Infinity;
    for (let i = 0; i < p.safe.length; i++) {
      const lx = p.safe[i], d = Math.abs(lx - x) - (lx === p.centre ? persona.inside * 1.2 : 0);
      if (d < bd) { bd = d; best = lx; }
    }
    return best;
  }

  const bots = new Map();

  function bot(i) {
    let b = bots.get(i);
    if (!b) {
      const rand = rng(seed * 7919 + i * 104729);
      const persona = i === 0 ? PERSONAS[0] : PERSONAS[(i + seed) % PERSONAS.length];
      b = { rand, persona, line: (rand() * 2 - 1), phase: rand() * Math.PI * 2, n1: rand() * 6.28, n2: rand() * 6.28,
        t: 0, driftT: -1, driftCool: 0, driftDir: 0, holdT: 0, itemCool: 0, stuckT: 0, reverseT: 0,
        launched: false, launch: rand(), setupSide: 0, wantDrift: false, why: {}, routeLap: -1, route: null, routes: 0,
        waterInside: false, chuteLane: 0, field: null };
      // water picks on their own stream (the drift / item / launch rolls keep their sequence)
      const wr = rng(seed * 7919 + i * 104729 + 1013904223);
      b.waterInside = wr() < persona.inside;
      b.chuteLane = Math.floor(wr() * 3) - 1;
      bots.set(i, b);
    }
    return b;
  }

  function reset() { bots.clear(); }

  /** The lateral band (lo..hi) clear of rails and open edges over the next m metres. */
  function lineLimits(s, m, out) {
    out.lo = -Infinity; out.hi = Infinity;
    const em = feel.get('ai.openEdgeMarginM'), rm = fg(feel, 'ai.railMarginM');
    for (let d = 0; d <= m; d += STEP) {
      const k = at(s + d);
      if (gapAt[k]) continue;
      out.hi = Math.min(out.hi, half[k] - (openR[k] ? em : rm));
      out.lo = Math.max(out.lo, -half[k] + (openL[k] ? em : rm));
    }
    if (out.lo > out.hi) { const c = (out.lo + out.hi) / 2; out.lo = out.hi = c; }
    return out;
  }
  const LIM = { lo: 0, hi: 0 };

  // Split roads use their physical distance for pursuit. No position or progress writes.
  const forkProbe = makeQuery();
  function forkDrive(r, b, ctx, dt, out) {
    if (!track.forks?.length) return false;
    const st = r.st, held = track.forkAtSeg(st[S.seg]), lap = st[S.lap] | 0, s = st[S.s];
    if (held) b.fork = held;
    if (b.fork && !held && (s > b.fork.toS - 2 || s < b.fork.fromS - 65 || s > b.fork.fromS + b.fork.mouthM + 30)) b.fork = null;
    if (!b.fork) for (const f of track.forks) {
      // The permanent bypass is the bot route; timed bridges remain an optional player gamble.
      if (f.movingBridge) continue;
      if (s < f.fromS - 60 || s > f.fromS + 4) continue;
      const choose = typeof forkChoice === 'function' ? forkChoice({ fork: f, racer: r, lap, seed })
        : forkChoice === 'all' || forkChoice === f.id || (forkChoice !== 'none' && forkChoice === null && (r.i + f.index + lap + seed) % 3 === 0);
      if (choose) { b.fork = f; break; }
    }
    const f = b.fork;
    if (!f) return false;
    const local = held ? f.localS(s) : f.query(st[S.posX], st[S.posY], st[S.posZ], st[S.seg], forkProbe).forkS;
    const speed = Math.hypot(st[S.velX], st[S.velY], st[S.velZ]), look = clamp(speed * .65, 10, 19);
    let aim;
    if (!held && s < f.fromS - look) aim = track.frameAt(s + look).p;
    else if (local + look > f.lengthM) aim = track.frameAt(f.toS + local + look - f.lengthM).p;
    else aim = f.frameAt(local + look).p;
    const tx = aim[0] - st[S.posX], ty = aim[1] - st[S.posY], tz = aim[2] - st[S.posZ];
    const qx = st[S.quatX], qy = st[S.quatY], qz = st[S.quatZ], qw = st[S.quatW];
    const rx = 1 - 2 * (qy * qy + qz * qz), ry = 2 * (qx * qy + qw * qz), rz = 2 * (qx * qz - qw * qy);
    const fx = -2 * (qx * qz + qw * qy), fy = -2 * (qy * qz - qw * qx), fz = -(1 - 2 * (qx * qx + qy * qy));
    const x = tx * rx + ty * ry + tz * rz, z = tx * fx + ty * fy + tz * fz;
    yawParams(r.feel, YP);
    const soft = Math.max(.2, calmOf(speed, YP) * YP.user);
    out.steer = clamp(Math.atan2(x, Math.max(z, .1)) * feel.get('ai.steerGain') / soft, -1, 1);
    let vmax = 28;
    for (let d = 0; d < 50; d += 8) {
      const a = f.frameAt(local + d), c = f.frameAt(local + d + 6);
      const angle = Math.acos(clamp(a.t[0]*c.t[0]+a.t[1]*c.t[1]+a.t[2]*c.t[2], -1, 1)) * 180 / Math.PI / 6;
      const v = bendSpeed(angle, YP, .65);
      vmax = Math.min(vmax, Math.sqrt(v*v + 2 * feel.get('engine.coastMps2') * d));
    }
    out.throttle = speed < vmax ? 1 : 0;
    out.brake = speed > vmax + 2 ? clamp((speed - vmax) / 5, 0, 1) : 0;
    b.driftT = -1; b.route = null; b.field = null; b.courtRoute = null;
    if (b.reverseT > 0) { b.reverseT -= dt; out.throttle = 0; out.brake = 1; out.steer = -out.steer; }
    else if (speed < 2 && st[S.respawnPhase] === RESPAWN.NONE) {
      b.stuckT += dt; if (b.stuckT > 2) { b.stuckT = 0; b.reverseT = .8; }
    } else b.stuckT = 0;
    b.why = { fork: f.id, localS: local, targetMps: vmax };
    if (r.item) itemLogic(r, b, ctx, dt, out);
    return true;
  }

  function drive(r, ctx, dt, out) {
    out.steer = 0; out.throttle = 0; out.brake = 0; out.hop = false; out.hopPressed = false; out.pause = false; out.item = false; out.back = false;
    const race = ctx && ctx.race, st = r.st, b = bot(r.i);
    if (!race || (race.phase !== 'racing' && race.phase !== 'finished')) return;
    b.t += dt;
    const ph = st[S.respawnPhase];
    if (ph === RESPAWN.PULL || ph === RESPAWN.HOVER || ph === RESPAWN.RELEASE) {
      b.courtRoute = null; b.field = null;
      b.driftT = -1; b.reverseT = 0; b.stuckT = 0; out.throttle = 1; return;
    }
    if (forkDrive(r, b, ctx, dt, out)) return;
    // The long authored launches use the same finite cap as a player, without lyric support.
    const aerial = (track.aerials || []).find(a => st[S.s] >= a.rampS0 && st[S.s] <= a.landingS1);
    if (aerial && !track.forkAtSeg?.(st[S.seg])) {
      const speed = Math.hypot(st[S.velX], st[S.velZ]), f = track.frameAt(st[S.s] + Math.max(12, speed * .6), FR);
      const dx = f.p[0] - st[S.posX], dy = f.p[1] - st[S.posY], dz = f.p[2] - st[S.posZ];
      const qx = st[S.quatX], qy = st[S.quatY], qz = st[S.quatZ], qw = st[S.quatW];
      const x = dx * (1 - 2 * (qy * qy + qz * qz)) + dy * 2 * (qx * qy + qw * qz) + dz * 2 * (qx * qz - qw * qy);
      const z = -dx * 2 * (qx * qz + qw * qy) - dy * 2 * (qy * qz - qw * qx) - dz * (1 - 2 * (qx * qx + qy * qy));
      out.throttle = 1; out.hop = !!st[S.airborne];
      yawParams(r.feel, YP);
      const soft = Math.max(.2, calmOf(speed, YP) * YP.user);
      out.steer = st[S.airborne] ? 0 : clamp(Math.atan2(x, Math.max(.1, z)) * feel.get('ai.steerGain') / soft, -1, 1);
      b.driftT = -1; b.reverseT = 0; b.stuckT = 0;
      if (r.item) itemLogic(r, b, ctx, dt, out);
      return;
    }

    // launch at go, by chance (the player's launch is race.js's)
    if (!b.launched && r.meta && r.meta.isBot) {
      b.launched = true;
      if (b.launch < feel.get('ai.launchChance') && race.timeS < 0.25 && r.events) {
        const tier = feel.get('boost.launchTier');
        applyBoost(st, BOOST.LAUNCH, tier, r.feel, r.events);
        r.events.emit('launch-boost', { tier });
      }
    }

    // rubber band (soft, top speed only, bots only, off once the player is home)
    const player = ctx.racers && ctx.racers[0];
    let want = 1;
    if (r.meta && r.meta.isBot && player && !player.finished) {
      const d = player.progressM - r.progressM;
      want = d > 0 ? lerp(1, feel.get('ai.rubberUpMul'), clamp(d / feel.get('ai.rubberBehindM'), 0, 1))
        : lerp(1, feel.get('ai.rubberDownMul'), clamp(-d / feel.get('ai.rubberAheadM'), 0, 1));
    }
    r.rubber = damp(r.rubber || 1, want, fg(feel, 'ai.rubberTcS'), dt);

    const top = r.feel.get('speed.topMps');
    const s = st[S.s], lat = st[S.lateral];
    const speed = Math.max(0, st[S.speedPct]) * top;
    let la = Math.max(feel.get('ai.lookAheadMinM'), speed * feel.get('ai.lookAheadS'));
    const p = b.persona, blind = r.mods ? clamp(r.mods.blind, 0, 1) : 0;
    const court = courts.at(s), lap = st[S.lap] | 0;
    if (court && s >= court.court.fromS) la = Math.min(la, 12);
    if (court && (!b.courtRoute || b.courtRoute.plan !== court || b.courtLap !== lap)) {
      b.courtRoute = courts.choose(court, seed, r.i, lap, s >= court.first - 15 ? lat : null); b.courtLap = lap;
      b.courtCommitted = s >= court.first - 15;
    } else if (!court) b.courtRoute = null;
    // The preceding bend or a boost may prevent the planned approach. Commit to the line
    // actually reached before the first opening, rather than crossing an aperture to chase a preference.
    if (b.courtRoute && !b.courtCommitted && s >= court.court.fromS + 1) {
      const side = track.frameAt(s, FR0).b;
      const lateralV = st[S.velX] * side[0] + st[S.velY] * side[1] + st[S.velZ] * side[2];
      b.courtRoute = courts.choose(court, seed, r.i, lap, lat + clamp(lateralV * 0.6, -4, 4)); b.courtCommitted = true;
    }
    const nearLip = lipAhead(s, fg(feel, 'ai.jumpGuardM'));
    // stepping stones (wave 2): from approachM out the line eases toward the nearest safe lane; at commitM the lane is
    // confirmed from where the kart is and where it slides (the court's late-commit rule) and held through the field
    // and leaveM past it. Before the commit a bot knocked more than 2 m off its lane (a bump) may pick again once
    // laneHoldS has run; after the commit never.
    const fp = fieldAt(s);
    if (!fp) b.field = null;
    else {
      const slide = clamp((b.latV || 0) * 0.6, -3, 3), commitS = fp.fromS - fg(feel, 'ai.stones.commitM');
      if (!b.field || b.field.plan !== fp) b.field = { plan: fp, lane: pickLane(fp, lat + slide, p), t: 0, committed: false };
      else b.field.t += dt;
      // At commitment the real footprint wins over projected drift, which can request an unreachable late weave.
      if (!b.field.committed && s >= commitS) { b.field.lane = pickLane(fp, lat, p); b.field.committed = true; b.field.t = 0; }
      else if (!b.field.committed && b.field.t >= fg(feel, 'ai.stones.laneHoldS') && Math.abs(lat - b.field.lane) > 2) {
        b.field.lane = pickLane(fp, lat + slide, p); b.field.t = 0;
      }
    }
    const inField = !!fp && s >= fp.fromS && s < fp.toS;
    const inHold = inField || (!!fp && s >= fp.fromS - fg(feel, 'ai.stones.holdLeadM') && s < fp.toS);

    // polish 4: a drop shortcut, rolled once a lap as the bot nears its open edge
    if (routeDrive(r, b, ctx, dt, out, s, speed, blind)) return;

    // the line
    const wander = feel.get('ai.lineWanderM') * p.wander * Math.sin(2 * Math.PI * b.t / feel.get('ai.lineWanderS') + b.phase);
    const bendLa = bend[at(s + la)];
    const cutK = clamp(Math.abs(bendLa) / feel.get('ai.driftCurveDegPerM'), 0, 1);
    let target = b.line * feel.get('ai.lineSpreadM') * p.spread + wander + Math.sign(bendLa) * feel.get('ai.cornerCutM') * p.cut * cutK;
    if (nearLip) target *= 0.3;
    // water: the run under the kart (the chute counts from lipLeadM before its lip); no drift on water
    const kWater = at(s), onWater = surf[kWater] !== 0;
    const run = runAt(s, 0), chute = (run && run.kind === 'chute') ? run : runAt(s, fg(feel, 'ai.water.lipLeadM'));
    const onChute = !!chute && chute.kind === 'chute';
    const onBend = !!run && run.kind === 'bend';
    if (onChute) {
      // hold the chosen lane into and down the chute: no wander, no cut
      target = b.chuteLane * Math.min(fg(feel, 'ai.water.laneM'), half[kWater] - fg(feel, 'ai.railMarginM') - 0.5);
    } else if (onBend) {
      // inside (let the river carry you round) or outside (speed, arrive wide), rolled once per bot
      const lineM = Math.min(fg(feel, 'ai.water.lineM'), half[kWater] - fg(feel, 'ai.railMarginM') - 0.5);
      target = run.side * (b.waterInside ? lineM : -lineM) + wander * 0.5;
    }
    const wide = onWater || onChute ? null : driftSetup(b, s, speed, la, nearLip || !!court || inHold);
    if (wide !== null) target = wide;
    // aim against the lateral flow ahead: it will carry the kart that far over the next leadS seconds
    target -= flowL[at(s + la)] * fg(feel, 'ai.water.leadS');

    // around slower karts and live traps ahead
    const avoidM = feel.get('ai.avoidM'), shift = feel.get('ai.avoidShiftM');
    if (ctx.racers) {
      for (const o of ctx.racers) {
        if (o === r || o.fxv && o.fxv.ghost) continue;
        const ds = wrap(o.st[S.s] - s + L / 2, L) - L / 2;
        if (ds <= 0 || ds > avoidM) continue;
        if (o.st[S.speedPct] * o.feel.get('speed.topMps') >= speed - 0.5) continue;   // faster: follow, take the wake
        const ol = o.st[S.lateral];
        if (Math.abs(ol - target) < shift) target = ol + (target >= ol ? shift : -shift);
      }
    }
    const traps = trapsOf(ctx.items);
    for (let k = 0; k < traps.length; k++) {
      const t = traps[k];
      if (!t || !(t.s >= 0) || typeof t.x !== 'number') continue;   // rows carry the trap's own s and x
      if (wrap(t.s - s, L) > avoidM * 1.5) continue;
      if (Math.abs(t.x - target) < shift) target = t.x + (target >= t.x ? shift : -shift);
    }
    // polish 6: a boost pad that would carry the kart into a bend it cannot take (an open one most of all) is left alone
    for (let k = 0; k < padRisk.length; k++) {
      const pd = padRisk[k];
      if (!pd.risky || wrap(pd.s - s, L) > avoidM * 1.5) continue;
      const edge = pd.w / 2 + shift * 0.5;
      if (Math.abs(target - pd.x) < edge) target = pd.x + (target >= pd.x ? edge : -edge);
    }
    const limitLook = court && s < court.last + 3 ? Math.min(la * 1.5, court.last + 3 - s) : la * 1.5;
    lineLimits(s, limitLook, LIM);
    if (b.courtRoute) target = courts.target(b.courtRoute, s, la, target);
    target = clamp(target, LIM.lo, LIM.hi);
    // stepping stones: ease onto the picked lane over the first blendM of the commit zone, hold it (no avoid shift,
    // no wander, no open-edge band: the lane is inside the road by construction) and let go over leaveM after the field
    if (b.field) {
      const a0 = fp.fromS - fg(feel, 'ai.stones.approachM');
      const enter = smoothstep(a0, a0 + fg(feel, 'ai.stones.blendM'), s + la * 0.5);
      const leave = smoothstep(fp.toS, fp.toS + fg(feel, 'ai.stones.leaveM'), s);
      // damped: aim short of the lane by the slide, so the sideways speed is gone at the entry (airborne over the
      // gaps the kart cannot steer, and 5 m/s of slide carried a bot clean across its 5 m stone into the slit)
      const aim = b.field.lane - clamp((b.latV || 0) * fg(feel, 'ai.stones.settleS'), -2.5, 2.5);
      target = lerp(target, aim, enter * (1 - leave));
    }

    // steer: pure pursuit on the target point
    const f = track.frameAt(s + la, FR);
    const tx = f.p[0] + f.b[0] * target - st[S.posX], ty = f.p[1] + f.b[1] * target - st[S.posY], tz = f.p[2] + f.b[2] * target - st[S.posZ];
    const qx = st[S.quatX], qy = st[S.quatY], qz = st[S.quatZ], qw = st[S.quatW];
    const rx = 1 - 2 * (qy * qy + qz * qz), ry = 2 * (qx * qy + qw * qz), rz = 2 * (qx * qz - qw * qy);          // +X
    const fx = -(2 * (qx * qz + qw * qy)), fy = -(2 * (qy * qz - qw * qx)), fz = -(1 - 2 * (qx * qx + qy * qy)); // -Z
    const x = tx * rx + ty * ry + tz * rz, z = tx * fx + ty * fy + tz * fz;
    // the calm softens the wheel at speed, so the bot pushes the stick further for the same yaw: its
    // pursuit loop keeps the Phase A response and only the full-lock limit is new (sim/steer.js)
    yawParams(r.feel, YP);
    const soft = Math.max(0.2, calmOf(speed, YP) * YP.user);
    let steer = Math.atan2(x, Math.max(z, 0.1)) * feel.get('ai.steerGain') / soft;
    if (blind > 0 && !inHold) steer += blind * feel.get('ai.blindSteerNoise') * (0.6 * Math.sin(1.7 * b.t + b.n1) + 0.4 * Math.sin(3.1 * b.t + b.n2));
    out.steer = clamp(steer, -1, 1);
    // stepping stones: airborne over a gap the kart keeps its heading (the lane was picked at the commit)
    if (inField && st[S.airborne] > 0) out.steer = 0;
    // on the chute itself the wheel stays small: the lane was chosen at the lip, gravity does the rest
    if (onChute && run === chute) { const m = fg(feel, 'ai.water.chuteSteerMax'); out.steer = clamp(out.steer, -m, m); }
    // heading against the road here (+ = nose right of travel) and sideways speed across it
    const f0 = track.frameAt(s, FR0);
    const ft = fx * f0.t[0] + fy * f0.t[1] + fz * f0.t[2], fb = fx * f0.b[0] + fy * f0.b[1] + fz * f0.b[2];
    b.hdg = Math.atan2(fb, ft);
    b.latV = st[S.velX] * f0.b[0] + st[S.velY] * f0.b[1] + st[S.velZ] * f0.b[2];

    // facing the wrong way: brake, then reverse with the wheel turned the other way, until the nose comes round
    const fwdV = st[S.velX] * fx + st[S.velY] * fy + st[S.velZ] * fz;
    if (fwdV < -0.5 && st[S.engineV] < 0) out.steer = -out.steer;   // reversing: the yaw law flips (sim/kart.js), so does the wheel
    if (ft < -0.1 && ph === RESPAWN.NONE) {
      const turn = x >= 0 ? 1 : -1;
      out.throttle = 0; out.brake = 1; out.steer = fwdV > 1 ? turn : -turn;
      b.driftT = -1; b.stuckT = 0;
      return;
    }

    // throttle and brakes for the bend ahead (lift, then brake if well over)
    let vmax = Infinity;
    const brakeLook = Math.max(la, speed * 1.2), coast = feel.get('engine.coastMps2');
    // of the calmed grip law (sim/steer.js), the kart's own feel; a Freeze (mods.steerMul) is planned for too
    const share = fg(feel, 'ai.cornerMargin') * clamp(r.mods ? r.mods.steerMul : 1, 0.2, 1);
    for (let d = 0; d <= brakeLook; d += STEP * 2) {
      const v = bendSpeed(bend[at(s + d)], YP, share);
      vmax = Math.min(vmax, Math.sqrt(v * v + 2 * coast * d));
    }
    if (b.courtRoute && b.courtRoute.band.x !== 0 && s < court.first + 3 && Math.abs(lat - b.courtRoute.band.x) > 1) vmax = Math.min(vmax, 20);
    // stepping stones: the throttle stays down into and through the field (a lift before a gap is a fall)
    if (inHold) vmax = Math.max(vmax, fg(feel, 'ai.stones.minMps'));
    out.throttle = 1;
    if (!nearLip && speed > vmax) {
      out.throttle = 0;
      if (speed > vmax + 3) out.brake = clamp((speed - vmax) / 6, 0, 1);
    }
    if (blind > 0 && !inHold) out.throttle *= lerp(1, feel.get('ai.blindThrottle'), blind);

    // stuck (nose in a rail, facing the wrong way): back out
    if (b.reverseT > 0) {
      b.reverseT -= dt;
      out.throttle = 0; out.brake = 1; out.steer = -out.steer;
      return;
    }
    // a pool is never "stuck": the landing takes speed on purpose and no rescue is asked for there (the throttle
    // stays on through it by itself: the bot only lifts for a bend or the court ahead)
    const onPool = surf[kWater] === 2;
    if (onPool) b.stuckT = 0;
    else if (ph === RESPAWN.NONE && race.timeS > 2 && st[S.speedPct] < fg(feel, 'ai.stuckPct')) {
      b.stuckT += dt;
      if (b.stuckT > fg(feel, 'ai.stuckS')) { b.stuckT = 0; b.reverseT = fg(feel, 'ai.reverseS'); }
    } else b.stuckT = 0;

    driftLogic(r, b, dt, out, s, lat, speed, la, nearLip || !!court || onWater || onChute || inHold, blind);
    // a trick pays a boost on landing: never into an open edge ahead (polish 6: a boost cannot brake for an open bend)
    if (st[S.airborne] > 0 && st[S.airTime] > 0 && st[S.airTime] <= 0.1 && !b.trickRolled) { b.trickRolled = true; if (b.rand() < feel.get('trick.botChance') && !openAhead(s, fg(feel, 'ai.trickOpenM'))) out.hopPressed = true; } else if (st[S.grounded] > 0) b.trickRolled = false;
    // Challenge fields need a real hop. Use the current physical polygon, including its live yaw.
    if (inField && fp.field.platforming && st[S.grounded] > 0) {
      const stone = stoneIn(fp.field, s, lat), span = stone && stoneLaneSpan(stone, lat);
      const lead = clamp(speed * .1, 1.5, 4.5);
      if (span && span[1] - s >= 0 && span[1] - s <= lead && span[1] < fp.toS - 2) {
        out.hop = true; out.hopPressed = true;
      }
    }
    if (r.item) itemLogic(r, b, ctx, dt, out);
  }

  /**
   * Drop shortcuts (track.routes): once a lap, ai.routeLeadM + 40 m before an open edge, the bot rolls
   * ai.routeChance (x ai.routeLowMul for tier 1 personas). Taking it, it aims straight at the landing pad
   * from routeLeadM out, full throttle, no drift, until it is down on either road past the edge.
   * @returns {boolean} true while the route drives the bot (out filled)
   */
  function routeDrive(r, b, ctx, dt, out, s, speed, blind) {
    const rs = track.routes, st = r.st;
    if (!rs || !rs.length || !r.meta || !r.meta.isBot) return false;
    const lead = fg(feel, 'ai.routeLeadM'), lap = st[S.lap] | 0;
    if (b.route) {
      const rt = b.route, past = wrap(s - rt.toS, L);
      if (st[S.respawnPhase] !== RESPAWN.NONE || (st[S.grounded] > 0 && past > 3 && past < L / 2)) b.route = null;
    } else if (b.routeLap !== lap) {
      for (const rt of rs) {
        // polish 6: a gap route (the dare) needs a drift-in cut of 50+ deg at speed; the bots leave it to people
        if (rt.kind === 'gap') continue;
        const d = wrap(rt.fromS - s, L);
        if (d <= lead || d > lead + 40) continue;
        b.routeLap = lap;
        const chance = fg(feel, 'ai.routeChance') * (b.persona.tier >= 2 ? 1 : fg(feel, 'ai.routeLowMul'));
        if (blind < 0.3 && b.rand() < chance) b.route = rt;
      }
    }
    const rt = b.route;
    if (!rt || (st[S.grounded] > 0 && wrap(rt.fromS - s, L) > lead && wrap(rt.fromS - s, L) < L / 2)) return false;
    // follow the straight line through the landing pad along the lower road: the upper road bends away
    // under it, so the open edge comes to the kart (a hop takes it over the kerb, below)
    const ahead = fg(feel, 'ai.routeAimAheadM'), pt = rt.pad.t, pp = rt.pad.p, aim = RT_AIM;
    const along = (st[S.posX] - pp[0]) * pt[0] + (st[S.posY] - pp[1]) * pt[1] + (st[S.posZ] - pp[2]) * pt[2] + ahead;
    const pn = rt.pad.n, off = fg(feel, 'ai.routeLineM') * (rt.side === 'L' ? -1 : 1);   // the line sits toward the open side
    const bx = pt[1] * pn[2] - pt[2] * pn[1], by = pt[2] * pn[0] - pt[0] * pn[2], bz = pt[0] * pn[1] - pt[1] * pn[0];
    aim[0] = pp[0] + pt[0] * along + bx * off; aim[1] = pp[1] + pt[1] * along + by * off; aim[2] = pp[2] + pt[2] * along + bz * off;
    const tx = aim[0] - st[S.posX], ty = aim[1] - st[S.posY], tz = aim[2] - st[S.posZ];
    const qx = st[S.quatX], qy = st[S.quatY], qz = st[S.quatZ], qw = st[S.quatW];
    const rx = 1 - 2 * (qy * qy + qz * qz), ry = 2 * (qx * qy + qw * qz), rz = 2 * (qx * qz - qw * qy);
    const fx = -(2 * (qx * qz + qw * qy)), fy = -(2 * (qy * qz - qw * qx)), fz = -(1 - 2 * (qx * qx + qy * qy));
    const x = tx * rx + ty * ry + tz * rz, z = tx * fx + ty * fy + tz * fz;
    yawParams(r.feel, YP);
    const soft = Math.max(0.2, calmOf(speed, YP) * YP.user);
    out.steer = clamp(Math.atan2(x, Math.max(z, 0.1)) * feel.get('ai.steerGain') / soft, -1, 1);
    out.throttle = 1;
    b.driftT = -1;
    // the open edge is a kerb on the ground: hop over it once the line has carried the kart to it
    const hopLead = fg(feel, 'ai.routeHopLeadM'), into = wrap(s - (rt.openS0 - hopLead), L), sideLat = rt.side === 'L' ? -st[S.lateral] : st[S.lateral];
    if (st[S.grounded] > 0 && into < rt.openS1 - rt.openS0 + hopLead && sideLat > half[at(s)] - fg(feel, 'ai.routeHopEdgeM')) {
      out.hop = true; out.hopPressed = true;
      out.steer = clamp(out.steer, -0.15, 0.15);          // a straight hop: no drift
    }
    if (r.item) itemLogic(r, b, ctx, dt, out);
    return true;
  }

  /** Side (+1 right, -1 left) of a drift-worthy bend starting at s0: long, steady, no open edge, no jump. */
  function driftBend(s0, speed) {
    const curve = feel.get('ai.driftCurveDegPerM'), side = Math.sign(bend[at(s0)]);
    if (!side) return 0;
    const span = Math.max(20, speed * feel.get('ai.driftMinS') * 1.5);
    for (let d = 0; d <= span; d += STEP) if (bend[at(s0 + d)] * side < curve) return 0;
    if (openAhead(s0 - 10, span + 20) || lipAhead(s0 - 10, span + fg(feel, 'ai.jumpGuardM'))) return 0;
    // a hop where the bank still changes can land under the rising edge: drift only once the bank is steady
    const r0 = roll[at(s0)], tol = fg(feel, 'ai.driftBankTolDeg') * Math.PI / 180;
    for (let d = -10; d <= span; d += STEP) if (Math.abs(roll[at(s0 + d)] - r0) > tol) return 0;
    return side;
  }

  /** Set up wide for a drift: the outside lateral to aim at, or null. Rolls the persona once per bend. */
  function driftSetup(b, s, speed, la, nearLip) {
    if (b.driftT >= 0 || nearLip) return null;
    const side = driftBend(s + la + 10, speed) || (b.setupSide && driftBend(s + la * 0.3, speed));
    if (side !== b.setupSide) { b.setupSide = side; b.wantDrift = !!side && b.rand() < b.persona.drift; }
    if (!side || !b.wantDrift || b.driftCool > 0) return null;
    return -side * Math.min(fg(feel, 'ai.driftWideM'), half[at(s)] - fg(feel, 'ai.railMarginM') - 0.5);
  }

  function driftLogic(r, b, dt, out, s, lat, speed, la, nearLip, blind) {
    const st = r.st;
    b.driftCool = Math.max(0, b.driftCool - dt);
    const curve = feel.get('ai.driftCurveDegPerM'), minS = feel.get('ai.driftMinS');
    if (b.driftT >= 0) {                                  // holding a hop drift
      b.driftT += dt;
      const dir = b.driftDir, k = bend[at(s + la)] * dir, room = half[at(s)] - fg(feel, 'ai.railMarginM');
      const into = b.hdg * dir, ahead = lat + b.latV * fg(feel, 'ai.driftLeadS');   // where the slide puts us soon
      const inside = ahead * dir > room - 0.8 || into > fg(feel, 'ai.driftMaxDeg') * Math.PI / 180;
      const tierOk = (st[S.driftStage] | 0) >= b.persona.tier;
      const sliding = st[S.driftDir] !== 0;
      const wall = inside && st[S.grounded] > 0 && ahead * dir > room;
      const danger = nearLip || openAhead(s, la) || blind > 0.5 || wall;
      const done = b.driftT >= minS && (k < curve * 0.5 || inside || tierOk);
      if (danger || done || b.driftT > fg(feel, 'ai.driftMaxS') || (!sliding && b.driftT > 0.35 && st[S.grounded] > 0)) {
        const w = !sliding ? 'cancelled' : tierOk ? 'tier' : danger ? (wall ? 'wall' : 'risk') : done ? (inside ? 'inside' : 'bend') : 'max';
        b.why[w] = (b.why[w] || 0) + 1;       // release reasons, for tuning (botOf)
        b.driftT = -1; b.driftCool = fg(feel, 'ai.driftCoolS'); b.wantDrift = false;
        return;                                        // hop released: the drift pays its tier
      }
      out.hop = true;
      // the drift yaw has a floor, so: counter-steer in the air (the nose turns, the path does not), then
      // steer into it on the ground (charges at drift.softInMul) while the slide leaves room
      if (!tierOk) out.steer = st[S.grounded] > 0 && into < fg(feel, 'ai.driftIntoDeg') * Math.PI / 180 && ahead * dir < room - 2 ? dir : -dir;
      return;
    }
    // start one: on a drift-worthy bend, set up wide, fast, grounded, nothing risky near
    if (!b.wantDrift || b.driftCool > 0 || nearLip || blind > 0.3 || st[S.grounded] <= 0 || st[S.airborne] > 0) return;
    if (st[S.stalled] > 0 || speed < feel.get('drift.minSpeedMps') + 6) return;
    const side = driftBend(s + la * 0.3, speed);
    if (!side || side !== b.setupSide) return;
    if (lat * side > -1.5 && bend[at(s)] * side < curve) return;   // not wide yet and the bend is still ahead
    // the floor on drift yaw turns the nose in through the hop, so hop with it still pointing a little out,
    // as long as the hop lands inside the road
    const room = half[at(s)] - fg(feel, 'ai.railMarginM');
    if (b.hdg * side > 0.05 || (lat + b.latV * fg(feel, 'ai.driftLeadS')) * side < -room) return;
    out.hop = true; out.hopPressed = true;
    if (out.steer * side < 0.6) out.steer = side * 0.6;   // the drift picks its side from the stick
    b.driftT = 0; b.driftDir = side;
  }

  function itemLogic(r, b, ctx, dt, out) {
    const it = r.item;
    b.itemCool = Math.max(0, b.itemCool - dt);
    if (!it.id || it.rolling) { b.holdT = 0; return; }
    b.holdT += dt;
    if (b.holdT < feel.get('ai.itemHoldMinS') || b.itemCool > 0) return;
    if (b.rand() > feel.get('ai.itemUseChancePerS') * b.persona.item * dt) return;
    const def = ITEMS[it.id];
    if (!def) return;
    const range = feel.get('ai.itemRangeM'), me = r.progressM;
    let ahead = false, behind = false, wideAhead = false, leaderAhead = false, tetherAhead = false, close = false;
    const pinkM = feel.get('items.pinkRangeM'), tetherM = feel.get('items.tetherRangeM'), snapM = feel.get('items.snapRadiusM');
    for (const o of ctx.racers || []) {
      if (o === r || o.finished) continue;             // a finished kart is never a target (items skip it too)
      const d = o.progressM - me;
      if (d > 0) leaderAhead = true;
      if (d > 0 && d <= range) ahead = true;
      if (d > 0 && d <= pinkM) wideAhead = true;
      if (d < 0 && -d <= range) behind = true;
      if (d > 0 && d <= tetherM) tetherAhead = true;
      if (Math.abs(d) <= snapM) close = true;
    }
    let press = false, back = false;
    if (it.id === 'snap') press = close;
    else if (def.use === 'self') press = it.id !== 'surge' || r.st[S.boostKind] === BOOST.NONE;
    else if (def.use === 'tether') press = tetherAhead;
    else if (def.use === 'throw' || def.use === 'bounce') {
      if (ahead) press = true;
      else if (behind && b.rand() < feel.get('ai.itemDropChance')) { press = true; back = true; }
    } else if (def.use === 'lob') press = leaderAhead || behind;   // at the leader; leading, at place 2 once close
    else if (def.use === 'wide') press = wideAhead;
    else if (def.use === 'trap') press = behind;
    if (press && it.id === 'shatter' && ctx.items && typeof ctx.items.nearOpenEdge === 'function' &&
      ctx.items.nearOpenEdge(r.st[S.s], feel.get('items.shatterEdgeSafeM'))) press = false;
    if (!press) return;
    out.item = true; out.back = back;
    b.itemCool = feel.get('ai.itemCooldownS');
  }

  return { drive, reset, personaOf: (i) => bot(i).persona.id, botOf: (i) => bot(i), water: () => runs, fields: () => fields };
}
