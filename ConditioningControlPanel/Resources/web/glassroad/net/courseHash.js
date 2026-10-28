import { GOONWORKS_CONTACT } from '../track/goonworks-setpieces.js';
import { BOUDOIR_PRESS_REVISION } from '../track/boudoir-setpieces.js';
import { AERIAL_REVISION } from '../track/aerial.js';
import { BOOST_SAFETY_REVISION } from '../track/boostSafety.js';

/** Match the shipped course and tuning before admitting a kart to an online room. */
export async function courseHash(trackJson, feelJson, runtimeCourse = null) {
  const data = [trackJson, feelJson];
  if (runtimeCourse) data.push(runtimeCourse);
  const bytes = new TextEncoder().encode(JSON.stringify(data));
  const digest = await globalThis.crypto.subtle.digest('SHA-256', bytes);
  return Array.from(new Uint8Array(digest), b => b.toString(16).padStart(2, '0')).join('');
}

/** The hash a room admits: the shipped course and tuning plus the runtime course built from them (aerial crossings,
 * moving traps). `track` = loadTrack(trackJson, { moving: true }). The page, dev/netbots.mjs and the room's
 * gen-track-cards.mjs all call this, so a room card always matches a browser seat. */
export function onlineCourseHash(trackJson, feelJson, track) {
  return courseHash(trackJson, feelJson, { aerialRevision: AERIAL_REVISION, aerials: track.aerials, moving: track.moving,
    courseShapeRevision: track.courseShape?.revision ?? 0, courseShapeNodes: track.courseShapeNodes ?? null,
    boostSafetyRevision: BOOST_SAFETY_REVISION, boosts: track.boosts,
    ...(track.id === 'goonworks' ? { goonworksContact: GOONWORKS_CONTACT } : {}),
    ...(track.id === 'boudoir' ? { boudoirPressRevision: BOUDOIR_PRESS_REVISION } : {}) });
}
