/* host/devicePictures.js - the phone's Scrolller pictures on the real world screens (CONTRACT.md section 7, device origin).
 *
 * The phone host caches each picture and serves it same-origin from the page's own loopback server, so these are
 * origin-clean textures, unlike the web's display-only route (render/webMediaScreens.js). This is only a provider:
 * pixels reach the screens through the renderer's existing prewarmed owners (render/media.js setSource + swapStill),
 * so there is no new material, no new upload path and no shader link mid-race.
 *
 * Budgets. Owners = media.videoCapacity (phone 3): one texture and one material each, shared by all their faces.
 * One decode in flight at a time, off the main thread (img.decode + createImageBitmap) and never past
 * DECODE_TIMEOUT_MS; long edge at most PICTURE_EDGE, however large the host's file is. Each picture costs one budgeted
 * upload. Canvases are a small reused ring (owners + 1), since Safari frees dropped canvases late. The rotation walks
 * the owners so each one changes every TURN_S (GENTLE_TURN_S on Gentle Visuals), the same seven seconds the web
 * screens use, and it never drops a face to its placeholder between pictures. No per-frame allocation in the tick.
 *
 * The page ticks this every frame, garage included. update(dt, paused) only ever syncs while `paused`: the caller
 * passes true whenever nothing draws the world (menu, frozen, staged warm), so no decode or swap competes with those.
 * Source-off, consent-off, expiry, a row leaving the feed and a course switch take effect at once, paused too, and
 * shrink the canvases they free so no picture's pixels stay behind.
 *
 * Flash cards read their picture every frame, so nothing is placed while a burst is live (holdFlash): a view-covering
 * card never cuts to another picture mid-hold and a burst never repeats a still. A takedown still wins: that card's
 * seed answers null at once (the burst's own placeholder) and the other cards keep theirs.
 */
import { selectScrolllerSlots } from '../render/mediaSelection.js';

export const PICTURE_EDGE = 640;
export const TURN_S = 7, GENTLE_TURN_S = 14;
export const DECODE_TIMEOUT_MS = 10000;
const RETRY_S = 1, ALL_FAILED_RETRY_S = 30;
const NO_ROWS = Object.freeze([]);
const wipe = (canvas) => { canvas.width = canvas.height = 1; };

/** Whole-pixel size with the long edge at most `edge`, keeping the aspect; never enlarges. */
export function fitEdge(w, h, edge = PICTURE_EDGE) {
  const k = Math.min(1, edge / Math.max(w, h, 1));
  return { w: Math.max(1, Math.round(w * k)), h: Math.max(1, Math.round(h * k)) };
}

/** Decode one same-origin picture into `canvas` off the main thread where the browser can. Throws when unusable or hung. */
export async function decodeInto(url, canvas, { makeImage = () => new Image(), edge = PICTURE_EDGE, timeoutMs = DECODE_TIMEOUT_MS } = {}) {
  const img = makeImage();
  img.decoding = 'async';
  img.crossOrigin = 'anonymous';   // a redirect off this origin fails instead of tainting the canvas
  img.src = url;
  const letGo = () => { try { img.removeAttribute('src'); } catch { /* gone */ } };
  let timer = 0, dead = false;
  const hung = new Promise((_, reject) => { timer = setTimeout(() => { dead = true; letGo(); reject(new Error('picture decode timed out')); }, timeoutMs); });
  try {
    await Promise.race([img.decode(), hung]);
    const { w, h } = fitEdge(img.naturalWidth, img.naturalHeight, edge);
    let bitmap = null;
    try {
      const making = globalThis.createImageBitmap?.(img, { resizeWidth: w, resizeHeight: h, resizeQuality: 'medium' });
      making?.then?.((late) => { if (dead) late?.close?.(); }, () => {});
      bitmap = await Promise.race([making, hung]);
    } catch (e) {
      if (dead) throw e;
      bitmap = null;   // older WebKit refuses the options: the decoded image draws just as well
    }
    canvas.width = w; canvas.height = h;
    canvas.getContext('2d', { alpha: false }).drawImage(bitmap || img, 0, 0, w, h);
    bitmap?.close?.();
  } finally {
    clearTimeout(timer);
    letGo();
  }
}

function hasRow(list, url) {
  for (let i = 0; i < list.length; i++) if (list[i].url === url) return true;
  return false;
}
function sameRows(a, b) {
  if (a.length !== b.length) return false;
  for (let i = 0; i < a.length; i++) if (a[i].url !== b[i].url) return false;
  return true;
}

export function createDevicePictures(media, { feed, active = () => true, makeCanvas = () => document.createElement('canvas'), decode = decodeInto } = {}) {
  const faces = selectScrolllerSlots(media?.slots || [], { share: media?.scrolllerShare });
  const owners = [], ring = [];   // owners: { canvas, url, k, slots } in rotation order; ring: free canvases
  const flashList = [];           // the distinct pictures on the screens; a taken-down one reads null until the burst ends
  let made = 0, rows = NO_ROWS, cursor = 0, turn = 0, clock = 0, waitS = 0, failures = 0, swaps = 0;
  let loading = false, next = null, disposed = false, resting = false, flashN = 0, flashDirty = false, held = () => false;
  const capacity = () => {
    const c = media?.videoCapacity;
    return Math.min(faces.length, Number.isFinite(c) ? Math.max(0, Math.floor(c)) : 0);
  };
  const live = () => !disposed && active() && feed ? feed.deviceRows : NO_ROWS;
  const takeCanvas = () => { if (ring.length) return ring.pop(); made++; return makeCanvas(); };
  const free = (canvas) => { wipe(canvas); ring.push(canvas); };
  function onOwner(url) {
    for (let i = 0; i < owners.length; i++) if (owners[i].url === url) return true;
    return false;
  }
  function rebuildFlash() {
    flashList.length = 0; flashDirty = false;
    for (let i = 0; i < owners.length; i++) {
      let dup = false;
      for (let j = 0; j < i; j++) if (owners[j].url === owners[i].url) dup = true;
      if (!dup) flashList.push(owners[i].canvas);
    }
    flashN = flashList.length;
  }
  /** An owner's faces go back to the Loom now, and a live flash card on it to its placeholder. */
  function dropOwner(i) {
    const o = owners[i];
    for (const id of o.slots) media.setSource(id, null);
    const at = flashList.indexOf(o.canvas);
    if (at >= 0) { flashList[at] = null; flashN--; flashDirty = true; }
    owners.splice(i, 1);
    free(o.canvas);
  }
  function adopt(list) {
    const same = sameRows(rows, list);
    rows = list;
    if (same) return;   // a repeated feed: nothing decodes or uploads again
    cursor = 0; failures = 0; waitS = 0; resting = false;
    if (next && !hasRow(list, next.url)) { free(next.canvas); next = null; }
    for (let i = owners.length - 1; i >= 0; i--) if (!hasRow(list, owners[i].url)) dropOwner(i);
    for (const canvas of ring) wipe(canvas);
    if (!rows.length) turn = 0;
    if (flashDirty && !held()) rebuildFlash();
  }
  const sync = () => { const list = live(); if (list !== rows) adopt(list); };
  /** Bring a new owner's faces up (a full pool leaves them on their Loom placeholders). */
  function attach(pic) {
    const n = capacity();
    let k = 0;
    for (let i = 0; i < owners.length; i++) if (owners[i].k === k) { k++; i = -1; }
    const ids = faces.filter((_, i) => i % n === k).map((s) => s.id);
    let taken = false;
    for (const id of ids) if (media.setSource(id, { kind: 'canvas', el: pic.canvas, borrowed: true, credit: 'scrolller' }) !== false) taken = true;
    if (!taken) { free(pic.canvas); return; }
    owners.push({ canvas: pic.canvas, url: pic.url, k, slots: ids });
    rebuildFlash();
  }
  function place(gentle) {
    if (!next) return;
    if (owners.length < capacity()) { const pic = next; next = null; attach(pic); clock = 0; return; }
    if (!owners.length || clock < (gentle ? GENTLE_TURN_S : TURN_S) / owners.length) return;
    const o = owners[turn++ % owners.length], pic = next;
    next = null; clock = 0;
    if (!media.swapStill(o.canvas, pic.canvas)) { free(pic.canvas); return; }
    ring.push(o.canvas);
    o.canvas = pic.canvas; o.url = pic.url; swaps++;
    rebuildFlash();
  }
  /** The next row to decode: one not on the screens, else a repeat only to fill an empty owner, else none. */
  function pick() {
    const n = rows.length, cap = capacity();
    if (!n || !cap) return '';
    for (let i = 0; i < n; i++) {
      const at = (cursor + i) % n;
      if (!onOwner(rows[at].url)) { cursor = (at + 1) % n; return rows[at].url; }
    }
    if (owners.length >= cap) return '';
    const url = rows[cursor % n].url;
    cursor = (cursor + 1) % n;
    return url;
  }
  function load() {
    if (loading || next || waitS > 0 || resting) return;
    const url = pick();
    if (!url) { resting = true; return; }
    const canvas = takeCanvas();
    loading = true;
    Promise.resolve().then(() => decode(url, canvas)).then(() => {
      if (disposed || !hasRow(rows, url)) { free(canvas); return; }
      failures = 0; next = { canvas, url };
    }, () => {
      free(canvas);
      if (disposed || !hasRow(rows, url)) return;
      failures++; waitS = failures >= rows.length ? ALL_FAILED_RETRY_S : RETRY_S;
    }).finally(() => { loading = false; });
  }
  return {
    sync,
    /** paused: nothing draws the world, so only sync. Positional so the race tick allocates nothing. */
    update(dt, paused = false, gentle = false) {
      sync();
      if (flashDirty && !held()) rebuildFlash();
      if (paused || disposed) return;
      const step = dt > 0 ? dt : 0;
      clock += step; waitS = waitS > step ? waitS - step : 0;
      if (!held()) place(gentle);
      load();
    },
    /** `fn` true while a flash burst is live or queued: nothing is placed until it ends. */
    holdFlash(fn) { held = typeof fn === 'function' ? fn : () => false; },
    /** A picture on the screens for the flash stills and pickup fronts (a canvas: never a clip), or null. */
    sourceForFlash(seed = 0) {
      sync();
      if (!flashList.length) return null;
      return flashList[Math.abs(Math.trunc(seed)) % flashList.length] || null;
    },
    /** Distinct pictures on the screens now: a burst never repeats one (a short feed may fill two owners with one row). */
    get flashCount() { sync(); return flashN; },
    stats: () => ({ rows: rows.length, owners: owners.length, canvases: made, swaps, turn, decoding: loading, waiting: !!next }),
    dispose() {
      if (disposed) return;
      rows = NO_ROWS;
      if (next) { free(next.canvas); next = null; }
      for (let i = owners.length - 1; i >= 0; i--) dropOwner(i);
      for (const canvas of ring) wipe(canvas);
      flashList.length = 0; flashN = 0; flashDirty = false;
      disposed = true;
    },
  };
}
