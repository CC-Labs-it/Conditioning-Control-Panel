// Keep the whole outlined glyph inside its atlas cell and the whole strip inside the road.
import { wrap, clamp } from '../core/math.js';
import { clearWordLane } from './surfaceDecals.js';
import threshold from './roadWordProfiles/threshold.js';
import keep from './roadWordProfiles/keep.js';
import mainframe from './roadWordProfiles/mainframe.js';
import boudoir from './roadWordProfiles/boudoir.js';
import goonworks from './roadWordProfiles/goonworks.js';

const profiles = { threshold, keep, mainframe, boudoir, goonworks };
export const roadLetteringProfile = id => profiles[id] || threshold;

export function glyphFit(metrics, px, rowPx, stroke, stretch) {
  const ascent = metrics.actualBoundingBoxAscent || px * .8;
  const descent = metrics.actualBoundingBoxDescent || px * .2;
  const height = ascent + descent + 2 * px * stroke / stretch;
  return Math.min(1, rowPx * .76 / Math.max(1, height));
}

/** One bounded lane-follow update; time is the song clock, so pause freezes it exactly. */
export function letteringLane(previous, target, dt) {
  if (!Number.isFinite(previous)) return target;
  return previous + clamp(target - previous, -Math.max(0, dt) * 1.1, Math.max(0, dt) * 1.1);
}

export function letteringFootprint(track, s, desiredLane, desiredWidth, lenM) {
  let narrow = Infinity;
  for (let i = 0; i <= 8; i++) narrow = Math.min(narrow, track.frameAt(wrap(s + lenM * (i / 8 - .5), track.lengthM)).w);
  const width = Math.max(.1, Math.min(desiredWidth, narrow - 1.4));
  const room = Math.max(0, (narrow - width) / 2 - .65);
  const lane = clamp(clearWordLane(track, s, desiredLane, width, lenM), -room, room);
  return { width, lane };
}
