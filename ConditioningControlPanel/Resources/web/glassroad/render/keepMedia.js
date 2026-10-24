/* Media faces come from the same prison walls, gatehouses and tower tablets as their backing. */
import { getKeepLayout } from '../track/keep-layout.js';
import { prisonPlan } from './keepPrison.js';
import { axesFrom } from './mediaMath.js';

export function keepMediaLayout(track) {
  const course = getKeepLayout(track); if (!course) return null;
  const slots = prisonPlan(track, course).mounts.map((mount, i) => ({
    id: 'keep-media-' + mount.id, mountId: mount.id, frame: mount.frame, s: mount.s,
    kind: 'landscape', w: mount.w, h: mount.h, phone: mount.phone,
    program: Math.floor(i / 2) % 2 ? 3 : 0, C: [...mount.p], ...axesFrom(mount.t, mount.n),
  }));
  return { track: 'keep', slots };
}