/* sim/gaps.js - feel keys the sim needs that feel.json does not carry yet (CONTRACT.md "feel gap").
 *
 * gap(feel, path) reads feel first, so the moment the key lands in feel.json the sheet wins.
 * Each default is a slice starting point, reported to the integrator as a contract gap.
 */
const GAPS = {
  'kart.radiusM': 0.5,        // saucer radius: rail contact distance (polish 6: the kart is drawn at 0.68, saucer ~0.51 m)
  'kart.gripTcS': 0.05,       // lateral velocity bleeds into the heading with this time constant (grip turn)
  'kart.alignTcS': 0.08,      // kart up slerps to the road normal with this time constant
  'kart.snapM': 0.35,         // a grounded kart sticks to the road within this height (crests, sags)
  'drift.slideTcS': 0.25,     // the same bleed while drifting: sets the drift slip angle
  'drift.steerMin': 0.2,      // stick share that picks a drift direction and the soft-drift side
  'wall.minHitMps': 1.0,      // into-rail speed under this slides along the rail with no hit
  'wall.topM': 1.5,           // polish 4: an airborne kart this far over the road plane clears a rail (the tube is 0.9 m)
  'boost.tailEndTcs': 4,      // a boost tail ends after this many tail time constants
  'respawn.hoverM': 2.0,      // hover height above the anchor node during a rescue
  // polish 4 ROUTES (sim/routes.js): the drop shortcut's soft catch and its clean-landing boost
  'route.catchPct': 0.25,     // a route landing keeps at most this share of top speed
  'route.catchHoldS': 2.5,    // and thrust is capped there this long (the soft glass catch)
  'route.cleanLenM': 24,      // clean: within this length of road around the pad
  'route.cleanLatM': 3.5,     // and this far either side of the pad's centre
  'route.cleanDeg': 25,       // with the nose this close to the road's heading
  'route.cleanAmount': 0.15,  // clean landing boost: share of top over top, when the catch ends
  'route.cleanHoldS': 0.6,
  'route.kickUpMps': 2.5,     // EDGES lane: the opening's kicker lip throws a kart driving off it up this fast
  'route.holdSteer': 0.25,    // polish 6: a grounded kart turning away from the opening this hard is held like the rail
  'route.airPull': 1,         // in the air over the landing road: drift back to its middle at this many m/s per metre off
  'route.airMaxMps': 5,       // at most this fast
  'route.airAccel': 6,        // changing by at most this many m/s per second
  // polish 6 THE LOOP (sim/kart.js): on a magnet stretch the road carries the kart round
  'loop.guideTcS': 0.15,      // the nose eases toward its own line up the road with this time constant
  'loop.guideAheadS': 0.25,   // aiming this many seconds of speed ahead (at least 4 m)
  'loop.carryPct': 0.4,       // a forward engine floor, as a share of top
  'loop.carryMps2': 14,       // reached at this rate (no jolt)
};

// feel.get throws on a missing key and a feel never gains keys (set refuses them), so a miss is remembered
// per feel object: no Error thrown per kart per tick. A racer overlay (sim/racers.js) can gain a key in
// its own `over` at any time, so that is checked first.
const MISSING = new WeakMap();
const hasOwn = Object.prototype.hasOwnProperty;

/** @returns {any} feel value, else the slice default above */
export function gap(feel, path) {
  if (feel.over && hasOwn.call(feel.over, path)) return feel.over[path];
  const miss = MISSING.get(feel);
  if (miss && miss.has(path)) return GAPS[path];
  try { return feel.get(path); } catch (e) {
    if (miss) miss.add(path); else MISSING.set(feel, new Set([path]));
    return GAPS[path];
  }
}
export const GAP_KEYS = Object.freeze(Object.keys(GAPS));
