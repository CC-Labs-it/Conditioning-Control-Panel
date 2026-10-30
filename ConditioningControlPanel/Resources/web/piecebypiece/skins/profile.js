/* ============================================================================
 * skins/profile.js - the players' profile frames and avatars on the name tags
 * (contract 4, "others see it"; no three.js or DOM here, so node can test it).
 *
 * The art is the Prize Counter's profile family (Astra's 512 px PNGs, copied
 * from Resources/cosmetics/shop/ into assets/profile/). A seat's cosmetics ride
 * the match state and the watch head as
 *
 *   cosmetics: { white: { avatar_id, frame_id }, black: { avatar_id, frame_id } }
 *
 * clamped by the server to each player's grants. This side only drops ids it
 * has no art for. A server that sends nothing leaves the plain side discs.
 * ==========================================================================*/

export const FRAMES = Object.freeze(['profile.frame.gilt', 'profile.frame.ink-stamp', 'profile.frame.lace', 'profile.frame.polaroid', 'prime.frame.room-host']);
export const AVATARS = Object.freeze(['profile.avatar.emi-pixel', 'profile.avatar.rain-portrait']);

/** A frame PNG's transparent window as a share of its canvas (ShopCosmetics.FrameWindowRatio on the desktop). */
export const FRAME_WINDOW = 0.78;

const BASE = './assets/profile/';

/** One seat's cosmetics to { avatar, frame } (known ids or null), or null when it wears nothing drawable. */
export function normCosmetics(raw) {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return null;
  const avatar = AVATARS.includes(raw.avatar_id) ? raw.avatar_id : null;
  const frame = FRAMES.includes(raw.frame_id) ? raw.frame_id : null;
  return avatar || frame ? { avatar, frame } : null;
}

/** Both seats off a match state or watch head, or null when it carries no cosmetics at all. */
export function seatCosmetics(src) {
  const c = src && typeof src === 'object' ? src.cosmetics : null;
  if (!c || typeof c !== 'object' || Array.isArray(c)) return null;
  return { white: normCosmetics(c.white), black: normCosmetics(c.black) };
}

export const artUrl = (id) => BASE + id + '.png';

/** A stable key for a seat's cosmetics, so a steady state never repaints a tag. */
export const faceKey = (c) => (c ? (c.avatar || '') + '|' + (c.frame || '') : '');
