/* Window and mixer preferences outlive a run. Preview preferences never share the normal save key. */
export const PREVIEW_PREFS_KEY = 'fuzz.preview.prefs.v1';
const object = value => value && typeof value === 'object' && !Array.isArray(value) ? value : {};
export function readPreviewPreferences(storage) {
  try {
    const text = storage?.getItem(PREVIEW_PREFS_KEY);
    if (!text || text.length > 100000) return {};
    const value = object(JSON.parse(text));
    return { windowLayouts: object(value.windowLayouts), audioMix: object(value.audioMix),
      ...(value.buttonWardrobe ? {buttonWardrobe:object(value.buttonWardrobe)} : {}),
      muted: value.muted === true, masterVolume: Number.isFinite(value.masterVolume) ? Math.min(1, Math.max(0, value.masterVolume)) : 1 };
  } catch { return {}; }
}
export function writePreviewPreferences(storage, extras) {
  try { storage?.setItem(PREVIEW_PREFS_KEY, JSON.stringify({ windowLayouts: extras.windowLayouts, audioMix: extras.audioMix, buttonWardrobe:extras.buttonWardrobe, muted: extras.muted, masterVolume: extras.masterVolume })); } catch { /* browser storage may be unavailable */ }
}
export function windowPreferences(extras) {
  const layouts = object(extras.windowLayouts);
  extras.windowLayouts = layouts;
  return {
    get(key, legacy = {}) { return object(layouts[key] || legacy); },
    set(key, value) { layouts[key] = { ...value }; },
  };
}
