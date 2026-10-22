// The opening intro's clock. Pure: no DOM, no canvas, so node can check it.
//
// The page opens on the ads' end card, rebuilt: a train of meshed brass gears
// comes up along the baseline and every letter of "Piece by Piece" is wound up
// through it on a ratchet, the outer letters first (the King's Walk card), the
// letters squash and wobble as they land, the gears sink, then the line "It's
// still chess." in the 1858 serif and the pink stamp "Mostly." (the chess ad's
// card). Then the whole card lifts away and the page underneath is just there.
// ui/open-intro.js draws it; everything here is a function of intro time.
//
// Levels: 'full' is the clockwork, 'reduced' a calm card fade with nothing
// moving, 'off' nothing at all. A skip takes the whole card out fast.

// the board intro's ratchet and eases: the name winds up the way the men do
import { ratchet, smooth, outCubic } from '../board/intro-plan.js';

export const OPEN = Object.freeze({
  max: 4,                       // the hard cap at speed 1, before any gentle scaling
  gearsIn: 0.4,                 // the train comes up from below
  firstAt: 0.12, land: 1.45,    // the first letter starts, the last one lands
  rise: 0.72, clicks: 5,        // each letter's wind: this long, in this many clicks
  wobble: 0.14,                 // the landing squash
  gearsOutDelay: 0.06, gearsOut: 0.42,
  gearSpin: 2.6,                // radians a second while the letters rise
  lineAt: 1.62, lineIn: 0.38,
  stampAt: 2.0, stampIn: 0.18,
  outAt: 2.58, out: 0.42,       // the card lifts away; the page is there at 3.0 s
  skipOut: 0.2,                 // a click, a key or a touch takes it out this fast
  hurryAt: 1.55,                // a spectate open goes out once the name has landed
  reduced: Object.freeze({ in: 0.18, hold: 0.24, out: 0.18 }),
});

export { smooth };

const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const outBack = (u, s = 1.7) => { u = clamp(u, 0, 1) - 1; return 1 + u * u * ((s + 1) * u + s); };

/** Which level plays: the Options box off is 'off'; reduced motion is 'reduced' unless Gentle asks for the full one slower. */
export function openLevel({ enabled = true, reduced = false, gentle = 0 } = {}) {
  if (!enabled) return 'off';
  if (gentle > 0) return 'full';
  return reduced ? 'reduced' : 'full';
}

/**
 * The plan for one title. `letters` is [{ ch, at }] with `at` 0..1 across the word (the letter's middle).
 * The outer letters start first, the middle last; spaces never move.
 * Returns { level, letters: [{ ch, at, start }], total, outAt }.
 */
export function openPlan(letters, level = 'full') {
  const T = OPEN;
  if (level === 'off') return { level, letters: [], total: 0, outAt: 0 };
  if (level === 'reduced') {
    const R = T.reduced;
    return { level, letters: letters.map(l => ({ ...l, start: 0 })), total: R.in + R.hold + R.out, outAt: R.in + R.hold };
  }
  const span = T.land - T.rise - T.firstAt;
  const out = letters.map(l => {
    const edge = Math.min(l.at, 1 - l.at) * 2;      // 0 at the ends, 1 in the middle
    return { ...l, start: T.firstAt + clamp(edge, 0, 1) * span };
  });
  return { level, letters: out, total: T.outAt + T.out, outAt: T.outAt };
}

/** A spectate open: the name lands, then the card goes, with no line and no stamp to wait for. */
export function hurried(plan, t) {
  if (plan.level !== 'full') return plan;
  const outAt = Math.min(plan.outAt, Math.max(t, OPEN.hurryAt));
  return { ...plan, outAt, total: outAt + OPEN.out };
}

/** How far a skip has taken the card out: 1 before it, 0 once it has gone. */
export function skipFade(t, skipAt) {
  return skipAt == null ? 1 : clamp(1 - (t - skipAt) / OPEN.skipOut, 0, 1);
}

/** Time as the picture sees it: a skip freezes the card where it was while it fades. */
const seen = (t, skipAt) => (skipAt == null ? t : Math.min(t, skipAt));

/**
 * One letter at intro time t: `drop` 1 (all the way under the baseline) to 0 (home),
 * `sx`/`sy` its squash, `hidden` before it starts.
 */
export function letterPose(l, t, level = 'full', skipAt = null) {
  if (level === 'reduced') return { drop: 0, sx: 1, sy: 1, hidden: false };
  t = seen(t, skipAt);
  const u = (t - l.start) / OPEN.rise;
  if (u <= 0) return { drop: 1, sx: 1, sy: 1, hidden: true };
  const after = t - l.start - OPEN.rise;
  let sx = 1, sy = 1;
  if (after > 0) { const k = Math.exp(-after * 8) * Math.cos(after * 30); sx = 1 + OPEN.wobble * k; sy = 1 - OPEN.wobble * k; }
  return { drop: 1 - ratchet(u, OPEN.clicks), sx, sy, hidden: false };
}

/** The gear train: `k` 0 (sunk) to 1 (up on the baseline), `spin` its turn. Full level only. */
export function gearTrain(plan, t, skipAt = null) {
  const T = OPEN;
  if (plan.level !== 'full') return { k: 0, spin: 0 };
  const at = seen(t, skipAt);
  const kin = smooth(at / T.gearsIn), kout = smooth((at - (T.land + T.gearsOutDelay)) / T.gearsOut);
  // turns at speed while the letters rise, then winds down as it sinks
  const spinT = at < T.land ? at : T.land + (1 - Math.exp(-(at - T.land) * 3)) / 3;
  return { k: kin * (1 - kout) * skipFade(t, skipAt), spin: T.gearSpin * spinT };
}

/** The serif line: `k` 0..1 its wipe in. */
export function lineState(plan, t, skipAt = null) {
  if (plan.level === 'reduced') return { k: 1 };
  if (plan.level !== 'full') return { k: 0 };
  return { k: outCubic((seen(t, skipAt) - OPEN.lineAt) / OPEN.lineIn) };
}

/** The pink stamp: `k` 0 (not yet) to 1 (landed), `scale` its slam, `rot` its tilt (radians), `a` its alpha. */
export function stampState(plan, t, skipAt = null) {
  const tilt = -7 * Math.PI / 180;
  if (plan.level === 'reduced') return { k: 1, scale: 1, rot: tilt, a: 1 };
  const d = seen(t, skipAt) - OPEN.stampAt;
  if (plan.level !== 'full' || d <= 0) return { k: 0, scale: 1.9, rot: tilt, a: 0 };
  const k = clamp(d / OPEN.stampIn, 0, 1);
  return {
    k,
    scale: 1.9 + (1 - 1.9) * outBack(k, 2.6),
    rot: tilt + 2 * Math.PI / 180 * Math.exp(-d * 6) * Math.sin(d * 30),
    a: clamp(d / 0.06, 0, 1),
  };
}

/** The whole card: `a` alpha, `lift` 0..1 up and away, `kick` the stamp's knock (scale - 1). */
export function cardState(plan, t, skipAt = null) {
  const R = OPEN.reduced;
  let a, lift = 0, kick = 0;
  if (plan.level === 'reduced') {
    a = Math.min(smooth(t / R.in), 1 - smooth((t - plan.outAt) / R.out));
  } else {
    const o = smooth((seen(t, skipAt) - plan.outAt) / OPEN.out);
    a = 1 - o; lift = o;
    const d = seen(t, skipAt) - (OPEN.stampAt + OPEN.stampIn * 0.6);
    if (d > 0 && plan.outAt > OPEN.stampAt) kick = 0.025 * Math.exp(-d * 9) * Math.cos(d * 26);
  }
  return { a: a * skipFade(t, skipAt), lift, kick };
}

/** The dark under the card: opaque until the card goes, then gone with it (no blank frame, no flash). */
export function backdrop(plan, t, skipAt = null) {
  if (plan.level === 'reduced') return clamp(1 - smooth((t - plan.outAt) / OPEN.reduced.out), 0, 1) * skipFade(t, skipAt);
  return (1 - smooth((seen(t, skipAt) - plan.outAt) / OPEN.out)) * skipFade(t, skipAt);
}

/** Ratchet clicks of the outer letters crossed in (a, b]: one quiet tick each, never one per letter. */
export function clicksBetween(plan, a, b) {
  if (plan.level !== 'full' || !plan.letters.length) return 0;
  const first = Math.min(...plan.letters.map(l => l.start));
  let n = 0;
  for (let k = 0; k < OPEN.clicks; k++) {
    const at = first + (k / OPEN.clicks) * OPEN.rise;
    if (at > a && at <= b) n++;
  }
  return n;
}

/** The moments the page's sound kit marks: the name landing, the stamp. Crossed in (a, b]. */
export function cuesBetween(plan, a, b) {
  if (plan.level !== 'full') return [];
  const out = [];
  if (OPEN.land > a && OPEN.land <= b && plan.outAt >= OPEN.land) out.push('land');
  if (OPEN.stampAt > a && OPEN.stampAt <= b && plan.outAt > OPEN.stampAt) out.push('stamp');
  return out;
}

/** True once nothing of the intro is left to draw. */
export function openOver(plan, t, skipAt = null) {
  if (plan.level === 'off') return true;
  if (skipAt != null) return t >= skipAt + OPEN.skipOut;
  return t >= plan.total;
}
