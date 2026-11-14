/* render/nameTag.js - the three-free name tag law over rival karts, so tests run it under node.
 *
 * Two styles (owner, 2026-10-30: "we need to make the players (human) visible with a tag"):
 * - bot: the small muted pill with the kart's colour dot, only near (LOOK.tagNearM, inside the rival LOD).
 * - human: a remote player in an online room (a seat meta that is not a bot) gets a bright pink pill with
 *   the display name, drawn over traffic (no depth test), seen out to HUMAN_TAG.farM and past the rival LOD,
 *   held at a minimum on-screen size so it stays readable at distance.
 * Every tag fades IN when its kart shows and OUT when it goes (distance, hide or a seat that left). The
 * only motion is a small grow on the way in, and none at all with motion off or Gentle; nothing pulses.
 * Behind the camera a tag is a sprite like any other and is culled with it (no edge clamp).
 */
import { tagAlpha } from './kartPose.js';

export const HUMAN_TAG = {
  fill: '#ff4fa3', ring: '#ffffff', text: '#ffffff', shade: 'rgba(70, 0, 36, 0.55)',
  font: '700 38px system-ui, sans-serif',
  sizeM: [1.9, 0.46], upM: 1.95,
  farM: 160, fadeM: 30,            // full to farM - fadeM, gone past farM
  hideNearM: [3, 6],               // a human right beside the player still reads, just not at point blank
  keepM: 22, keepMax: 4,           // past keepM the tag grows with distance (up to keepMax) so it holds its size on screen
  popIn: 0.82,                     // scale at alpha 0 on the way in (motion on only)
  inS: 0.25, outS: 0.45,           // fade times
};
export const BOT_TAG_FADE = { inS: 0.2, outS: 0.3 };

/** 'human' for a remote player's seat, 'bot' for any other rival, null for the player (racer 0) or no meta. */
export function tagKind(meta, i) {
  if (!(i > 0) || !meta) return null;
  return !meta.isBot && meta.seat !== undefined && meta.seat !== null ? 'human' : 'bot';
}

/** Where the tag's alpha wants to be this frame (0..1). near = the rival is inside the pose LOD. */
export function tagTarget(kind, distM, near, L, H = HUMAN_TAG) {
  if (!kind) return 0;
  const band = (d, [h0, h1]) => { const t = h1 > h0 ? Math.min(1, Math.max(0, (d - h0) / (h1 - h0))) : 1; return t * t * (3 - 2 * t); };
  if (kind === 'human') {
    const far = 1 - Math.min(1, Math.max(0, (distM - (H.farM - H.fadeM)) / H.fadeM));
    return far * far * (3 - 2 * far) * band(distM, H.hideNearM);
  }
  return near ? tagAlpha(distM, L) * band(distM, L.tagHideNearM || [0, 0]) : 0;
}

/** Ease alpha toward target: rises over inS, falls over outS (linear, so an IN and an OUT both finish). */
export function stepTagAlpha(a, target, dt, fade = HUMAN_TAG) {
  if (!(dt > 0)) return a;
  if (target > a) return Math.min(target, a + dt / Math.max(fade.inS, 1e-3));
  return Math.max(target, a - dt / Math.max(fade.outS, 1e-3));
}

/** Tag scale factor: the human tag keeps a readable size at distance; motion on adds a small grow on the way in. */
export function tagScaleK(kind, distM, a, still, H = HUMAN_TAG) {
  const keep = kind === 'human' ? Math.min(H.keepMax, Math.max(1, distM / H.keepM)) : 1;
  return keep * (still ? 1 : H.popIn + (1 - H.popIn) * Math.min(1, Math.max(0, a)));
}
