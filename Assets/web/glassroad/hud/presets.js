/* hud/presets.js - preset chat for online races (NET.md section 4, `emote { code }`). Pure: no DOM, no three.
 *
 * Friends law: no free text anywhere. A player picks one of these ids; only the id travels, each client prints
 * its own copy. The record's pose byte carries `POSE_BASE + index` while the emote plays, so a remote kart shows
 * it with Astra's shared kart clips (art/models/emi-kart-event-bindings.json: wave, cheer, sulk, countdown_ready)
 * and a face from the emi-faces atlas. The room allows one emote per 2 s per socket.
 */

export const POSE_BASE = 16;
export const EMOTE_GAP_MS = 2000;
/** How long a bubble stays on a seat row (its IN and OUT included). */
export const EMOTE_SHOW_MS = 3000;

/** Clip lengths from the bindings file (durationSec), used to hold the pose byte while the clip plays. */
export const CLIP_S = Object.freeze({ wave: 1.5, cheer: 1.6, sulk: 1.2, countdown_ready: 0.7 });

const P = (code, text, clip, face) => Object.freeze({ code, text, clip, face });
/** Order is the wire: index i rides the pose byte as POSE_BASE + i. Append only. */
export const PRESETS = Object.freeze([
  P('hi', 'Hi', 'wave', 'happy'),
  P('ready', 'Ready', 'countdown_ready', 'determined'),
  P('gl', 'Good luck', 'wave', 'wink'),
  P('gg', 'Good race', 'cheer', 'cheer'),
  P('nice', 'Nice line', 'cheer', 'sparkle'),
  P('oops', 'Oops', 'sulk', 'sad'),
  P('again', 'Again?', 'wave', 'grin'),
  P('sec', 'One sec', null, 'sleepy'),
  P('cute', 'Cute kart', 'wave', 'heart'),
  P('bye', 'Off I go', 'wave', 'smug'),
]);

const BY_CODE = new Map(PRESETS.map((p, i) => [p.code, i]));

export const isPreset = (code) => BY_CODE.has(code);
/** The preset for a code, or null (an unknown code is never printed). */
export const presetOf = (code) => (BY_CODE.has(code) ? PRESETS[BY_CODE.get(code)] : null);
/** The pose byte for a code, or 0. */
export const poseOf = (code) => (BY_CODE.has(code) ? POSE_BASE + BY_CODE.get(code) : 0);
/** The preset a pose byte names, or null (0..15 are trick poses, never emotes). */
export function presetByPose(pose) {
  const i = (pose | 0) - POSE_BASE;
  return i >= 0 && i < PRESETS.length ? PRESETS[i] : null;
}
/** Seconds the pose byte holds for a code: its clip, or a short face-only beat. */
export const poseHoldS = (code) => { const p = presetOf(code); return p ? (p.clip ? CLIP_S[p.clip] : 1) : 0; };
/** True when a new emote may go out (one per EMOTE_GAP_MS). */
export const emoteAllowed = (lastAt, now) => !(now - lastAt < EMOTE_GAP_MS);
