/* sim/trick.js - the trick boost (polish 4, FEEL.md "trick"). Three-free.
 *
 * A hop pressed in a timing window around a LIP pays a trick: the lip of a ramp or kicker, a slope crest
 * that throws the kart (both take off with cause 'lip'), or a boost pad (its entry is the lip moment).
 * - late: in the air, within trick.lateS after a 'lip' take-off (the press does not hop, the kart is up);
 * - pad: on the ground, within trick.padLateS after entering a pad (the press hops off the pad);
 * - early: on the ground, within trick.earlyS before a lip, jump or pad ahead (needs the track: kart.js
 *   passes it to hop(); without it only the late and pad sides work).
 * Within trick.greatS of the lip moment the grade is 'great', else 'good'; outside the window a press is
 * a plain hop (or nothing in the air). Emits trick { grade, airS, at } when it registers (airS = air so
 * far) and trick-land { grade, airS, amount, holdS } on touch-down, where it pays a boost: trick.amount
 * by grade, held trick.holdS + trick.holdPerAirS x air (capped at holdMaxS), so bigger air pays longer.
 * Over a running boost it never pays less: the larger of the trick and the running amount + stackAdd,
 * and the longer hold. A drop or a rescue loses the trick. The per-kart record lives beside the state
 * (the frozen layout has no free field), keyed by the state array.
 */
import { S, BOOST, RESPAWN } from './state.js';
import { wrap } from '../core/math.js';
import { applyBoost, boostHoldLeftS, BOOST_TRICK } from './boost.js';

export const TRICK_GRADES = Object.freeze(['', 'good', 'great']);

const REC = new WeakMap();
function rec(st) {
  let r = REC.get(st);
  if (!r) REC.set(st, (r = { lipAge: 1e3, armed: 0, at: '' }));
  return r;
}

/** The armed trick grade of a kart: '' with none, else 'good' or 'great' (render, tests). */
export const trickOf = (st) => TRICK_GRADES[(REC.get(st) || { armed: 0 }).armed];

const gradeOf = (dtS, feel) => (Math.abs(dtS) <= feel.get('trick.greatS') ? 2 : 1);

function arm(st, r, grade, at, events) {
  r.armed = grade; r.at = at;
  events.emit('trick', { grade: TRICK_GRADES[grade], airS: st[S.airborne] > 0 ? st[S.airTime] : 0, at });
}

const FA = { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] }, FB = { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] };
const pitchOf = (t) => Math.asin(Math.max(-1, Math.min(1, t[1])));

/**
 * A lip within distM ahead of s: a jump lip, a pad under `lateral`, or a crest (the road pitches down by
 * trick.crestDeg or more between here and just past distM). @returns {{at: string, d: number}|null}
 */
export function lipAhead(track, s, lateral, distM, feel) {
  if (!track || !(distM > 0)) return null;
  const L = track.lengthM;
  for (const j of track.jumps || []) { const d = wrap(j.s - s, L); if (d <= distM) return { at: 'lip', d }; }
  for (const p of track.boosts || []) {
    const d = wrap(p.s - s, L);
    if (d <= distM && Math.abs(lateral - p.x) <= p.w / 2) return { at: 'pad', d };
  }
  if (typeof track.frameAt === 'function') {
    const a = pitchOf(track.frameAt(s, FA).t), b = pitchOf(track.frameAt(s + distM + 2, FB).t);
    if ((a - b) * 180 / Math.PI >= feel.get('trick.crestDeg')) return { at: 'crest', d: distM };
  }
  return null;
}

/** Every tick (from sim/drift.js updateDrift): age the lip clock, a late press in the air, rescue loses it. */
export function tickTrick(st, a, feel, dt, events, grounded) {
  const r = rec(st);
  r.lipAge += dt;
  if (st[S.respawnPhase] !== RESPAWN.NONE) { r.armed = 0; r.lipAge = 1e3; return; }
  if (!grounded && st[S.airborne] > 0 && a.hopPressed && !r.armed && r.lipAge <= feel.get('trick.lateS')) {
    arm(st, r, gradeOf(r.lipAge, feel), 'lip', events);
  }
}

/** Take-off (sim/air.js): a new air loses an unpaid trick; a 'lip' take-off starts the late window. */
export function noteTakeoff(st, cause) {
  const r = rec(st);
  r.armed = 0;
  if (cause === 'lip') { r.lipAge = 0; r.at = 'lip'; }
}

/** A ground hop (sim/hop.js), after its take-off: the pad window, then a lip ahead (with the track). */
export function trickOnHop(st, feel, events, speed, track) {
  const r = rec(st);
  if (r.armed) return;
  if (st[S.boostKind] === BOOST.PAD && st[S.boostT] <= feel.get('trick.padLateS')) {
    arm(st, r, gradeOf(st[S.boostT], feel), 'pad', events);
    return;
  }
  if (!track) return;
  const v = Math.max(1, Math.abs(speed));
  const hit = lipAhead(track, st[S.s], st[S.lateral], v * feel.get('trick.earlyS'), feel);
  if (hit) arm(st, r, gradeOf(hit.d / v, feel), hit.at, events);
}

/** Touch-down (sim/air.js): pay an armed trick. @returns {number} the grade paid (0 none) */
export function trickOnLand(st, feel, events, airTime) {
  const r = rec(st), g = r.armed;
  if (!g) return 0;
  r.armed = 0;
  let amount = feel.get('trick.amount')[g - 1];
  let hold = Math.min(feel.get('trick.holdMaxS'), feel.get('trick.holdS')[g - 1] + feel.get('trick.holdPerAirS') * airTime);
  const left = boostHoldLeftS(st, feel);
  if (left > 0) {
    amount = Math.max(amount, st[S.boostAmount] + feel.get('trick.stackAdd'));
    hold = Math.max(hold, left);
  }
  applyBoost(st, BOOST_TRICK, g, feel, events, { amount, holdS: hold });
  events.emit('trick-land', { grade: TRICK_GRADES[g], airS: airTime, amount, holdS: hold });
  return g;
}
