/* sim/kartStats.js - what a kart body does (owner 2026-10-31; table in feel.json `karts`, meaning in FEEL.md).
 *
 * core/feel.js is global, so a body's numbers live on the racer: createRacers puts one row on r.feel.kart and
 * sim/kart.js, sim/drift.js and sim/contact.js read it from there. A plain feel (a sim test, a tool) has no row and
 * drives the neutral kart. A missing or unknown body reads as the table's default, so nothing here throws.
 * No three.js.
 */
export const STAT_KEYS = ['speed', 'accel', 'handling', 'stability'];
export const TYPE_LABELS = { allround: 'All-rounder', easy: 'Easy', drifter: 'Drifter', heavy: 'Heavy', fast: 'Fast' };
/** All ones: multiplying by this row leaves every number exactly as it was. */
export const NEUTRAL_KART = Object.freeze({ type: 'allround', topMul: 1, accelMul: 1, gripMul: 1, driftMul: 1, wallMul: 1, bumpMul: 1 });

const VEHICLE = 'emi-vehicle-';
const has = (o, k) => !!o && typeof o === 'object' && Object.prototype.hasOwnProperty.call(o, k);

/** The `karts` block of a Feel or of a parsed feel.json; null when there is none. */
function table(feel) {
  try {
    const t = typeof feel?.get === 'function' ? feel.get('karts') : feel?.karts;
    return t && typeof t === 'object' ? t : null;
  } catch { return null; }
}

/** True for an id with a row of its own (the dev switch asks; the sim never needs to). */
export const isKartBody = (feel, bodyId) => has(table(feel)?.bodies, bodyId);

/** The row for a body, the default row when unknown; never throws. */
export function kartStats(feel, bodyId) {
  const t = table(feel), rows = t?.bodies;
  const row = has(rows, bodyId) ? rows[bodyId] : has(rows, t?.default) ? rows[t.default] : null;
  return { ...NEUTRAL_KART, ...row };
}

/** The body a seat's look names (NET.md looks): `emi-vehicle-undertow` -> `undertow`; null for anything else. */
export const bodyOfVehicle = (vehicle) => (typeof vehicle === 'string' && vehicle.startsWith(VEHICLE) ? vehicle.slice(VEHICLE.length) : null);

/** The highest topMul in the table: the one limit a room holds every seat to (CCP-Server rooms, `maxKartTopMul`). */
export function maxTopMul(feel) {
  const rows = table(feel)?.bodies;
  return rows ? Math.max(1, ...Object.values(rows).map((r) => Number(r?.topMul) || 1)) : 1;
}

/** Seven levels with Lovebomb in the middle: five could not show every trade-off (House Edge read as better than Velvet
 * Lock on every bar, which is really faster). tests/kart-stats.test.mjs fails if any kart reads as beating another. */
export const BAR_LEVELS = 7;
// Where each level starts, as the change from Lovebomb. A table edit that lands a row between marks may need new marks.
const CUTS = {
  speed: [-0.018, -0.012, -0.0001, 0.0001, 0.015, 0.022],
  accel: [-0.18, -0.1, -0.001, 0.001, 0.1, 0.18],
  handling: [-0.3, -0.15, -0.001, 0.001, 0.08, 0.12],
  stability: [-0.5, -0.2, -0.001, 0.001, 0.5, 0.6],
};
// On a 1e-6 grid first: 1 - 0.95 is 0.050000000000000044.
const bar = (key, change) => { const t = Math.round(change * 1e6) / 1e6; return 1 + CUTS[key].filter((c) => t >= c).length; };

/** The garage's four bars, integers 1..BAR_LEVELS, Lovebomb 4 4 4 4, plus the type word's key. */
export function statBars(feel, bodyId) {
  const k = kartStats(feel, bodyId);
  return {
    speed: bar('speed', k.topMul - 1),
    accel: bar('accel', k.accelMul - 1),
    handling: bar('handling', (1 - k.gripMul) + (k.driftMul - 1)),
    stability: bar('stability', (1 - k.wallMul) + (1 - k.bumpMul)),
    type: k.type,
  };
}
