/* hud/options.js - the player's options, kept in this browser.
 *
 * Gentle preview, steering assist and three volumes. Stored under 'glassroad.options.v1';
 * every storage access sits in try/catch (private windows, blocked storage, previews), and a
 * bad or missing record reads as the defaults. Pure apart from the injected storage.
 */

export const OPTIONS_KEY = 'glassroad.options.v1';
export const OPTION_DEFAULTS = Object.freeze({ gentlePreview: false, assist: false, volMaster: 0.8, volMusic: 0.7, volSfx: 0.9 });
const VOLS = ['volMaster', 'volMusic', 'volSfx'];

/** Merge anything over the defaults: booleans stay booleans, volumes clamp to 0..1 in 0.05 steps. */
export function cleanOptions(raw) {
  const o = { ...OPTION_DEFAULTS };
  if (!raw || typeof raw !== 'object') return o;
  for (const k of ['gentlePreview', 'assist']) if (typeof raw[k] === 'boolean') o[k] = raw[k];
  for (const k of VOLS) {
    const v = Number(raw[k]);
    if (Number.isFinite(v)) o[k] = Math.round(Math.min(1, Math.max(0, v)) * 20) / 20;
  }
  return o;
}

const store = () => { try { return globalThis.localStorage || null; } catch (e) { return null; } };

export function loadOptions(storage = store()) {
  try { return cleanOptions(JSON.parse(storage?.getItem(OPTIONS_KEY) || 'null')); } catch (e) { return cleanOptions(null); }
}

export function saveOptions(o, storage = store()) {
  try { storage?.setItem(OPTIONS_KEY, JSON.stringify(cleanOptions(o))); return true; } catch (e) { return false; }
}

/** One step of a volume (pad left / right): 0.1, clamped. */
export function stepVolume(v, dir) { return cleanOptions({ volMaster: (Number(v) || 0) + (dir < 0 ? -0.1 : 0.1) }).volMaster; }
