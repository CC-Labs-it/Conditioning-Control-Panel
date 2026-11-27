/* ui/adcard/export.js - the Living Ad card leaves as a GIF, a video (MP4 where the browser records it, else WebM), a
 * PNG, or a copied picture. The card's own timeline is frozen at each moment t, what a clone cannot see (transforms,
 * opacity, colours, background sizes mid-animation) is baked inline, and html2canvas rasterises the card. html2canvas
 * and gifenc (both MIT, vendor/) load on the first export only. GIF 432 px at 12 fps, video 640 px at 15 fps, PNG 1080. */

import { ADCARD_COPY } from '../../data/content/adcard.js';

const VENDOR = new URL('../../vendor/', import.meta.url).href;
const BAKE = ['transform', 'opacity', 'color', 'background-size'];
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const fill = (s, v) => String(s).replace(/\{(\w+)\}/g, (m, k) => (k in v ? v[k] : m));

let h2cPromise = null;
function loadHtml2canvas(doc) {
  const win = doc.defaultView;
  if (win.html2canvas) return Promise.resolve(win.html2canvas);
  h2cPromise ||= new Promise((resolve, reject) => {
    const s = doc.createElement('script');
    s.src = VENDOR + 'html2canvas.min.js';
    s.onload = () => (win.html2canvas ? resolve(win.html2canvas) : reject(new Error('html2canvas did not load')));
    s.onerror = () => { h2cPromise = null; reject(new Error('html2canvas did not load')); };
    doc.head.append(s);
  });
  return h2cPromise;
}

/** Freeze the card at t and bake what the clone cannot read; returns the undo. */
function paintAt(ad, s, t) {
  const win = ad.card.ownerDocument.defaultView;
  s.anims.forEach((a) => { a.pause(); a.currentTime = t; });
  ad.sideAt(s, t, false);
  ad.drawSpiralAt(t);
  const saved = [];
  for (const el of new Set(s.anims.map((a) => a.effect.target))) {
    const cs = win.getComputedStyle(el), keep = el.getAttribute('style');
    const vals = BAKE.map((p) => [p, cs.getPropertyValue(p)]);
    saved.push([el, keep]);
    vals.forEach(([p, v]) => el.style.setProperty(p, v));
  }
  return () => saved.forEach(([el, keep]) => { if (keep == null) el.removeAttribute('style'); else el.setAttribute('style', keep); });
}

async function snap(ad, s, t, width) {
  const doc = ad.card.ownerDocument, h2c = await loadHtml2canvas(doc);
  const restore = paintAt(ad, s, t);
  try {
    // html2canvas copies the whole document for every frame: leave out everything that is not the card, its ancestors or a stylesheet.
    const card = ad.card, aside = (el) => !(el === card || el.contains(card) || card.contains(el) || el.tagName === 'STYLE' || el.tagName === 'LINK' || el.closest('head'));
    return await h2c(card, { scale: width / card.offsetWidth, backgroundColor: doc.defaultView.getComputedStyle(card).backgroundColor, logging: false, ignoreElements: aside });
  } finally { restore(); }
}

async function drawPhotograph(image, width) {
  await image.decode();
  const canvas = image.ownerDocument.createElement('canvas');
  canvas.width = width;
  canvas.height = Math.round(image.naturalHeight * width / image.naturalWidth);
  canvas.getContext('2d').drawImage(image, 0, 0, canvas.width, canvas.height);
  return canvas;
}

/** Captures the card's frames. With `each(canvas, last)` every frame is handed over as it is drawn and none is kept
 *  (the GIF encodes as it goes); without it the frames come back as a list (the video replays them on a clock). */
async function frames(ad, fps, width, progress, stop, each = null) {
  const out = [];
  const take = async (canvas, last) => { if (each) await each(canvas, last); else out.push(canvas); };
  if (ad.photograph) { await take(await drawPhotograph(ad.photograph, width), true); return out; }
  const s = ad.build(true), END = s.end + (s.tail || 0);
  const total = Math.ceil(END / 1000 * fps) + 1;
  try {
    for (let f = 0; f < total; f++) {
      if (stop()) throw new Error('stopped');
      await take(await snap(ad, s, Math.min(END, 250 + f * 1000 / fps), width), f === total - 1);
      progress(f + 1, each ? total : total * 2, fill(ADCARD_COPY.working, { i: f + 1, n: total }));
    }
  } finally { ad.play({ quiet: true }); }
  return out;
}

function even(list, doc) {
  const w = list[0].width & ~1, h = list[0].height & ~1;
  return list.map((c) => {
    if (c.width === w && c.height === h) return c;
    const n = doc.createElement('canvas'); n.width = w; n.height = h; n.getContext('2d').drawImage(c, 0, 0, w, h); return n;
  });
}

/** A GIF that takes its frames as they are captured: each one is quantised and written at once, then the page gets a
 *  turn, so there is no long encode at the end (the page used to stall on "Putting it together"). Sizes follow
 *  even(): the first frame's size rounded down to even, any other size drawn to it. Same palette per frame as before. */
export async function gifWriter(doc, fps) {
  const { GIFEncoder, quantize, applyPalette } = await import(VENDOR + 'gifenc.esm.js');
  const enc = GIFEncoder();
  let w = 0, h = 0;
  return {
    async add(c, last) {
      if (!w) { w = c.width & ~1; h = c.height & ~1; }
      if (c.width !== w || c.height !== h) { const n = doc.createElement('canvas'); n.width = w; n.height = h; n.getContext('2d').drawImage(c, 0, 0, w, h); c = n; }
      const data = c.getContext('2d').getImageData(0, 0, w, h).data;
      const palette = quantize(data, 256), index = applyPalette(data, palette);
      enc.writeFrame(index, w, h, { palette, delay: last ? 2500 : Math.round(1000 / fps) });
      await sleep(0);
    },
    finish() { enc.finish(); return new Blob([enc.bytes()], { type: 'image/gif' }); },
  };
}

async function video(list, fps, doc, progress) {
  const win = doc.defaultView;
  list = even(list, doc);
  const w = list[0].width, h = list[0].height;
  const cv = doc.createElement('canvas'); cv.width = w; cv.height = h;
  cv.style.cssText = 'position:fixed;left:-10000px;top:0;';
  doc.body.append(cv);
  try {
    const g = cv.getContext('2d'); g.drawImage(list[0], 0, 0);
    const types = ['video/mp4;codecs=avc1.42E01E', 'video/mp4', 'video/webm;codecs=vp9', 'video/webm'];
    const type = types.find((x) => win.MediaRecorder && win.MediaRecorder.isTypeSupported(x));
    if (!type || !cv.captureStream) throw new Error('no recorder');
    const rec = new win.MediaRecorder(cv.captureStream(fps), { mimeType: type, videoBitsPerSecond: 5e6 });
    const chunks = [];
    rec.ondataavailable = (e) => { if (e.data.size) chunks.push(e.data); };
    const done = new Promise((r) => { rec.onstop = r; });
    rec.start();
    const dt = 1000 / fps, t0 = win.performance.now();
    for (let i = 0; i < list.length; i++) {
      g.drawImage(list[i], 0, 0);
      progress(list.length + i + 1, list.length * 2, ADCARD_COPY.encoding);
      await sleep(Math.max(0, t0 + (i + 1) * dt - win.performance.now()));
    }
    await sleep(2000);
    rec.stop(); await done;
    return new Blob(chunks, { type: (rec.mimeType || type).split(';')[0] });
  } finally { cv.remove(); }
}

async function png(ad) {
  if (ad.photograph) {
    const canvas = await drawPhotograph(ad.photograph, ad.photograph.naturalWidth || 1100);
    return new Promise(resolve => canvas.toBlob(resolve, 'image/png'));
  }
  const s = ad.build(true);
  try {
    const c = await snap(ad, s, s.end, 1080);
    return await new Promise((r) => c.toBlob(r, 'image/png'));
  } finally { ad.play({ quiet: true }); }
}

/** Hands a file to the player: the share sheet where the device has one for files (phones), else a download. */
export async function saveBlob(doc, blob, name) {
  const win = doc.defaultView, nav = win.navigator;
  const file = typeof win.File === 'function' ? new win.File([blob], name, { type: blob.type }) : null;
  const coarse = win.matchMedia?.('(pointer: coarse)').matches;
  if (coarse && file && nav.canShare?.({ files: [file] })) {
    try { await nav.share({ files: [file] }); return 'shared'; } catch (e) { if (e?.name === 'AbortError') return 'cancelled'; }
  }
  const url = win.URL.createObjectURL(blob), a = doc.createElement('a');
  a.href = url; a.download = name; a.rel = 'noopener'; a.style.display = 'none';
  doc.body.append(a); a.click(); a.remove();
  setTimeout(() => win.URL.revokeObjectURL(url), 60000);
  return 'saved';
}

/**
 * The line under the buttons once an export is out. A download says where it went: the Downloads folder is where a
 * browser puts it by default, and the CCP app's WebView2 sets no folder of its own (no DownloadStarting handler, no
 * DefaultDownloadFolderPath), so it lands there too. `shared` is the share sheet's own line, which each surface keeps.
 */
export function statusLine(status, shared = ADCARD_COPY.saved) {
  if (status === 'saved') return ADCARD_COPY.savedDownloads;
  if (status === 'shared') return shared;
  if (status === 'copied') return ADCARD_COPY.copied;
  if (status === 'nocopy') return ADCARD_COPY.noCopy;
  return '';
}

/**
 * Runs one export. kind: 'gif' | 'video' | 'png' | 'copy'. progress(i, n, text). stop() -> true aborts between frames.
 * Resolves { status, blob } where status is 'saved' | 'shared' | 'copied' | 'cancelled'.
 */
export async function exportCard(ad, kind, { progress = () => {}, stop = () => false, filename = 'terms-and-conditioning-ad' } = {}) {
  const doc = ad.card.ownerDocument, win = doc.defaultView;
  if (kind === 'copy') {
    const blobP = png(ad);
    let item = null;
    try { item = new win.ClipboardItem({ 'image/png': blobP }); } catch { item = null; }
    if (!item || !win.navigator.clipboard?.write) return { status: 'nocopy', blob: await blobP };
    await win.navigator.clipboard.write([item]);
    return { status: 'copied', blob: await blobP };
  }
  if (kind === 'png') {
    const blob = await png(ad);
    if (stop()) return { status: 'cancelled' };
    return { status: await saveBlob(doc, blob, filename + '.png'), blob };
  }
  if (kind === 'gif') {
    const out = await gifWriter(doc, 12);
    await frames(ad, 12, 432, progress, stop, out.add);
    if (stop()) return { status: 'cancelled' };
    progress(1, 1, ADCARD_COPY.encoding);
    const blob = out.finish();
    return { status: await saveBlob(doc, blob, filename + '.gif'), blob };
  }
  const fps = 15, list = await frames(ad, fps, 640, progress, stop);
  // MediaRecorder timestamps a canvas on each draw. Keep the still visible for the usual poster hold.
  const videoFrames = ad.photograph ? Array(Math.ceil(2.5 * fps)).fill(list[0]) : list;
  const blob = await video(videoFrames, fps, doc, progress);
  if (stop()) return { status: 'cancelled' };
  const ext = blob.type.includes('mp4') ? 'mp4' : 'webm';
  return { status: await saveBlob(doc, blob, filename + '.' + ext), blob };
}
