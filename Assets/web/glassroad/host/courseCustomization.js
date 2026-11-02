import { COURSES } from '../track/catalog.js';

export const CUSTOMIZATION_KEY = 'glassroad.courseMedia.v1';
const validCourse = id => COURSES.some(c => c.id === id);
const storage = () => { try { return globalThis.localStorage; } catch { return null; } };
export function loadCustomization(store = storage()) {
  try {
    const raw = JSON.parse(store?.getItem(CUSTOMIZATION_KEY) || '{}'), out = {};
    for (const [id, value] of Object.entries(raw || {})) {
      if (!validCourse(id) || !value || typeof value !== 'object') continue;
      out[id] = {};
      if (['files', 'scrolller', 'off'].includes(value.source)) out[id].source = value.source;
      if (typeof value.songId === 'string' && /^[a-z0-9-]{1,40}$/.test(value.songId)) out[id].songId = value.songId;
    }
    return out;
  } catch { return {}; }
}
export function saveCustomization(picks, store = storage()) {
  try { store?.setItem(CUSTOMIZATION_KEY, JSON.stringify(picks)); } catch { /* this visit only */ }
}
/** Reorder only host-validated song rows. Stored choices never become fetch URLs. */
export function selectedSongCharts(pick, rows, defaults) {
  if (!pick || pick === 'default') return defaults;
  const url = rows.find(row => row.id === pick)?.url;
  return url ? [url] : defaults;
}
