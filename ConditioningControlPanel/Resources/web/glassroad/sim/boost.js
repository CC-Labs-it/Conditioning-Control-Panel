/* sim/boost.js - boost payouts and their envelope (plan section 6, "Boost payout" and "Boost pads").
 *
 * A boost holds an engine FLOOR: it rises linearly from the engineV latched at the start (boostFromV)
 * to top * (1 + amount) over boost.riseS, so a kart at cruise really takes riseS to gain it, holds for
 * the tier's hold, then the extra decays with time constant boost.tailTcS. boost-end fires as the
 * tail starts, so fx and audio ease back over the same tail constant the engine does.
 */
import { S, BOOST } from './state.js';
import { gap } from './gaps.js';

export const BOOST_NAME = Object.freeze(['', 'drift', 'drift', 'drift', 'pad', 'launch']);

/** Start a boost: kind BOOST.*, tier 1..3 (ignored for PAD). Replaces any running boost. Emits boost-start. */
export function applyBoost(st, kind, tier, feel, events) {
  let amount, hold;
  if (kind === BOOST.PAD) {
    amount = feel.get('pad.amount'); hold = feel.get('pad.holdS'); tier = 0;
  } else {
    const i = Math.min(3, Math.max(1, tier | 0)) - 1;
    amount = feel.get('boost.tierAmount')[i]; hold = feel.get('boost.tierHoldS')[i]; tier = i + 1;
  }
  st[S.boostKind] = kind; st[S.boostT] = 0; st[S.boostAmount] = amount; st[S.boostHoldS] = hold;
  st[S.boostFromV] = Math.max(0, st[S.engineV]);
  events.emit('boost-start', { kind: BOOST_NAME[kind], tier, amount, holdS: hold });
}

/** 0..1: linear rise, hold, exponential tail. */
export function envelope(t, holdS, feel) {
  const rise = feel.get('boost.riseS');
  if (t < rise) return t / rise;
  if (t <= rise + holdS) return 1;
  return Math.exp(-(t - rise - holdS) / feel.get('boost.tailTcS'));
}

/** Advance the running boost by dt; returns the engine floor it holds (m/s), 0 with none. */
export function boostFloor(st, feel, top, dt, events) {
  const kind = st[S.boostKind];
  if (kind === BOOST.NONE) return 0;
  const hold = st[S.boostHoldS], end = feel.get('boost.riseS') + hold;
  const t0 = st[S.boostT], t = (st[S.boostT] = t0 + dt);
  if (t0 <= end && t > end) events.emit('boost-end', { kind: BOOST_NAME[kind] });
  if (t > end + feel.get('boost.tailTcS') * gap(feel, 'boost.tailEndTcs')) { clear(st); return 0; }
  const e = envelope(t, hold, feel), amt = st[S.boostAmount], peak = top * (1 + amt), from = st[S.boostFromV];
  return t <= end ? from + (peak - from) * e : top * (1 + amt * e);
}

/** Cut the running boost (wall, drop). Emits boost-end unless the tail already announced it. */
export function endBoost(st, feel, events) {
  const kind = st[S.boostKind];
  if (kind === BOOST.NONE) return;
  if (st[S.boostT] <= feel.get('boost.riseS') + st[S.boostHoldS]) events.emit('boost-end', { kind: BOOST_NAME[kind] });
  clear(st);
}

function clear(st) { st[S.boostKind] = BOOST.NONE; st[S.boostT] = 0; st[S.boostAmount] = 0; st[S.boostHoldS] = 0; st[S.boostFromV] = 0; }
