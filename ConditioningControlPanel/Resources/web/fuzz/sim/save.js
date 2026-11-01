/* ============================================================================
 * sim/save.js - one versioned save string. localStorage in the browser, a plain
 * string anywhere else (the pause card's Export shows it). Parsing never
 * throws and never trusts the input: unknown ids are dropped, numbers clamped.
 * Tested by tests/save.test.mjs.
 * ==========================================================================*/

import { BY_ID } from '../data/items.js';
import { createState, MAX_PACE } from './economy.js';

export const SAVE_KEY = 'fuzz.save.v1';
export const SAVE_VERSION = 1;
export const SAVE_EVERY_MS = 5000;

const num = (v, lo = 0, hi = Number.MAX_SAFE_INTEGER) => (Number.isFinite(v) ? Math.min(hi, Math.max(lo, v)) : lo);

export function serialize(s, extras = {}) {
  return JSON.stringify({ v: SAVE_VERSION, s, ...extras });
}

/** Returns {state, extras} or null when the string is not a save we can read. */
export function parse(raw, { now = 0, knownAchievements = null } = {}) {
  if (typeof raw !== 'string' || raw.length > 200_000) return null;
  let data;
  try { data = JSON.parse(raw); } catch { return null; }
  if (!data || data.v !== SAVE_VERSION || !data.s || typeof data.s !== 'object') return null;
  const src = data.s;
  const s = createState({ now, pace: num(src.pace, 1, MAX_PACE) });
  s.fuzz = num(src.fuzz);
  s.total = Math.max(s.fuzz, num(src.total));
  s.clicks = num(src.clicks);
  s.crits = num(src.crits);
  s.bounces = num(src.bounces);
  s.corners = num(src.corners);
  for (const [id, n] of Object.entries(src.owned || {})) {
    const item = BY_ID[id];
    if (!item) continue;
    const cap = item.grow ? (item.max ?? Infinity) : 1;
    const count = Math.floor(num(n, 0, cap));
    if (count > 0) s.owned[id] = count;
  }
  s.bought = Array.isArray(src.bought) ? src.bought.filter((id) => typeof id === 'string' && BY_ID[id]) : [];
  s.achievements = Array.isArray(src.achievements)
    ? src.achievements.filter((id) => typeof id === 'string' && (!knownAchievements || knownAchievements.includes(id)))
    : [];
  for (const k of Object.keys(s.sources)) s.sources[k] = num(src.sources?.[k]);
  s.sparkles = num(src.sparkles);
  s.startedAt = Number.isFinite(src.startedAt) ? src.startedAt : now;
  s.ended = !!src.ended && !!s.owned.drop;
  s.endedAt = s.ended ? num(src.endedAt) : 0;
  const { v, s: _ignored, ...extras } = data;
  return { state: s, extras };
}

/** Browser storage helpers: every call swallows the quota and privacy errors. */
export function load(storage, opts) {
  try { const raw = storage?.getItem(SAVE_KEY); return raw ? parse(raw, opts) : null; } catch { return null; }
}

export function store(storage, s, extras) {
  try { storage?.setItem(SAVE_KEY, serialize(s, extras)); return true; } catch { return false; }
}

export function forget(storage) {
  try { storage?.removeItem(SAVE_KEY); return true; } catch { return false; }
}
