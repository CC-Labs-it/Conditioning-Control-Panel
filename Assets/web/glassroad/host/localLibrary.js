/* host/localLibrary.js - "My files": the player's own pictures and clips in place of Scrolller (tester #7, 2026-10-28).
 *
 * The files never leave the device. Each one becomes a blob: object url in this page (nothing is uploaded, nothing
 * goes over the host bridge or the room), and every url is revoked when the pick is replaced, the source changes or
 * the page lets go of it. Only the CHOICE is remembered (MEDIA_SOURCE_KEY); the files are picked again after a reload.
 * While My files is on the race's Scrolller switch is off, so the host fetches nothing from Scrolller.
 *
 * The library has the Scrolller feed's shape (host/scrolllerFeed.js), so the same display-only world screens
 * (render/webMediaScreens.js) and the flash-burst stills take it unchanged; `credit` is '' (no Scrolller label).
 */
import { MAX_FEED_ROWS, isVideo, displayVideo } from './scrolllerFeed.js';

export const MEDIA_SOURCE_KEY = 'glassroad.mediaSource.v1';
/** At most this many files are shown from one pick, the same cap as a Scrolller feed. */
export const MAX_LIBRARY_FILES = MAX_FEED_ROWS;
export const SOURCES = Object.freeze(['scrolller', 'files', 'off']);
const LABELS = Object.freeze({ scrolller: 'Scrolller', files: 'My files', off: 'Off' });
export const sourceLabel = (mode) => LABELS[mode] || LABELS.off;
/** The Options button steps Scrolller, My files, Off, then round again. */
export const nextSource = (mode) => SOURCES[(SOURCES.indexOf(mode) + 1) % SOURCES.length] || 'scrolller';
/** Where the pictures come from: the local choice wins, else the race's stored Scrolller switch. */
export const sourceMode = ({ files = false, consent = false } = {}) => files ? 'files' : consent ? 'scrolller' : 'off';

const store = () => { try { return globalThis.localStorage || null; } catch { return null; } };
export function loadFilesChoice(storage = store()) {
  try { return storage?.getItem(MEDIA_SOURCE_KEY) === 'files'; } catch { return false; }
}
export function saveFilesChoice(on, storage = store()) {
  try { if (on) storage?.setItem(MEDIA_SOURCE_KEY, 'files'); else storage?.removeItem(MEDIA_SOURCE_KEY); } catch { /* private mode */ }
}

const kindOf = (f) => /^image\//.test(f?.type || '') ? 'image' : /^video\//.test(f?.type || '') ? 'video' : '';
/** The files a pick may use: pictures and clips only, deduped by name + size, at most `max`. A bigger pick is
 *  sampled at random (`rng`) so a folder of hundreds still shows a mix, never only its first files. */
export function pickFiles(list, { max = MAX_LIBRARY_FILES, rng = Math.random } = {}) {
  const seen = new Set(), ok = [];
  for (const f of Array.from(list || [])) {
    const key = (f?.name || '') + ':' + (f?.size || 0);
    if (!kindOf(f) || !(f.size > 0) || seen.has(key)) continue;
    seen.add(key); ok.push(f);
  }
  for (let i = ok.length - 1; i > 0 && ok.length > max; i--) {
    const j = Math.floor(rng() * (i + 1)); [ok[i], ok[j]] = [ok[j], ok[i]];
  }
  return ok.slice(0, Math.max(0, max));
}

const loaded = (el) => isVideo(el) ? !el.error && el.readyState >= 1 : el.complete && el.naturalWidth > 0;

export function createLocalLibrary({ makeImage = () => new Image(), makeVideo = () => displayVideo(),
  createURL = (f) => URL.createObjectURL(f), revokeURL = (u) => URL.revokeObjectURL(u) } = {}) {
  let items = [];   // [{ el, url }]
  function clear() {
    for (const { el, url } of items) {
      try { el.removeAttribute('src'); if (isVideo(el)) el.load?.(); } catch { /* gone */ }
      try { revokeURL(url); } catch { /* already revoked */ }
    }
    items = [];
  }
  const ready = () => items.map((i) => i.el).filter(loaded);
  return {
    credit: '',
    /** Replace the pick: the old urls are revoked first. Returns how many files are kept. */
    setFiles(list, opts) {
      clear();
      for (const f of pickFiles(list, opts)) {
        const el = kindOf(f) === 'video' ? makeVideo() : makeImage();
        if (!el) continue;
        const url = createURL(f);
        if (isVideo(el)) el.preload = 'metadata'; else el.decoding = 'async';
        el.src = url; items.push({ el, url });
      }
      return items.length;
    },
    clear,
    get count() { return items.length; },
    get ready() { return ready().length; },
    get stillCount() { return ready().filter((el) => !isVideo(el)).length; },
    sourceFor(seed = 0) { const r = ready(); return r.length ? r[Math.abs(Math.trunc(seed)) % r.length] : null; },
    stillFor(seed = 0) {
      const r = ready().filter((el) => !isVideo(el));
      return r.length ? r[Math.abs(Math.trunc(seed)) % r.length] : null;
    },
    dispose: clear,
  };
}

/** One feed for the screens and the flash burst that follows the current source (`get` returns a feed or null). */
export function feedSwitch(get) {
  const f = () => get() || null;
  return {
    get credit() { return f()?.credit ?? 'via Scrolller'; },
    get count() { return f()?.count || 0; },
    get ready() { return f()?.ready || 0; },
    get stillCount() { return f()?.stillCount || 0; },
    sourceFor(seed) { return f()?.sourceFor(seed) || null; },
    stillFor(seed) { return f()?.stillFor(seed) || null; },
  };
}

/** Open the device's file picker (call it inside the click). `onFiles` gets the FileList; nothing is read here. */
export function openFilePicker(onFiles, doc = globalThis.document) {
  const input = doc?.createElement?.('input');
  if (!input) return false;
  input.type = 'file'; input.multiple = true; input.accept = 'image/*,video/*';
  input.style.cssText = 'position:fixed;left:-9999px;width:1px;height:1px;opacity:0';
  const done = () => { input.remove?.(); };
  input.addEventListener('change', () => { try { if (input.files?.length) onFiles(input.files); } finally { done(); } }, { once: true });
  input.addEventListener('cancel', done, { once: true });
  doc.body?.append(input);
  input.click();
  return true;
}
