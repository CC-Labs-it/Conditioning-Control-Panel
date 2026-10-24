/* ============================================================================
 * ui/reactions-prefs.js - the two choices about the crowd (owner, 2026-10-14).
 *
 *   show    "Play watchers' reactions": a SEAT shows and plays what the stands
 *           send. Off: nothing floats, nothing sounds. On by default. The
 *           stands always see their own room.
 *   sound   the reactions' own sounds, everywhere (the mute button on the
 *           reaction bar and the Options row are the same switch). On by
 *           default. The page's "Sound effects" switch still mutes everything.
 *
 * Kept in this page's own storage (like the camera choices); every page (desktop,
 * web, phone) keeps its own. Kept apart from game/preferences.js on purpose.
 * ==========================================================================*/

const KEY = 'pbp-reactions-v1';
let saved = {};
try { saved = JSON.parse(globalThis.localStorage?.getItem(KEY) || '{}') || {}; } catch { /* private storage */ }
if (!saved || typeof saved !== 'object' || Array.isArray(saved)) saved = {};
const listeners = new Set();

export function reactPrefs() {
  return { show: saved.show !== false, sound: saved.sound !== false };
}

export function setReactPrefs(patch = {}) {
  if (typeof patch.show === 'boolean') saved.show = patch.show;
  if (typeof patch.sound === 'boolean') saved.sound = patch.sound;
  try { globalThis.localStorage?.setItem(KEY, JSON.stringify(saved)); } catch { /* session only */ }
  const p = reactPrefs();
  for (const fn of listeners) { try { fn(p); } catch { /* one listener never stops the rest */ } }
  return p;
}

/** Called at once with the current choice, then on every change. Returns the unsubscribe. */
export function onReactPrefs(fn) {
  listeners.add(fn);
  fn(reactPrefs());
  return () => listeners.delete(fn);
}

export const reactionPrefs = Object.freeze({ get: reactPrefs, set: setReactPrefs, on: onReactPrefs });
export default reactionPrefs;
