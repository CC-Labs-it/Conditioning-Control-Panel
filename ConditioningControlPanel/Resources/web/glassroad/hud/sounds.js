/* hud/sounds.js - which menu sound a card or lobby change asks for (polish 5, S4). Pure: no DOM, no audio.
 *
 * The cards and the lobby emit one hud event, `sound` { name }, and boot hands the name to audio.cue (audio/
 * cues-b1.js UI_CUES, recorded files through audio/sfxmap.js). Names: menu-move, menu-ok, menu-back,
 * menu-toggle, menu-deny, seat-in, seat-out, ready-on, ready-off, lobby-tick.
 */

export const UI_SOUNDS = Object.freeze(['menu-move', 'menu-ok', 'menu-back', 'menu-toggle', 'menu-deny',
  'seat-in', 'seat-out', 'ready-on', 'ready-off', 'lobby-tick']);

/** Card button actions that are a toggle or a step, not a confirm. */
const TOGGLE_ACTS = new Set(['toggle', 'cycle', 'howto-off', 'layout', 'lb-hstep', 'lb-lstep']);
const BACK_ACTS = new Set(['back', 'quit', 'menu', 'lb-back', 'lb-exit', 'lb-leave', 'lb-home']);

/** The sound for pressing a card button by its data-act. lb-ready plays nothing: the seat update answers it. */
export function actSound(act) {
  const a = String(act || '');
  if (!a || a === 'lb-ready') return null;
  if (TOGGLE_ACTS.has(a)) return 'menu-toggle';
  if (BACK_ACTS.has(a)) return 'menu-back';
  return 'menu-ok';
}

const occupied = (s) => !!s && (s.kind === 'human' || s.kind === 'bot') && !s.left;

/**
 * One sound for a seats update (prev -> next arrays of hud/lobbyMath.js seats): someone sat down, someone
 * left, someone got ready, someone stopped being ready, in that order of weight; null when nothing a player
 * hears changed (looks, idle and pause flags stay quiet).
 */
export function seatSound(prev, next) {
  if (!Array.isArray(prev) || !Array.isArray(next)) return null;
  let inN = 0, outN = 0, readyN = 0, unreadyN = 0;
  const n = Math.max(prev.length, next.length);
  for (let i = 0; i < n; i++) {
    const a = prev[i], b = next[i], ia = occupied(a), ib = occupied(b);
    if (!ia && ib) inN++;
    else if (ia && !ib) outN++;
    else if (ia && ib) {
      if (!a.ready && b.ready) readyN++;
      else if (a.ready && !b.ready) unreadyN++;
    }
  }
  return inN ? 'seat-in' : outN ? 'seat-out' : readyN ? 'ready-on' : unreadyN ? 'ready-off' : null;
}

/**
 * The grid count's next bell on server time: the next whole second before countAt (countAt - k s, k >= 1),
 * as { atMs, waitMs, left } (left = the seconds the label shows after it), or null when none is left.
 */
export function nextGridBell(nowMs, countAt) {
  if (!(countAt > 0) || !(nowMs < countAt)) return null;
  const k = Math.floor((countAt - nowMs) / 1000 - 1e-9);
  if (k < 1) return null;
  const atMs = countAt - k * 1000;
  return { atMs, waitMs: Math.max(0, atMs - nowMs), left: k };
}
