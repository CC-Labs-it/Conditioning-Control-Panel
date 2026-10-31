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

// On a 1e-6 grid first: 1 - 0.95 is 0.050000000000000044, which would tip a half way mark.
const bar = (t) => Math.max(1, Math.min(5, 3 + Math.round(Math.round(t * 1e6) / 1e6)));

/** The garage's four bars, integers 1..5, Lovebomb 3 3 3 3, plus the type word's key. */
export function statBars(feel, bodyId) {
  const k = kartStats(feel, bodyId), top = k.topMul - 1;
  return {
    speed: bar(top * (top >= 0 ? 2 / 0.024 : 2 / 0.02)),   // the table's fastest and slowest rows are the ends of the bar
    accel: bar((k.accelMul - 1) * 10),
    handling: bar(((1 - k.gripMul) + (k.driftMul - 1)) * 10),
    stability: bar(((1 - k.wallMul) + (1 - k.bumpMul)) * (2 / 0.6)),
    type: k.type,
  };
}
