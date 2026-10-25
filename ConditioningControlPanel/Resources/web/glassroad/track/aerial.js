/* Authored long flights. The existing spline remains the physical flight corridor. */
import { wrap } from '../core/math.js';

export const AERIAL_REVISION = 2;
// Stable node anchors deliberately avoid each course's existing toys, forks and stepping fields.
const ANCHORS = Object.freeze({
  threshold: [[140, 150, 'prism-rise'], [232, 242, 'prism-skywalk']],
  keep: [[311, 320, 'north-approach'], [531, 540, 'courtyard-flight']],
  boudoir: [[312, 321, 'upper-runout'], [722, 732, 'mirror-approach', 35]],
  mainframe: [[54, 63, 'address-uplink'], [484, 493, 'lower-bus-flight']],
  goonworks: [[376, 386, 'grand-fold-flight'], [534, 544, 'home-flight']],
});
const lapS = (track, s) => s >= 0 && s < track.lengthM ? s : wrap(s, track.lengthM);

/** Shape the actual spline before frames, collision and meshes are built. Source JSON stays intact. */
export function shapeAerialNodes(id, nodes) {
  const smooth = x => x * x * (3 - 2 * x);
  const drops = new Map();
  for (const [from, to, name, runoutM = 45] of ANCHORS[id] || []) {
    if (!nodes[from - 3] || !nodes[to + 1]) throw Error('Missing aerial ramp nodes: ' + id);
    const drop = Math.max(3, nodes[to].p[1] - nodes[from].p[1] - 1);
    drops.set(id + '-' + name, drop);
    const distance = [0];
    for (let i = from - 3; i < nodes.length - 1; i++) {
      distance.push(distance.at(-1) + Math.hypot(...nodes[i + 1].p.map((v, k) => v - nodes[i].p[k])));
      if (i >= to && distance.at(-1) - distance[to - from + 3] >= runoutM) break;
    }
    const at = i => distance[i - from + 3];
    const landing = at(to), end = distance.filter(s => s <= landing + runoutM).at(-1);
    for (let i = from - 3; i < from - 3 + distance.length; i++) {
      const s = at(i);
      let lift;
      if (i <= from) lift = 4 * (s / at(from)) ** 2;
      else if (i < to) lift = 4 - (4 + drop) * smooth((s - at(from)) / (landing - at(from)));
      else lift = -drop * (1 - smooth(Math.min(1, (s - landing) / Math.max(1, end - landing))));
      nodes[i].p[1] += lift;
      if (i >= from && i < to) nodes[i].flags = ['jump'];
    }
  }
  return drops;
}

export function buildAerials(track) {
  return (ANCHORS[track.id] || []).map(([from, to, id, runoutM = 45]) => {
    const gapS0 = track.nodeS[from], gapS1 = track.nodeS[to];
    if (!(gapS1 > gapS0)) throw Error('Missing aerial anchors: ' + track.id + '/' + id);
    let widthM = Infinity;
    const rampS0 = track.nodeS[from - 3];
    for (let s = rampS0; s <= gapS1 + runoutM; s += 5) widthM = Math.min(widthM, track.frameAt(s).w);
    return Object.freeze({ id: track.id + '-' + id, rampS0, rampS1: gapS0,
      gapS0, gapS1, landingS0: gapS1, landingS1: gapS1 + runoutM, widthM,
      launchRiseM: 4, landingDropM: track.aerialDrops?.get(track.id + '-' + id) ?? 3 });
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
