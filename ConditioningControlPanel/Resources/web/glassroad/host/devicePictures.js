/* host/devicePictures.js - the phone's Scrolller pictures on the real world screens (CONTRACT.md section 7, device origin).
 *
 * The phone host caches each picture and serves it same-origin from the page's own loopback server, so these are
 * origin-clean textures, unlike the web's display-only route (render/webMediaScreens.js). This is only a provider:
 * pixels reach the screens through the renderer's existing prewarmed owners (render/media.js setSource + swapStill),
 * so there is no new material, no new upload path and no shader link mid-race.
 *
 * Budgets. Owners = media.videoCapacity (phone 3): one texture and one material each, shared by all their faces.
 * One decode in flight at a time, off the main thread (img.decode + createImageBitmap); long edge at most PICTURE_EDGE,
 * however large the host's file is. Each picture costs one budgeted upload. Canvases are a small reused ring
 * (owners + 1), since Safari frees dropped canvases late. The rotation walks the owners so each one changes every
 * TURN_S (GENTLE_TURN_S on Gentle Visuals), the same seven seconds the web screens use, and it never drops a face to
 * its placeholder between pictures. Paused, nothing moves or decodes. The update runs in the race frame only, so
 * nothing here competes with the shader warm behind the menu. Source-off, consent-off, expiry and a course switch
 * clear everything at once, including while paused.
 */
import { selectScrolllerSlots } from '../render/mediaSelection.js';

export const PICTURE_EDGE = 640;
export const TURN_S = 7, GENTLE_TURN_S = 14;
const RETRY_S = 1, ALL_FAILED_RETRY_S = 30;
const NO_ROWS = Object.freeze([]);

/** Whole-pixel size with the long edge at most `edge`, keeping the aspect; never enlarges. */
export function fitEdge(w, h, edge = PICTURE_EDGE) {
  const k = Math.min(1, edge / Math.max(w, h, 1));
  return { w: Math.max(1, Math.round(w * k)), h: Math.max(1, Math.round(h * k)) };
}

/** Decode one same-origin picture into `canvas` off the main thread where the browser can. Throws when unusable. */
export async function decodeInto(url, canvas, { makeImage = () => new Image(), edge = PICTURE_EDGE } = {}) {
  const img = makeImage();
  img.decoding = 'async';
  img.src = url;
  try {
    await img.decode();
    const { w, h } = fitEdge(img.naturalWidth, img.naturalHeight, edge);
    let bitmap = null;
    try { bitmap = await globalThis.createImageBitmap?.(img, { resizeWidth: w, resizeHeight: h, resizeQuality: 'medium' }); }
    catch { bitmap = null; }   // older WebKit refuses the options: the decoded image draws just as well
    canvas.width = w; canvas.height = h;
    canvas.getContext('2d', { alpha: false }).drawImage(bitmap || img, 0, 0, w, h);
    bitmap?.close?.();
  } finally {
    try { img.removeAttribute('src'); } catch { /* gone */ }
  }
}

export function createDevicePictures(media, { feed, active = () => true, makeCanvas = () => document.createElement('canvas'), decode = decodeInto } = {}) {
  const faces = selectScrolllerSlots(media?.slots || [], { share: media?.scrolllerShare });
  const owners = [], ring = [];   // owners: { canvas, url, gen, slots } in rotation order; ring: free canvases
  let made = 0, rows = NO_ROWS, gen = 0, cursor = 0, turn = 0, clock = 0, waitS = 0, failures = 0;
  let loading = null, next = null, disposed = false, swaps = 0, ownerCount = 0;
  const capacity = () => {
    const c = media?.videoCapacity;
    return Math.min(faces.length, Number.isFinite(c) ? Math.max(0, Math.floor(c)) : 0);
  };
  const live = () => !disposed && active() && feed ? feed.deviceRows : NO_ROWS;
  const takeCanvas = () => { if (ring.length) return ring.pop(); made++; return makeCanvas(); };
  function adopt(list) {
    rows = list; gen++; cursor = 0; failures = 0; waitS = 0;
    if (next) { ring.push(next.canvas); next = null; }
    if (rows.length) return;
    for (const o of owners) {
      for (const id of o.slots) media.setSource(id, null);
      ring.push(o.canvas);
    }
    owners.length = 0; ownerCount = 0; turn = 0;
  }
  /** Bring a new owner's faces up (a full pool leaves them on their Loom placeholders). False = refused. */
  function attach(pic) {
    const n = capacity(), k = ownerCount;
    const ids = faces.filter((_, i) => i % n === k).map((s) => s.id);
    let taken = false;
    for (const id of ids) if (media.setSource(id, { kind: 'canvas', el: pic.canvas, borrowed: true, credit: 'scrolller' }) !== false) taken = true;
    if (!taken) { ring.push(pic.canvas); return false; }
    owners.push({ canvas: pic.canvas, url: pic.url, gen: pic.gen, slots: ids }); ownerCount++;
    return true;
  }
  /** Is another picture wanted? An owner still to fill, one from an older feed, or more rows than owners. */
  const wanted = () => rows.length > 0 && capacity() > 0
    && (ownerCount < capacity() || owners.some((o) => o.gen !== gen) || rows.length > ownerCount);
  function load() {
    if (loading || next || waitS > 0 || !wanted()) return;
    const url = rows[cursor++ % rows.length].url, canvas = takeCanvas(), mine = gen;
    loading = Promise.resolve().then(() => decode(url, canvas)).then(() => {
      if (disposed) return;
      if (mine !== gen) { ring.push(canvas); return; }
      failures = 0; next = { canvas, url, gen: mine };
    }, () => {
      if (disposed) return;
      ring.push(canvas);
      if (mine !== gen) return;
      failures++; waitS = failures >= rows.length ? ALL_FAILED_RETRY_S : RETRY_S;
    }).finally(() => { loading = null; });
  }
  function place(gentle) {
    if (!next) return;
    if (ownerCount < capacity()) { const pic = next; next = null; attach(pic); clock = 0; return; }
    if (ownerCount === 0) return;
    const every = (gentle ? GENTLE_TURN_S : TURN_S) / ownerCount;
    const stale = owners.findIndex((o) => o.gen !== gen);
    if (clock < every) return;
    const k = stale >= 0 ? stale : turn++ % ownerCount, o = owners[k];
    if (!media.swapStill(o.canvas, next.canvas)) return;
    ring.push(o.canvas);
    o.canvas = next.canvas; o.url = next.url; o.gen = next.gen;
    next = null; clock = 0; swaps++;
  }
  const sync = () => { const list = live(); if (list !== rows) adopt(list); };
  /** Distinct pictures on the screens now: a burst never repeats one (a short feed may fill two owners with one row). */
  const flashable = () => {
    if (!live().length) return NO_ROWS;
    const seen = new Set();
    return owners.filter((o) => !seen.has(o.url) && seen.add(o.url)).map((o) => o.canvas);
  };
  return {
    sync,
    update(dt, { paused = false, gentle = false } = {}) {
      sync();
      if (paused || disposed) return;
      const step = Math.max(0, dt || 0);
      clock += step; waitS = Math.max(0, waitS - step);
      place(gentle);
      load();
    },
    /** A picture on the screens for the flash stills and pickup fronts (a canvas: never a clip), or null. */
    sourceForFlash(seed = 0) {
      const list = flashable();
      return list.length ? list[Math.abs(Math.trunc(seed)) % list.length] : null;
    },
    get flashCount() { return flashable().length; },
    stats: () => ({ rows: rows.length, owners: ownerCount, canvases: made, swaps, turn, decoding: !!loading, waiting: !!next }),
    dispose() {
      if (disposed) return;
      adopt(NO_ROWS);
      disposed = true;
    },
  };
}
