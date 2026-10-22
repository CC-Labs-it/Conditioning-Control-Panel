/* hud/options.js - the player's options, kept in this browser.
 *
 * Gentle preview, steering assist, steering sensitivity, three volumes and the display rows (polish 2):
 * frame cap, render scale, quality, shadows and the fps readout. Music road pass 4: Steer assist (Light, Strong,
 * Off; sim/assist.js) and the how-to card's "don't show again" (howtoOff). Stored under 'glassroad.options.v1';
 * every storage access sits in try/catch (private windows, blocked storage, previews), and a bad or
 * missing record reads as the defaults. Pure apart from the injected storage.
 */

export const OPTIONS_KEY = 'glassroad.options.v1';
export const OPTION_DEFAULTS = Object.freeze({
  gentlePreview: false, assist: false, volMaster: 0.8, volMusic: 1, volSfx: 0.55, mixV: 2,
  frameCap: 'display', renderScale: 'auto', quality: 'auto', showFps: false, steerSens: 1, shadows: true,
  steerAssist: 'light', howtoOff: false,
});
const VOLS = ['volMaster', 'volMusic', 'volSfx'];
// mixV 2 (owner, 2026-10-22: effects too loud, the song and its voice too quiet): a record saved before it keeps
// every other option and takes the new volume defaults once
const MIX_V = 2;
const BOOLS = ['gentlePreview', 'assist', 'showFps', 'shadows', 'howtoOff'];

/** The choice rows: allowed values in cycle order (the first is the default). */
export const OPTION_CHOICES = Object.freeze({
  frameCap: Object.freeze(['display', 60, 30]),
  renderScale: Object.freeze(['auto', 1, 0.75, 0.5]),
  quality: Object.freeze(['auto', 'high', 'mid', 'phone']),
  steerAssist: Object.freeze(['light', 'strong', 'off']),   // sim/assist.js ASSIST_MODES, the same order
});
export const STEER_SENS = Object.freeze({ min: 0.6, max: 1.4, step: 0.1 });

/** A choice value from anything: numbers may arrive as strings (a select, a stored record). */
function choice(key, v) {
  const list = OPTION_CHOICES[key];
  for (const c of list) if (c === v || (typeof c === 'number' && v !== '' && v !== null && Number(v) === c)) return c;
  return OPTION_DEFAULTS[key];
}
const sens = (v) => {
  const x = Number(v);
  if (!Number.isFinite(x)) return OPTION_DEFAULTS.steerSens;
  return Math.round(Math.min(STEER_SENS.max, Math.max(STEER_SENS.min, x)) * 10) / 10;
};

/** Merge anything over the defaults: booleans stay booleans, volumes clamp to 0..1 in 0.05 steps,
 * choices fall back to their default, steering sensitivity clamps to 0.6..1.4 in 0.1 steps. */
export function cleanOptions(raw) {
  const o = { ...OPTION_DEFAULTS };
  if (!raw || typeof raw !== 'object') return o;
  for (const k of BOOLS) if (typeof raw[k] === 'boolean') o[k] = raw[k];
  for (const k of VOLS) {
    const v = Number(raw[k]);
    if (raw[k] !== null && raw[k] !== '' && Number.isFinite(v)) o[k] = Math.round(Math.min(1, Math.max(0, v)) * 20) / 20;
  }
  for (const k of Object.keys(OPTION_CHOICES)) if (k in raw) o[k] = choice(k, raw[k]);
  if ('steerSens' in raw) o.steerSens = sens(raw.steerSens);
  return o;
}

/** The next value of a choice row (wraps), or the previous with dir < 0. */
export function cycleOption(key, v, dir = 1) {
  const list = OPTION_CHOICES[key];
  if (!list) return v;
  const i = list.indexOf(choice(key, v));
  return list[(i + (dir < 0 ? -1 : 1) + list.length) % list.length];
}

/** Short labels for the options card. */
export function choiceLabel(key, v) {
  const c = choice(key, v);
  if (key === 'frameCap') return c === 'display' ? 'Off' : String(c);
  if (key === 'renderScale') return c === 'auto' ? 'Auto' : Math.round(c * 100) + '%';
  if (key === 'quality') return { auto: 'Auto', high: 'High', mid: 'Mid', phone: 'Low' }[c];
  if (key === 'steerAssist') return { off: 'Off', light: 'Light', strong: 'Strong' }[c];
  return String(c);
}

const store = () => { try { return globalThis.localStorage || null; } catch (e) { return null; } };

export function loadOptions(storage = store()) {
  try {
    const raw = JSON.parse(storage?.getItem(OPTIONS_KEY) || 'null');
    if (raw && typeof raw === 'object' && raw.mixV !== MIX_V) for (const k of VOLS) delete raw[k];
    return cleanOptions(raw);
  } catch (e) { return cleanOptions(null); }
}

export function saveOptions(o, storage = store()) {
  try { storage?.setItem(OPTIONS_KEY, JSON.stringify(cleanOptions(o))); return true; } catch (e) { return false; }
}

/** One step of a volume (pad left / right): 0.1, clamped. */
export function stepVolume(v, dir) { return cleanOptions({ volMaster: (Number(v) || 0) + (dir < 0 ? -0.1 : 0.1) }).volMaster; }

/** One step of any slider row (pad left / right): volumes by 0.1, steering sensitivity by 0.1. */
export function stepOption(key, v, dir) {
  if (key === 'steerSens') return sens((Number(v) || 0) + (dir < 0 ? -STEER_SENS.step : STEER_SENS.step));
  return stepVolume(v, dir);
}
