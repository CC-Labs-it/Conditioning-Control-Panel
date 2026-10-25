/* sim/assist.js - the player's steer assist (music road pass 4, owner: "help the player: keep them toward the
 * CENTRE of the track, a slight tendency to AUTOPILOT, in case they trance"). Pure: no DOM, no three.js.
 *
 * It reshapes the PLAYER's actions only, between the input sample and the sim (boot.js), never a bot's and never
 * the sim law. Four layers, all off in mode 'off':
 *   PULL    a gentle steer back toward the centre that grows with |lateral| past assist.pullFromHalf of the half
 *           width, plus a little heading alignment with the road a short way ahead.
 *   AUTO    no steer input for assist.autoSteerAfterS: steering that follows the drive line (pure pursuit on the
 *           centre with a small cut to the inside of a bend) eases in over assist.autoEaseS.
 *   CRUISE  no input at all for assist.cruiseAfterS: a soft cruise throttle (assist.cruisePct of top speed, lifted
 *           and gently braked for a bend ahead the same way the bots plan one), so a tranced player keeps rolling
 *           with the song. It never boosts, only hops at challenge lips during established autopilot, never drifts or fires an item.
 *   EASE    (polish 6) the throttle held into a bend the kart cannot take at its speed: the throttle lifts and the
 *           brake gets a light dab (Strong a firmer one) so the kart holds its line. Never under a boost, on a
 *           jump lip, in a drift, or while the player brakes or steers for a shortcut's opening.
 * No fight: a steer the player makes always wins. While the stick is held, an assist that agrees adds a little
 * (fading as the stick goes to full lock), one that disagrees is dropped, so the kart never turns less, or the
 * other way, than the player asks. ANY steer input drops AUTO at once and ANY input drops CRUISE at once (no ease
 * out, no lag). Nothing acts outside the racing phase, in the air, in a drift, under a rescue or facing backwards
 * (CRUISE alone keeps a gentle U-turn there so a hands-off kart comes round).
 *   createAssist(track, feel, { mode }) -> { apply(a, st, dt, phase) -> actions, setMode(m), reset(), view, mode }
 * apply() copies `a` into its own actions object (no allocation) and returns that; `a` itself is never written.
 */
import { S, RESPAWN, BOOST } from './state.js';
import { clamp, wrap } from '../core/math.js';
import { bendSpeed, yawParams, calmOf } from './steer.js';
import { gap } from './gaps.js';
import { createCourtPaths } from './court-paths.js';
import { fieldAtS } from './stones.js';
import { stoneIn, stoneLaneSpan } from '../track/courts.js';

/** The option values in the order the Options card cycles them (the first is the default). */
export const ASSIST_MODES = Object.freeze(['light', 'strong', 'off']);
/** Per mode: pull, align and cap multipliers on the feel numbers (light = 1). */
const MODE = Object.freeze({
  off: Object.freeze({ on: false, pull: 0, align: 0, cap: 0 }),
  light: Object.freeze({ on: true, pull: 1, align: 1, cap: 1 }),
  strong: Object.freeze({ on: true, pull: 1.9, align: 1.7, cap: 1.8 }),
});
export const assistMode = (m) => (Object.prototype.hasOwnProperty.call(MODE, m) ? m : ASSIST_MODES[0]);

const STEP = 2;                                   // m between bend table samples
const ROUTE_NOSE = 0.035;                         // rad: a nose turned this far out toward an opening counts as steering for it
// polish 6: the autopilot steers round a boost pad that would carry it into a bend it cannot take
const PAD_RISK_M = 140, PAD_RISK_PCT = 0.85, PAD_CLEAR_M = 1.2, PAD_RAIL_M = 2, PAD_LOOK_M = 50;
// polish 6 EASE: the bend limit (sim/steer.js bendSpeed at this grip share), how far ahead (s of speed), the lift band
// (m/s over the limit from full throttle to none), and the brake dab cap (light; Strong x its cap multiplier)
const EASE_SHARE = 1, EASE_LOOK_S = 1.6, EASE_LIFT_MPS = 3, EASE_BRAKE = 0.15;
const YP = {};

export function createAssist(track, feel, { mode = 'light' } = {}) {
  const courts = createCourtPaths(track);
  let courtRoute = null, courtCommitted = false;
  const L = track.lengthM, N = Math.ceil(L / STEP);
  // per 2 m: signed bend (deg/m, + right) and half width (the bots build the same table, sim/ai.js)
  const bend = new Float32Array(N), half = new Float32Array(N);
  const lip = new Uint8Array(N);
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
      const f = track.frameAt(s, A);
      half[k] = f.w / 2;
    }
    // a jump lip and its gap: the line runs straight down the middle there
    for (const j of track.jumps || []) for (let d = -40; d <= (j.landS > j.s ? j.landS - j.s : 20) + 6; d += STEP) lip[Math.floor(wrap(j.s + d, L) / STEP) % N] = 1;
  }
  const at = (s) => Math.floor(wrap(s, L) / STEP) % N;
  const pads = (track.boosts || []).map((pd) => {
    yawParams(feel, YP);
    let risky = false;
    for (let d = 0; d <= PAD_RISK_M && !risky; d += STEP) risky = bendSpeed(bend[at(pd.s + d)], YP, feel.get('assist.cruiseCornerShare')) < PAD_RISK_PCT * feel.get('speed.topMps');
    return { s: pd.s, x: pd.x, w: pd.w, risky };
  });
  // drop shortcuts (track.routes): from assist.routeApproachM before an opening to its far end, a player who
  // steers toward that opening gets no centre pull (EDGES lane: the pull used to drag the player off the shortcut)
  const zones = (track.routes || []).map((r) => ({ from: r.openS0, len: r.openS1 - r.openS0, side: r.side === 'L' ? -1 : 1 }));
  const zoneAt = (s, lead) => { for (const z of zones) if (wrap(s - (z.from - lead), L) <= lead + z.len) return z; return null; };

  const FR = { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] }, FA = { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] };
  const out = { steer: 0, throttle: 0, brake: 0, hop: false, hopPressed: false, pause: false, item: false, back: false };
  /** Read-only status for tests and the dev beacon: auto and cruise are the eases (0..1), pull the last assist steer. */
  const view = { auto: 0, cruise: 0, pull: 0, steerIdleS: 0, idleS: 0, active: false };
  let m = MODE[assistMode(mode)], modeId = assistMode(mode);

  function reset() { view.auto = 0; view.cruise = 0; view.pull = 0; view.steerIdleS = 0; view.idleS = 0; view.active = false; courtRoute = null; }

  /** The nose against the road (+ = right of travel), forward and right axes of the kart. */
  const AX = { fx: 0, fy: 0, fz: 0, rx: 0, ry: 0, rz: 0 };
  function axes(st) {
    const qx = st[S.quatX], qy = st[S.quatY], qz = st[S.quatZ], qw = st[S.quatW];
    AX.rx = 1 - 2 * (qy * qy + qz * qz); AX.ry = 2 * (qx * qy + qw * qz); AX.rz = 2 * (qx * qz - qw * qy);
    AX.fx = -(2 * (qx * qz + qw * qy)); AX.fy = -(2 * (qy * qz - qw * qx)); AX.fz = -(1 - 2 * (qx * qx + qy * qy));
    return AX;
  }

  /**
   * One sim step of the assist. @param {object} a the sampled actions (read only)
   * @param {Float32Array} st the player's kart state @param {number} dt @param {string} phase race.phase
   * @returns {object} the actions the sim should use this step
   */
  function apply(a, st, dt, phase) {
    Object.assign(out, a);
    if (!m.on || phase !== 'racing') { reset(); return out; }
    const g = (k) => feel.get('assist.' + k);
    const dead = g('inputDead');
    const steerIn = Math.abs(a.steer) > dead;
    const anyIn = steerIn || a.throttle > dead || a.brake > dead || !!a.hop || !!a.hopPressed || !!a.item || !!a.back;
    // the timers: any input zeroes its ease at once (the player takes over the same step)
    if (steerIn) { view.steerIdleS = 0; view.auto = 0; } else view.steerIdleS += dt;
    if (anyIn) { view.idleS = 0; view.cruise = 0; } else view.idleS += dt;
    if (view.steerIdleS >= g('autoSteerAfterS')) view.auto = Math.min(1, view.auto + dt / Math.max(1e-3, g('autoEaseS')));
    if (view.idleS >= g('cruiseAfterS')) view.cruise = Math.min(1, view.cruise + dt / Math.max(1e-3, g('cruiseEaseS')));

    const ph = st[S.respawnPhase];
    const owned = ph === RESPAWN.PULL || ph === RESPAWN.HOVER || ph === RESPAWN.RELEASE;
    const top = feel.get('speed.topMps');
    const s = st[S.s], lat = st[S.lateral];
    const fork = track.forkAtSeg?.(st[S.seg]);
    const frameAhead = (d, out) => {
      if (!fork) return track.frameAt(s + d, out);
      const local = fork.localS(s) + d;
      return local > fork.lengthM ? track.frameAt(fork.toS + local - fork.lengthM, out) : fork.frameAt(local, out);
    };
    const curveAhead = d => {
      if (!fork) return bend[at(s + d)];
      const a = frameAhead(d - 4, FR), b = frameAhead(d + 4, FA);
      const dot = a.t.reduce((v, n, i) => v + n * b.t[i], 0), side = a.b.reduce((v, n, i) => v + n * b.t[i], 0);
      return Math.atan2(side, Math.abs(dot)) * 180 / Math.PI / 8;
    };
    const court = fork ? null : courts.at(s);
    if (!court || steerIn || owned || (courtRoute && courtRoute.plan !== court)) courtRoute = null;
    if (court && !courtRoute) { courtRoute = courts.choose(court, 0, 0, 0, lat); courtCommitted = s >= court.court.fromS + 1; }
    if (courtRoute && !courtCommitted && s >= court.court.fromS + 1) {
      courtRoute = courts.choose(court, 0, 0, 0, lat); courtCommitted = true;
    }
    const ax = axes(st);
    const fwdV = st[S.velX] * ax.fx + st[S.velY] * ax.fy + st[S.velZ] * ax.fz;
    const speed = Math.max(0, fwdV);
    const f0 = frameAhead(0, FR);
    const ft = ax.fx * f0.t[0] + ax.fy * f0.t[1] + ax.fz * f0.t[2];
    const backwards = ft < 0;
    view.active = false; view.pull = 0;
    if (owned) return out;

    // CRUISE (no input at all): a soft throttle, lifted and braked for the bend ahead; a U-turn when facing back
    if (view.cruise > 0) {
      if (backwards) {
        out.throttle = 0.5 * view.cruise;
        const fb = ax.fx * f0.b[0] + ax.fy * f0.b[1] + ax.fz * f0.b[2];
        out.steer = fb >= 0 ? -1 : 1;              // nose right of the road's back direction: turn it round the short way
        view.active = true;
        return out;
      }
      yawParams(feel, YP);
      const coast = feel.get('engine.coastMps2'), share = g('cruiseCornerShare');
      let vmax = g('cruisePct') * top;
      const look = Math.max(20, speed * 1.4);
      for (let d = 0; d <= look; d += STEP * 2) {
        const v = bendSpeed(curveAhead(d), YP, share);
        vmax = Math.min(vmax, Math.sqrt(v * v + 2 * coast * d));
      }
      out.throttle = speed < vmax ? g('cruiseThrottle') * view.cruise : 0;
      out.brake = !fork && lip[at(s)] ? 0 : clamp((speed - vmax - 3) / 8, 0, 0.5) * view.cruise;
    }

    // steering help only on the ground, rolling forward, out of a drift
    if (backwards || st[S.grounded] <= 0 || st[S.driftDir] !== 0 || speed < 2) return out;
    // Established hands-off autopilot jumps the challenge islands through the ordinary input.
    // Steering, braking, or disabled assist gives every jump back to the player immediately.
    const field = !fork && view.auto > .6 && view.cruise > .6 && !steerIn && a.brake <= dead && fieldAtS(track, s);
    if (field?.platforming && !a.hop && !a.hopPressed) {
      const stone = stoneIn(field, s, lat), span = stone && stoneLaneSpan(stone, lat);
      if (span && span[1] - s >= 0 && span[1] - s <= clamp(speed * .1, 1.5, 4.5) && span[1] < field.toS - 2) {
        out.hop = true; out.hopPressed = true;
      }
    }
    // the player steering for a drop shortcut's opening (stick toward it, or the nose already turned out that way
    // with the stick not held the other way: polish 6, an opening on the OUTSIDE of a turn sees every kart's nose
    // point out while it turns in, and the lip holds a kart turning away, sim/routes.js turnsAway)
    if (steerIn && !fork && (track.forks || []).some(f => s >= f.fromS - 30 && s <= f.fromS + 55)) return out;
    const z = !fork && steerIn && zones.length ? zoneAt(s, g('routeApproachM')) : null;
    if (z && (Math.sign(a.steer) === z.side || (a.steer * z.side > -gap(feel, 'route.holdSteer') &&
      Math.atan2(ax.fx * f0.b[0] + ax.fy * f0.b[1] + ax.fz * f0.b[2], ft) * z.side > ROUTE_NOSE))) {
      out.steer = a.steer;
      return out;
    }
    view.active = true;
    // EASE (polish 6, the coil and the column bring r 20-45 turns): throttle held into a bend the kart cannot take
    // at this speed, the assist lifts it and dabs the brake, so the kart holds its line instead of running wide
    // onto the rail (or an open edge). Not on a lip, not under a boost, never when the player brakes.
    if (a.throttle > dead && a.brake <= dead && (fork || !lip[at(s)]) && st[S.boostKind] === BOOST.NONE) {
      yawParams(feel, YP);
      const dabMax = EASE_BRAKE * m.cap, decel = feel.get('engine.coastMps2') + feel.get('engine.brakeMps2') * dabMax * 0.5;
      let vmax = Infinity;
      for (let d = 0, look = Math.max(20, speed * EASE_LOOK_S); d <= look; d += STEP) {
        const v = bendSpeed(curveAhead(d), YP, EASE_SHARE);
        if (v < Infinity) vmax = Math.min(vmax, Math.sqrt(v * v + 2 * decel * d));
      }
      const over = speed - vmax;
      if (over > 0) {
        out.throttle = a.throttle * clamp(1 - over / EASE_LIFT_MPS, 0, 1);
        if (over > EASE_LIFT_MPS) out.brake = clamp((over - EASE_LIFT_MPS) / 6, 0, dabMax);
      }
    }
    const h = fork ? frameAhead(0, FR).w / 2 : half[at(s)];
    // PULL: past pullFromHalf of the half width, back toward the centre
    const free = g('pullFromHalf') * h, ex = clamp((Math.abs(lat) - free) / Math.max(0.5, h - free), 0, 1);
    const courtLine = courtRoute ? courts.target(courtRoute, s, 0) : 0;
    const pull = courtRoute ? clamp((courtLine - lat) / 4, -1, 1) * g('pull') * m.pull
      : -Math.sign(lat) * ex * g('pull') * m.pull;
    // and a little heading alignment with the road a short way ahead (so it leans into a bend, never against it)
    const fa = frameAhead(Math.max(4, speed * g('alignLeadS')), FA);
    const at2 = ax.fx * fa.t[0] + ax.fy * fa.t[1] + ax.fz * fa.t[2], ab = ax.fx * fa.b[0] + ax.fy * fa.b[1] + ax.fz * fa.b[2];
    const hdg = Math.atan2(ab, at2);              // + = nose right of the road ahead
    const cap = g('max') * m.cap;
    const help = clamp(pull - hdg * g('align') * m.align, -cap, cap);

    let steer = help;
    if (view.auto > 0) {
      // AUTO: pure pursuit on the drive line, the centre with a small cut to the inside of a bend
      let la = Math.max(feel.get('ai.lookAheadMinM'), speed * feel.get('ai.lookAheadS'));
      if (courtRoute && s >= court.court.fromS) la = Math.min(la, 12);
      const k = at(s + la), bk = curveAhead(la);
      const cutK = clamp(Math.abs(bk) / feel.get('ai.driftCurveDegPerM'), 0, 1);
      const band = g('lineBandHalf') * Math.min(h, fork ? frameAhead(la, FA).w / 2 : half[k]);
      let target = !fork && (lip[k] || lip[at(s)]) ? 0 : clamp(Math.sign(bk) * g('lineCutM') * cutK, -band, band);
      for (let i = 0; !fork && i < pads.length; i++) {
        const pd = pads[i], edge = pd.w / 2 + PAD_CLEAR_M;
        if (!pd.risky || wrap(pd.s - s, L) > Math.max(la * 2, PAD_LOOK_M) || Math.abs(target - pd.x) >= edge) continue;
        target = clamp(pd.x + (target >= pd.x ? edge : -edge), -(Math.min(h, half[k]) - PAD_RAIL_M), Math.min(h, half[k]) - PAD_RAIL_M);
      }
      if (courtRoute) target = courts.target(courtRoute, s, la, target);
      const f = frameAhead(la, FA);
      const tx = f.p[0] + f.b[0] * target - st[S.posX], ty = f.p[1] + f.b[1] * target - st[S.posY], tz = f.p[2] + f.b[2] * target - st[S.posZ];
      const x = tx * ax.rx + ty * ax.ry + tz * ax.rz, z = tx * ax.fx + ty * ax.fy + tz * ax.fz;
      yawParams(feel, YP);
      const soft = Math.max(0.2, calmOf(speed, YP) * YP.user);
      const pursuit = clamp(Math.atan2(x, Math.max(z, 0.1)) * feel.get('ai.steerGain') / soft, -1, 1);
      steer = help + (pursuit - help) * view.auto;
    }
    view.pull = steer;
    const r = a.steer;
    if (!steerIn) out.steer = clamp(steer, -1, 1);
    else if (steer * r > 0) out.steer = clamp(r + steer * (1 - Math.abs(r)), -1, 1);   // agrees: a little more, fading at full lock
    else out.steer = r;                                                                // disagrees: the player wins
    return out;
  }

  return {
    apply, reset, view,
    setMode(x) { modeId = assistMode(x); m = MODE[modeId]; if (!m.on) reset(); },
    get mode() { return modeId; },
  };
}
