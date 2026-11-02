/* sim/grant.js - a boost of any size from B1 sources (slipstream, Surge, pickup bubbles), CONTRACT-B1.md.
 *
 * Same engine floor and envelope as a pad boost (sim/boost.js runs it as BOOST.PAD), but the size and
 * hold come from the caller and boost-start names the source: { kind: 'slip'|'surge'|'bubble', tier: 0,
 * amount, holdS }. boost-end still says 'pad' (boost.js names by the BOOST enum). A smaller grant never
 * cuts a bigger boost that is still rising or holding: it is dropped and grantBoost returns false.
 * Frozen contract file: owned by the contracts step, used by RACE (slipstream) and ITEMS.
 */
import { S, BOOST } from './state.js';

/** @returns {boolean} true when the boost started */
export function grantBoost(st, amount, holdS, kind, feel, events) {
  if (st[S.boostKind] !== BOOST.NONE && st[S.boostAmount] > amount &&
    st[S.boostT] <= feel.get('boost.riseS') + st[S.boostHoldS]) return false;
  st[S.boostKind] = BOOST.PAD; st[S.boostT] = 0; st[S.boostAmount] = amount; st[S.boostHoldS] = holdS;
  st[S.boostFromV] = Math.max(0, st[S.engineV]);
  events.emit('boost-start', { kind, tier: 0, amount, holdS });
  return true;
}
