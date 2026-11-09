// The reel is painted into WebGL. Use the station's composited frames for
// motion, since drawImage on a GIF element only copies its default image.
//
// A WebGL upload needs CORS. images.scrolller.com answers CORS only to scrolller.com (checked
// 2026-11-08: no Access-Control-Allow-Origin for tc.cclabs.app or app.cclabs.app), so on the
// website a Scrolller row can never reach the reel texture. Such a row loads as a plain DOM
// element instead ({dom:true}); slot-reel-overlay.js lays it over the reel window. The browser
// fetches it directly, nothing proxies or caches it on a server (Scrolller licence).

const NO_CORS_HOSTS = /(^|\.)scrolller\.com$/i;

const parse = (url, pageOrigin) => { try { return new URL(url, pageOrigin); } catch { return null; } };

/** True when `url` is a remote http(s) file from a host known to refuse CORS to this page. */
export function needsDomMedia(url, pageOrigin = globalThis.location?.origin) {
  const u = parse(url, pageOrigin);
  return !!u && /^https?:$/.test(u.protocol) && u.origin !== pageOrigin && NO_CORS_HOSTS.test(u.hostname);
}

function crossOriginHttp(url, pageOrigin = globalThis.location?.origin) {
  const u = parse(url, pageOrigin);
  return !!u && /^https?:$/.test(u.protocol) && u.origin !== pageOrigin;
}

export async function loadReelMedia(row, signal) {
  if (needsDomMedia(row.url)) return loadDomMedia(row, signal);
  const art = await loadTextureMedia(row, signal).catch(() => null);
  // Another cross-origin host that refused CORS can still be shown as an element.
  if (!art && !signal.aborted && crossOriginHttp(row.url)) return loadDomMedia(row, signal);
  return art;
}

async function loadTextureMedia(row, signal) {
  const options = { maxEdge: 320, maxFps: 12, signal };
  if (row.kind === 'clip') {
    const {clipSource} = await import('../../../../backroom/room/clip-source.js');
    return clipSource(row.url, { ...options, kind: row.kind });
  }
  if (row.kind === 'gif') {
    const {decodedSource} = await import('../../../../backroom/room/gif-decode.js');
    return decodedSource(row.url, options);
  }
  const image = new Image();
  image.crossOrigin = 'anonymous';
  const release = () => image.removeAttribute('src');
  signal.addEventListener('abort', release, {once:true});
  try {
    signal.throwIfAborted();
    image.src = row.url;
    await image.decode();
    signal.throwIfAborted();
    return {canvas:image, tick() {}, dispose:release};
  } catch {
    release();
    return null;
  } finally {
    signal.removeEventListener('abort', release);
  }
}

const LOAD_MS = 9000;

/** A display-only element for a row the reel texture cannot take. `canvas` stays null, so the
 *  reel keeps its built-in art underneath and the overlay draws `element` on top. */
export async function loadDomMedia(row, signal, doc = globalThis.document) {
  const clip = row.kind === 'clip';
  const element = doc.createElement(clip ? 'video' : 'img');
  const release = () => {
    try { if (clip) element.pause(); } catch { /* already stopped */ }
    element.removeAttribute('src');
    try { if (clip) element.load(); } catch { /* dropping the src frees the decoder */ }
  };
  if (clip) {
    element.muted = true; element.defaultMuted = true; element.loop = true;
    element.playsInline = true; element.preload = 'auto';
  } else {
    element.decoding = 'async';
    element.alt = '';
  }
  element.referrerPolicy='strict-origin';
  let timer = 0, abort = null;
  try {
    signal.throwIfAborted();
    await new Promise((resolve, reject) => {
      abort = () => reject(new Error('cancelled'));
      signal.addEventListener('abort', abort, {once:true});
      timer = setTimeout(() => reject(new Error('timed out')), LOAD_MS);
      if (clip) {
        element.addEventListener('loadeddata', resolve, {once:true});
        element.addEventListener('error', () => reject(new Error('clip failed')), {once:true});
        element.src = row.url;
      } else {
        element.src = row.url;
        element.decode().then(resolve, reject);
      }
    });
    const width = clip ? element.videoWidth : element.naturalWidth, height = clip ? element.videoHeight : element.naturalHeight;
    if (!width || !height) throw new Error('empty media');
    return {dom:true, kind:clip ? 'clip' : row.kind === 'gif' ? 'gif' : 'picture', element, width, height,
      canvas:null, tick() {}, dispose:release};
  } catch {
    release();
    return null;
  } finally {
    clearTimeout(timer);
    if (abort) signal.removeEventListener('abort', abort);
  }
}
