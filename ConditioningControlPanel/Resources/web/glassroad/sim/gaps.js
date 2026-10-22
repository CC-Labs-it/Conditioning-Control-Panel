/* sim/gaps.js - feel keys the sim needs that feel.json does not carry yet (CONTRACT.md "feel gap").
 *
 * gap(feel, path) reads feel first, so the moment the key lands in feel.json the sheet wins.
 * Each default is a slice starting point, reported to the integrator as a contract gap.
 */
const GAPS = {
  'kart.radiusM': 0.7,        // saucer radius: rail contact distance (camera note: 6.5 m is ~9 radii)
  'kart.gripTcS': 0.05,       // lateral velocity bleeds into the heading with this time constant (grip turn)
  'kart.alignTcS': 0.08,      // kart up slerps to the road normal with this time constant
  'kart.snapM': 0.35,         // a grounded kart sticks to the road within this height (crests, sags)
  'drift.slideTcS': 0.25,     // the same bleed while drifting: sets the drift slip angle
  'drift.steerMin': 0.2,      // stick share that picks a drift direction and the soft-drift side
  'wall.minHitMps': 1.0,      // into-rail speed under this slides along the rail with no hit
  'boost.tailEndTcs': 4,      // a boost tail ends after this many tail time constants
  'respawn.hoverM': 2.0,      // hover height above the anchor node during a rescue
};

/** @returns {any} feel value, else the slice default above */
export function gap(feel, path) {
  try { return feel.get(path); } catch (e) { return GAPS[path]; }
}
export const GAP_KEYS = Object.freeze(Object.keys(GAPS));
