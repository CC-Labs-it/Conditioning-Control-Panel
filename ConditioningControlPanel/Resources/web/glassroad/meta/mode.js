/* meta/mode.js - Race or Time Trial (CONTRACT-LOGBOOK.md section 7).
 *
 * `?mode=race` / `?mode=trial` on the url, else the last pick from the garage (localStorage, always in
 * try/catch). Online is always a race: sim-changing modes stay solo until the room's admission hash
 * carries the mode.
 */
export const MODES = Object.freeze(['race', 'trial']);
export const MODE_KEY = 'glassroad.mode.v1';
export const MODE_LABEL = Object.freeze({ race: 'Race', trial: 'Time trial' });

/** @returns {'race'|'trial'|null} the url's mode, null when it names none (or an unknown one) */
export function parseMode(search = '') {
  let m = null;
  try { m = new URLSearchParams(search).get('mode'); } catch (e) { return null; }
  return MODES.includes(m) ? m : null;
}

export function loadMode(storage = defaultStorage()) {
  try { const m = storage && storage.getItem(MODE_KEY); return MODES.includes(m) ? m : 'race'; } catch (e) { return 'race'; }
}

/** @returns {boolean} true when written */
export function saveMode(mode, storage = defaultStorage()) {
  if (!MODES.includes(mode)) return false;
  try { if (!storage) return false; storage.setItem(MODE_KEY, mode); return true; } catch (e) { return false; }
}

/** The mode the next run uses: online race, else the url, else the saved pick. */
export function resolveMode({ search = '', storage = defaultStorage(), online = false } = {}) {
  if (online) return 'race';
  return parseMode(search) || loadMode(storage);
}

function defaultStorage() { try { return globalThis.localStorage || null; } catch (e) { return null; } }
