/* host/scrolllerNiches.js - which Scrolller niches each course shows (web Online pictures, CONTRACT.md section 7).
 *
 * Every course has a default pair that fits its theme (owner, 2026-10-26: "propose the niches of scrolller to tie
 * to each track, leave them the option to customize"). A player's own pick per course is kept in this browser
 * under 'glassroad.niches.v1'; "Track default" drops it. Ids are the host catalog's (the desktop
 * FypOnlineCoordinator catalog, mirrored by the campus provider): never renamed, so a stored pick stays valid.
 * Pure apart from the injected storage; every storage access sits in try/catch.
 */
export const NICHES_KEY = 'glassroad.niches.v1';
export const MAX_NICHES = 4;
export const TRACK_NICHES = Object.freeze({
  threshold: Object.freeze(['hypno', 'bambisleep']),   // the first course: the house default pair
  mainframe: Object.freeze(['bimbo', 'hentai']),       // drone and system programming
  keep: Object.freeze(['beta', 'censored']),           // the prison castle: denial
  boudoir: Object.freeze(['sissy', 'bimbo']),          // Think Pink
  goonworks: Object.freeze(['goon', 'relapse']),       // the goon funhouse
});
const FALLBACK = TRACK_NICHES.threshold;

const store = () => { try { return globalThis.localStorage || null; } catch { return null; } };

/** Ids as strings, deduped, at most MAX_NICHES; `known` (catalog ids) filters when given. */
export function cleanPick(raw, known = null) {
  const out = [];
  for (const id of Array.isArray(raw) ? raw : []) {
    if (out.length >= MAX_NICHES) break;
    if (typeof id === 'string' && /^[a-z0-9_-]{1,24}$/.test(id) && !out.includes(id) && (!known || known.includes(id))) out.push(id);
  }
  return out;
}

export function loadPicks(storage = store()) {
  try {
    const raw = JSON.parse(storage?.getItem(NICHES_KEY) || 'null');
    if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return {};
    const out = {};
    for (const [track, ids] of Object.entries(raw)) { const p = cleanPick(ids); if (p.length) out[track] = p; }
    return out;
  } catch { return {}; }
}

/** The niches a course shows: the player's pick, else the course default. */
export function nichesFor(track, picks = loadPicks()) {
  return (picks[track]?.length ? picks[track] : TRACK_NICHES[track] || FALLBACK).slice();
}

export function isDefault(track, picks = loadPicks()) { return !picks[track]?.length; }

/** Keep a pick for a course (null or an empty list goes back to the course default). Returns the niches now in force. */
export function savePick(track, ids, storage = store()) {
  const picks = loadPicks(storage), pick = cleanPick(ids);
  if (pick.length) picks[track] = pick; else delete picks[track];
  try { storage?.setItem(NICHES_KEY, JSON.stringify(picks)); } catch { /* blocked storage: this visit only */ }
  return nichesFor(track, picks);
}

/** One chip press: on adds (up to MAX_NICHES), off removes, never below one. Returns the new list or null (refused). */
export function togglePick(current, id) {
  if (current.includes(id)) return current.length > 1 ? current.filter((x) => x !== id) : null;
  return current.length < MAX_NICHES ? [...current, id] : null;
}
