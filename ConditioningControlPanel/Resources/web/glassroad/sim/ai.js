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
 */
import { S, BOOST, RESPAWN } from './state.js';
import { clamp, lerp, wrap, damp } from '../core/math.js';
import { applyBoost } from './boost.js';
import { gap } from './gaps.js';
import { ITEMS } from './itemTable.js';
import { bendSpeed, yawParams, calmOf } from './steer.js';

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

// feel gap: personalities (multipliers on the ai.* numbers)
export const PERSONAS = Object.freeze([
  { id: 'steady', spread: 0.5, cut: 0.8, drift: 0.5, tier: 1, item: 0.8, wander: 0.6 },
  { id: 'drifter', spread: 0.8, cut: 1.0, drift: 1.0, tier: 2, item: 0.9, wander: 0.8 },
  { id: 'thrower', spread: 0.7, cut: 0.9, drift: 0.6, tier: 1, item: 1.4, wander: 1.0 },
  { id: 'cutter', spread: 1.0, cut: 1.3, drift: 0.8, tier: 2, item: 1.0, wander: 0.7 },
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

export function createAi(track, feel, { seed = 1 } = {}) {
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

  const bots = new Map();

  function bot(i) {
    let b = bots.get(i);
    if (!b) {
      const rand = rng(seed * 7919 + i * 104729);
      const persona = i === 0 ? PERSONAS[0] : PERSONAS[(i + seed) % PERSONAS.length];
      b = { rand, persona, line: (rand() * 2 - 1), phase: rand() * Math.PI * 2, n1: rand() * 6.28, n2: rand() * 6.28,
        t: 0, driftT: -1, driftCool: 0, driftDir: 0, holdT: 0, itemCool: 0, stuckT: 0, reverseT: 0,
        launched: false, launch: rand(), setupSide: 0, wantDrift: false, why: {}, routeLap: -1, route: null, routes: 0 };
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

  function drive(r, ctx, dt, out) {
    out.steer = 0; out.throttle = 0; out.brake = 0; out.hop = false; out.hopPressed = false; out.pause = false; out.item = false; out.back = false;
    const race = ctx && ctx.race, st = r.st, b = bot(r.i);
    if (!race || (race.phase !== 'racing' && race.phase !== 'finished')) return;
    b.t += dt;
    const ph = st[S.respawnPhase];
    if (ph === RESPAWN.PULL || ph === RESPAWN.HOVER || ph === RESPAWN.RELEASE) {
      b.driftT = -1; b.reverseT = 0; b.stuckT = 0; out.throttle = 1; return;
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
    const la = Math.max(feel.get('ai.lookAheadMinM'), speed * feel.get('ai.lookAheadS'));
    const p = b.persona, blind = r.mods ? clamp(r.mods.blind, 0, 1) : 0;
    const nearLip = lipAhead(s, fg(feel, 'ai.jumpGuardM'));

    // polish 4: a drop shortcut, rolled once a lap as the bot nears its open edge
    if (routeDrive(r, b, ctx, dt, out, s, speed, blind)) return;

    // the line
    const wander = feel.get('ai.lineWanderM') * p.wander * Math.sin(2 * Math.PI * b.t / feel.get('ai.lineWanderS') + b.phase);
    const bendLa = bend[at(s + la)];
    const cutK = clamp(Math.abs(bendLa) / feel.get('ai.driftCurveDegPerM'), 0, 1);
    let target = b.line * feel.get('ai.lineSpreadM') * p.spread + wander + Math.sign(bendLa) * feel.get('ai.cornerCutM') * p.cut * cutK;
    if (nearLip) target *= 0.3;
    const wide = driftSetup(b, s, speed, la, nearLip);
    if (wide !== null) target = wide;

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
    lineLimits(s, la * 1.5, LIM);
    target = clamp(target, LIM.lo, LIM.hi);

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
    if (blind > 0) steer += blind * feel.get('ai.blindSteerNoise') * (0.6 * Math.sin(1.7 * b.t + b.n1) + 0.4 * Math.sin(3.1 * b.t + b.n2));
    out.steer = clamp(steer, -1, 1);
    // heading against the road here (+ = nose right of travel) and sideways speed across it
    const f0 = track.frameAt(s, FR0);
    const ft = fx * f0.t[0] + fy * f0.t[1] + fz * f0.t[2], fb = fx * f0.b[0] + fy * f0.b[1] + fz * f0.b[2];
    b.hdg = Math.atan2(fb, ft);
    b.latV = st[S.velX] * f0.b[0] + st[S.velY] * f0.b[1] + st[S.velZ] * f0.b[2];

    // facing the wrong way: brake, then reverse with the wheel turned the other way, until the nose comes round
    const fwdV = st[S.velX] * fx + st[S.velY] * fy + st[S.velZ] * fz;
    if (fwdV < -0.5) out.steer = -out.steer;        // rolling backwards: the yaw law flips, so does the wheel
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
    out.throttle = 1;
    if (!nearLip && speed > vmax) {
      out.throttle = 0;
      if (speed > vmax + 3) out.brake = clamp((speed - vmax) / 6, 0, 1);
    }
    if (blind > 0) out.throttle *= lerp(1, feel.get('ai.blindThrottle'), blind);

    // stuck (nose in a rail, facing the wrong way): back out
    if (b.reverseT > 0) {
      b.reverseT -= dt;
      out.throttle = 0; out.brake = 1; out.steer = -out.steer;
      return;
    }
    if (ph === RESPAWN.NONE && race.timeS > 2 && st[S.speedPct] < fg(feel, 'ai.stuckPct')) {
      b.stuckT += dt;
      if (b.stuckT > fg(feel, 'ai.stuckS')) { b.stuckT = 0; b.reverseT = fg(feel, 'ai.reverseS'); }
    } else b.stuckT = 0;

    driftLogic(r, b, dt, out, s, lat, speed, la, nearLip, blind);
    // a trick pays a boost on landing: never into an open edge ahead (polish 6: a boost cannot brake for an open bend)
    if (st[S.airborne] > 0 && st[S.airTime] > 0 && st[S.airTime] <= 0.1 && !b.trickRolled) { b.trickRolled = true; if (b.rand() < feel.get('trick.botChance') && !openAhead(s, fg(feel, 'ai.trickOpenM'))) out.hopPressed = true; } else if (st[S.grounded] > 0) b.trickRolled = false;
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
    let ahead = false, behind = false, wideAhead = false, leaderAhead = false;
    const pinkM = feel.get('items.pinkRangeM');
    for (const o of ctx.racers || []) {
      if (o === r || o.finished) continue;             // a finished kart is never a target (items skip it too)
      const d = o.progressM - me;
      if (d > 0) leaderAhead = true;
      if (d > 0 && d <= range) ahead = true;
      if (d > 0 && d <= pinkM) wideAhead = true;
      if (d < 0 && -d <= range) behind = true;
    }
    let press = false, back = false;
    if (def.use === 'self') press = it.id !== 'surge' || r.st[S.boostKind] === BOOST.NONE;
    else if (def.use === 'throw') {
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

  return { drive, reset, personaOf: (i) => bot(i).persona.id, botOf: (i) => bot(i) };
}
