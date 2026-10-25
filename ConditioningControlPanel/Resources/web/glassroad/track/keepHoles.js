/* Missing slate slabs are authored once and consumed by the existing court support and mesh path. */
import { getKeepLayout } from './keep-layout.js';

const SITES = Object.freeze([
  ['cellblock-approach', 35, 11, 6, 12],
  ['tower-spiral', 975, 7, 3, 14],
  ['north-approach', 445, -8, 6, 12],
  ['crown-walk', 76, -10, 6, 14], ['west-descent', 45, 0, 6, 12],
  ['outer-rampart', 76, -11, 8, 16], ['serpentine-cross', 48, 8, 5, 12],
  ['moat-return', 72, 8, 6, 14], ['south-tower', 65, -8, 5, 12],
].map(Object.freeze));

/** Offline authoring emits ordinary node-anchored courts, so the host and client share the openings. */
export function keepHoleCourts(track) {
  if (track.id !== 'keep') return [];
  const layout = getKeepLayout(track);
  const before = s => Math.max(0, track.nodeS.findLastIndex(value => value <= s));
  return SITES.map(([id, distance, x, w, len], index) => {
    const section = layout.landmarks.find(row => row.id === id), s = section.s0 + distance;
    const node = before(s), from = before(s - 24), to = Math.min(track.nodeCount, before(s + len + 26) + 1);
    return { id: 'keep-worn-' + index, from, to,
      holes: [{ id: 'keep-missing-slab-' + index, node, ds: +(s - track.nodeS[node]).toFixed(3), x, w, len }] };
  });
}

/** Retain the established pad positions, removing only pads whose full rollout can enter a new hole. */
export function filterKeepHoleBoosts(track, boosts) {
  const holes = (track.courts || []).flatMap(c => c.holes), L = track.lengthM;
  return boosts.filter(pad => {
    const s = track.nodeS[pad.node];
    return !holes.some(h => Math.abs(h.x - pad.x) <= (h.w + pad.w) / 2 + .65 &&
      [-L, 0, L].some(shift => h.s + shift <= s + pad.len + 60 && h.s + h.len + shift >= s - 8));
  });
}
