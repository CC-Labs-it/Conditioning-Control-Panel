/* Media faces come from the same prison walls, gatehouses and tower tablets as their backing. */
import { getKeepLayout } from '../track/keep-layout.js';
import { prisonPlan } from './keepPrison.js';
import { axesFrom } from './mediaMath.js';

// Omit faces crossed by adjoining wings or roof ribs, plus paired faces for the even media mix.
const BLOCKED_MOUNTS = new Set([
  'main-prison-wing-2-facade', 'main-prison-wing-3-facade',
  'outer-gate-gate-1', 'cell-gallery-gate-1', 'chain-hall-gate-1', 'high-gate-gate-1',
  'high-gate-gate--1', 'cell-gallery-gate--1',
]);

export function keepMediaLayout(track) {
  const course = getKeepLayout(track); if (!course) return null;
  const slots = prisonPlan(track, course).mounts.filter(mount => !BLOCKED_MOUNTS.has(mount.id)).map((mount, i) => ({
    id: 'keep-media-' + mount.id, mountId: mount.id, frame: mount.frame, s: mount.s,
    kind: 'landscape', w: mount.w, h: mount.h, phone: mount.phone,
    // Stand beyond the outermost bars, window sills and tower buttresses.
    program: Math.floor(i / 2) % 2 ? 3 : 0, C: mount.p.map((v, axis) => v + mount.t[axis] * 1.2), ...axesFrom(mount.t, mount.n),
  }));
  return { track: 'keep', slots };
}