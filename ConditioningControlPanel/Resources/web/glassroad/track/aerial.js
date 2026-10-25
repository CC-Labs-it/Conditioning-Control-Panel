/* Authored long flights. The existing spline remains the physical flight corridor. */
import { wrap } from '../core/math.js';

export const AERIAL_REVISION = 1;
// Stable node anchors deliberately avoid each course's existing toys, forks and stepping fields.
const ANCHORS = Object.freeze({
  threshold: [[140, 150, 'prism-rise'], [232, 242, 'prism-skywalk']],
  keep: [[311, 320, 'north-approach'], [531, 540, 'courtyard-flight']],
  boudoir: [[312, 321, 'upper-runout'], [722, 732, 'mirror-approach', 35]],
  mainframe: [[54, 63, 'address-uplink'], [484, 493, 'lower-bus-flight']],
  goonworks: [[376, 386, 'grand-fold-flight'], [534, 544, 'home-flight']],
});
const lapS = (track, s) => s >= 0 && s < track.lengthM ? s : wrap(s, track.lengthM);

export function buildAerials(track) {
  return (ANCHORS[track.id] || []).map(([from, to, id, runoutM = 45]) => {
    const gapS0 = track.nodeS[from], gapS1 = track.nodeS[to];
    if (!(gapS1 > gapS0)) throw Error('Missing aerial anchors: ' + track.id + '/' + id);
    let widthM = Infinity;
    for (let s = gapS0 - 20; s <= gapS1 + runoutM; s += 5) widthM = Math.min(widthM, track.frameAt(s).w);
    return Object.freeze({ id: track.id + '-' + id, rampS0: gapS0 - 20, rampS1: gapS0,
      gapS0, gapS1, landingS0: gapS1, landingS1: gapS1 + runoutM, widthM });
  });
}

/** Approach, unsupported flight and generous landing runout, on the main physical path. */
export function aerialAt(track, s) {
  if (!Number.isFinite(s)) return null;
  const at = lapS(track, s);
  return track.aerials?.find(a => at >= a.rampS0 && at < a.landingS1) || null;
}

/** Half-open bounds retain actual support at the landing edge. */
export function aerialGapAt(track, s) {
  const a = aerialAt(track, s), at = lapS(track, s);
  return a && at >= a.gapS0 - 1e-7 && at < a.gapS1 - 1e-7 ? a : null;
}

/** Keep the existing pad's whole rollout and the flight landing free of fresh boosts. */
export function aerialBoostAllowed(track, s, len = 0) {
  return !(track.aerials || []).some(a => s < a.landingS1 && s + len > a.gapS0 - 85);
}
