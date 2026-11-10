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
    return await h2c(ad.card, { scale: width / ad.card.offsetWidth, backgroundColor: doc.defaultView.getComputedStyle(ad.card).backgroundColor, logging: false });
  } finally { restore(); }
}

async function* frames(ad, fps, width, progress, stop) {
  const s = ad.build(true), END = s.end + (s.tail || 0);
  const total = Math.ceil(END / 1000 * fps) + 1;
  try {
    for (let f = 0; f < total; f++) {
      if (stop()) throw new Error('stopped');
      let canvas = await snap(ad, s, Math.min(END, 250 + f * 1000 / fps), width);
      try {
        canvas = even(canvas, ad.card.ownerDocument);
        yield { canvas, last: f === total - 1 };
        progress(f + 1, total, fill(ADCARD_COPY.working, { i: f + 1, n: total }));
      } finally { canvas.width = canvas.height = 0; }
    }
  } finally { ad.play({ quiet: true }); }
}

function even(canvas, doc) {
  const w = canvas.width & ~1, h = canvas.height & ~1;
  if (canvas.width === w && canvas.height === h) return canvas;
  const out = doc.createElement('canvas'); out.width = w; out.height = h;
  out.getContext('2d').drawImage(canvas, 0, 0, w, h);
  canvas.width = canvas.height = 0;
  return out;
}

async function gif(source, fps, doc) {
  let worker = null, encoder = null;
  function response() {
    return new Promise((resolve, reject) => {
      worker.onmessage = ({ data }) => data.error ? reject(new Error(data.error)) : resolve(data);
      worker.onerror = (event) => { event.preventDefault(); reject(new Error(event.message || 'GIF worker failed')); };
    });
  }
  try {
    try {
      worker = new doc.defaultView.Worker(new URL('./export-gif-worker.js', import.meta.url), { type: 'module' });
      await response();
    } catch {
      worker?.terminate(); worker = null;
      encoder = (await import('./export-gif.js')).createGifEncoder();
    }
    for await (const { canvas, last } of source) {
      const width = canvas.width, height = canvas.height;
      const data = canvas.getContext('2d').getImageData(0, 0, width, height).data;
      const frame = { data, width, height, delay: last ? 2500 : Math.round(1000 / fps) };
      if (worker) {
        const done = response(); worker.postMessage(frame, [data.buffer]); await done;
      } else { encoder.frame(frame); await sleep(0); }
    }
    let bytes;
    if (worker) {
      const done = response(); worker.postMessage({ finish: true }); ({ bytes } = await done);
    } else bytes = encoder.finish();
    return new Blob([bytes], { type: 'image/gif' });
  } finally { worker?.terminate(); }
}

async function video(source, fps, doc, progress, stop) {
  const list = [];
  let w = 0, h = 0;
  for await (const { canvas } of source) {
    w = canvas.width; h = canvas.height;
    const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/png'));
    if (!blob) throw new Error('Frame encoding failed');
    list.push(blob);
  }
  const win = doc.defaultView;
  const cv = doc.createElement('canvas'); cv.width = w; cv.height = h;
  cv.style.cssText = 'position:fixed;left:-10000px;top:0;';
  doc.body.append(cv);
  let stream = null, rec = null;
  const decode = async blob => {
    if (win.createImageBitmap) {
      const image = await win.createImageBitmap(blob);
      return { image, close: () => image.close() };
    }
    const url = win.URL.createObjectURL(blob), image = new win.Image();
    try { image.src = url; await image.decode(); return { image, close: () => { image.src = ''; } }; }
    finally { win.URL.revokeObjectURL(url); }
  };
  const paint = async blob => {
    const frame = await decode(blob);
    try { cv.getContext('2d').drawImage(frame.image, 0, 0); } finally { frame.close(); }
  };
  try {
    await paint(list[0]);
    const types = ['video/mp4;codecs=avc1.42E01E', 'video/mp4', 'video/webm;codecs=vp9', 'video/webm'];
    const type = types.find((x) => win.MediaRecorder && win.MediaRecorder.isTypeSupported(x));
    if (!type || !cv.captureStream) throw new Error('no recorder');
    stream = cv.captureStream(fps);
    rec = new win.MediaRecorder(stream, { mimeType: type, videoBitsPerSecond: 5e6 });
    const chunks = [];
    rec.ondataavailable = (e) => { if (e.data.size) chunks.push(e.data); };
    const done = new Promise((resolve, reject) => { rec.onstop = resolve; rec.onerror = event => reject(event.error || new Error('Recording failed')); });
    done.catch(() => {});
    rec.start();
    const dt = 1000 / fps, t0 = win.performance.now();
    for (let i = 0; i < list.length; i++) {
      if (stop()) throw new Error('stopped');
      await paint(list[i]); list[i] = null;
      progress(list.length + i + 1, list.length * 2, ADCARD_COPY.encoding);
      await sleep(Math.max(0, t0 + (i + 1) * dt - win.performance.now()));
    }
    await sleep(2000);
    rec.stop(); await done;
    return new Blob(chunks, { type: (rec.mimeType || type).split(';')[0] });
  } finally {
    if (rec && rec.state !== 'inactive') rec.stop();
    stream?.getTracks().forEach(track => track.stop());
    list.length = 0; cv.width = cv.height = 0; cv.remove();
  }
}

async function png(ad) {
  const s = ad.build(true);
  let c;
  try {
    c = await snap(ad, s, s.end, 1080);
    return await new Promise((r) => c.toBlob(r, 'image/png'));
  } finally { if(c)c.width=c.height=0; ad.play({ quiet: true }); }
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
  const fps = kind === 'gif' ? 12 : 15, width = kind === 'gif' ? 432 : 640;
  const captureProgress = kind === 'gif' ? progress : (i, n, text) => progress(i, n * 2, text);
  const source = frames(ad, fps, width, captureProgress, stop);
  const blob = kind === 'gif' ? await gif(source, fps, doc) : await video(source, fps, doc, progress, stop);
  if (stop()) return { status: 'cancelled' };
  const ext = kind === 'gif' ? 'gif' : blob.type.includes('mp4') ? 'mp4' : 'webm';
  return { status: await saveBlob(doc, blob, filename + '.' + ext), blob };
}
