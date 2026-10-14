// The board intro's clock. Pure: no THREE, no DOM, so node can check it.
//
// A new game's men are wound up out of the board: every man starts under his
// square and comes up through a brass gear turning on it, rooks first (castles),
// then the pawns from the rook files in, then the court from the knights in.
// The rise is a ratchet: a few clicks, each a quick wind and a short hold. Each
// gear comes up just before its man, turns while he rises and sinks after he
// lands; four big wheels turn round the plinth and sink once the last man is
// home. board/intro.js draws it; everything here is a function of intro time.
//
// Levels: 'full' is the clockwork, 'reduced' a short calm lift with no gears
// and no wheels, 'off' no intro at all. A skip lands every man at once and
// takes the brass out fast.

export const INTRO = Object.freeze({
  gearIn: 0.25,                 // a gear rises this long before its man starts
  rooks: 0, rookDur: 0.9, rookClicks: 6,
  pawns: 0.35, pawnStep: 0.06, pawnDur: 0.7, pawnClicks: 4,
  court: 0.6, courtStep: 0.12, courtDur: 0.9, courtClicks: 5,
  gearOutDelay: 0.1, gearOut: 0.45,
  gearSpin: 2.4,                // radians a second while the man rises
  manTurn: Math.PI * 2,         // one turn on the way up, eased out
  wheelIn: 0.45, wheelOutLead: 0.3, wheelOut: 0.7, wheelTurn: 2.6,
  skipOut: 0.18,                // a skip takes the brass out this fast
  reduced: Object.freeze({ dur: 0.45, step: 0.08 }),
});

const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
export const smooth = (u) => { u = clamp(u, 0, 1); return u * u * (3 - 2 * u); };
export const outCubic = (u) => 1 - Math.pow(1 - clamp(u, 0, 1), 3);

/** The rise as a ratchet: `n` clicks, each a quick wind and a short hold. 0..1 in, 0..1 out. */
export function ratchet(u, n) {
  u = clamp(u, 0, 1);
  if (u >= 1) return 1;
  const k = Math.min(n - 1, Math.floor(u * n)), f = u * n - k;
  return (k + smooth(f / 0.62)) / n;
}

/** Which level plays: the Board intro option off is 'off', reduced motion is 'reduced'.
 *  Gentle visuals (gentle > 0) play the full clockwork, slower (the clock runs at `gentle`). */
export function introLevel({ enabled = true, reduced = false, gentle = 0 } = {}) {
  if (!enabled) return 'off';
  if (gentle > 0) return 'full';
  return reduced ? 'reduced' : 'full';
}

/**
 * How fast the intros run under the host's Gentle visuals: 0 = not gentle (the usual levels),
 * else 0.5..1 (0.5 = twice as long). The host sends gentleIntroSpeed > 0 only when Gentle is the
 * one reason for its reduced motion; the page's own reduce-motion box or the OS asking still win.
 */
export function gentleSpeed({ host = 0, ownReduced = false, osReduced = false } = {}) {
  const s = Number(host);
  if (!Number.isFinite(s) || s <= 0 || ownReduced || osReduced) return 0;
  return Math.max(0.5, Math.min(1, s));
}

/** Only a board set up for a new game: never a takeback, a resume, a resync or a replay. */
export function shouldPlay({ level, plies }) {
  return level !== 'off' && (Number(plies) || 0) === 0;
}

/**
 * The plan for one board. `men` is [{ sq, type }] (type p n b r q k).
 * Returns { level, men: [{ sq, type, start, dur, clicks, dir }], end, total }.
 * `end` is when the last man lands; `total` when the last of the brass is gone.
 */
export function introPlan(men, level = 'full') {
  const out = [];
  if (level === 'off') return { level, men: out, end: 0, total: 0 };
  const T = INTRO;
  for (const m of men) {
    const f = 'abcdefgh'.indexOf(m.sq[0]), r = Number(m.sq[1]) - 1;
    const edge = Math.abs(f - 3.5);                  // 3.5 on the rook files, 0.5 in the middle
    let start, dur, clicks;
    if (level === 'reduced') {
      start = m.type === 'r' ? 0 : m.type === 'p' ? T.reduced.step : T.reduced.step * 2;
      dur = T.reduced.dur; clicks = 0;
    } else if (m.type === 'r') {
      start = T.gearIn + T.rooks; dur = T.rookDur; clicks = T.rookClicks;
    } else if (m.type === 'p') {
      start = T.gearIn + T.pawns + (3.5 - edge) * T.pawnStep; dur = T.pawnDur; clicks = T.pawnClicks;
    } else {
      start = T.gearIn + T.court + Math.max(0, 2.5 - edge) * T.courtStep; dur = T.courtDur; clicks = T.courtClicks;
    }
    // meshed gears turn against their neighbours
    out.push({ sq: m.sq, type: m.type, start, dur, clicks, dir: ((f + r) % 2) ? 1 : -1 });
  }
  const end = out.reduce((a, m) => Math.max(a, m.start + m.dur), 0);
  const total = level === 'reduced' ? end
    : Math.max(end + T.gearOutDelay + T.gearOut, end - T.wheelOutLead + T.wheelOut);
  return { level, men: out, end, total };
}

/** How far a skip has taken the brass out: 1 before it, 0 once it has gone. */
function skipFade(t, skipAt) {
  return skipAt == null ? 1 : clamp(1 - (t - skipAt) / INTRO.skipOut, 0, 1);
}

/**
 * One man at intro time t: `lift` 0 (all the way down) to 1 (home), `spin` the
 * yaw still to unwind, `hidden` before he starts. A skip lands him at once.
 */
export function manPose(m, t, level = 'full', skipAt = null) {
  if (skipAt != null && t >= skipAt) return { lift: 1, spin: 0, hidden: false };
  const u = (t - m.start) / m.dur;
  if (level === 'reduced') return { lift: outCubic(u), spin: 0, hidden: u <= 0 };
  return {
    lift: ratchet(u, m.clicks),
    spin: (1 - outCubic(u)) * INTRO.manTurn * m.dir * (m.type === 'p' ? 0.5 : 1),
    hidden: u <= 0,
  };
}

/** His gear: `k` 0 (sunk) to 1 (up on the square), `angle` its turn. Full level only. */
export function gearState(m, t, skipAt = null) {
  const T = INTRO;
  const at = skipAt == null ? t : Math.min(t, skipAt);
  const kin = smooth((at - (m.start - T.gearIn)) / T.gearIn);
  const kout = smooth((at - (m.start + m.dur + T.gearOutDelay)) / T.gearOut);
  const landed = m.start + m.dur;
  // turns at speed while he rises, then winds down
  const spinT = at < landed ? at : landed + (1 - Math.exp(-(at - landed) * 3)) / 3;
  return { k: kin * (1 - kout) * skipFade(t, skipAt), angle: m.dir * T.gearSpin * spinT };
}

/** The big wheels round the plinth: `k` 0 (sunk) to 1 (up), `angle` their turn. Full level only. */
export function wheelState(plan, t, skipAt = null) {
  const T = INTRO;
  const at = skipAt == null ? t : Math.min(t, skipAt);
  const k = smooth(at / T.wheelIn) * (1 - smooth((at - (plan.end - T.wheelOutLead)) / T.wheelOut));
  return { k: k * skipFade(t, skipAt), angle: T.wheelTurn * at * 0.1 };
}

/** Rook ratchet clicks crossed in (a, b]: the intro ticks once for each, never per man. */
export function clicksBetween(plan, a, b) {
  if (plan.level !== 'full') return 0;
  const rook = plan.men.find(m => m.type === 'r');
  if (!rook) return 0;
  let n = 0;
  for (let k = 0; k < rook.clicks; k++) {
    const at = rook.start + (k / rook.clicks) * rook.dur;
    if (at > a && at <= b) n++;
  }
  return n;
}

/** True once nothing of the intro is left to draw. */
export function introOver(plan, t, skipAt = null) {
  if (skipAt != null) return t >= skipAt + INTRO.skipOut;
  return t >= plan.total;
}
