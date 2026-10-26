/* host/scrolllerFeed.js - the web host's Scrolller pictures (CONTRACT.md section 7, `scrolller-feed`).
 *
 * DISPLAY ONLY. Scrolller's media hosts send no CORS headers, so the browser shows their pictures but never
 * lets them enter the GPU or be read back. Stills go to the flash-burst page canvas (stillFor) and, with the
 * GIF-style clips (`kind: 'video'` rows, muted <video>, metadata only here), to display-only world panels behind
 * depth-tested windows in the game canvas (render/webMediaScreens.js). Nothing here fetches:
 * each <img> / <video> is loaded by the browser itself, and
 * the rows come from the host, which asks api.scrolller.com from this same browser (no CC Labs server
 * touches a byte). The desktop host never sends the message, so there it stays empty.
 *
 * Consent is the host's: the page asks with `set-setting` { key: 'media.remoteConsent', value } and only the
 * `setting` echo (what is stored) moves the switch. A closed gate arrives as an empty feed.
 */
export const MAX_FEED_ROWS = 12;
const SCROLLLER_MEDIA = /^https:\/\/([a-z0-9-]+\.)*scrolller\.com\//i;
/** A feed clip is a <video> element (test fakes say so with tagName). */
export const isVideo = (el) => String(el?.tagName || '').toUpperCase() === 'VIDEO';
// A clip is usable once its metadata is in (HAVE_METADATA); each world face still waits for its own frame.
const loaded = (el) => isVideo(el) ? !el.error && el.readyState >= 1 : el.complete && el.naturalWidth > 0;

/** The rows a feed message may carry: v1, https on a scrolller.com host, stills and GIF-style clips, deduped, capped. */
export function feedRows(msg) {
  if (!msg || msg.v !== 1 || !Array.isArray(msg.rows)) return [];
  const out = [], seen = new Set();
  for (const r of msg.rows) {
    if (out.length >= MAX_FEED_ROWS) break;
    const url = r && typeof r.url === 'string' ? r.url : '';
    if ((r?.kind !== 'image' && r?.kind !== 'video') || !SCROLLLER_MEDIA.test(url) || seen.has(url)) continue;
    seen.add(url);
    if (r.kind === 'image') { out.push({ url, kind: 'image' }); continue; }
    const poster = typeof r.poster === 'string' && SCROLLLER_MEDIA.test(r.poster) ? r.poster : '';
    out.push(poster ? { url, kind: 'video', poster } : { url, kind: 'video' });
  }
  return out;
}

/** A display-only clip: muted, inline, looping. It never asks for CORS: nothing reads it back. */
export function displayVideo(doc = globalThis.document) {
  const v = doc?.createElement('video');
  if (!v) return null;
  v.muted = true; v.defaultMuted = true; v.playsInline = true; v.loop = true;
  v.setAttribute?.('muted', ''); v.setAttribute?.('playsinline', '');
  return v;
}

/** The host catalog a feed carries: [{ id, label }] with plain ids and short labels, or null when absent. */
export function feedCatalog(msg) {
  if (!Array.isArray(msg?.catalog)) return null;
  const out = [];
  for (const c of msg.catalog) {
    if (out.length >= 40) break;
    if (typeof c?.id === 'string' && /^[a-z0-9_-]{1,24}$/.test(c.id) && typeof c.label === 'string' && c.label.length <= 24
      && !out.some((x) => x.id === c.id)) out.push({ id: c.id, label: c.label });
  }
  return out.length ? out : null;
}

export function createScrolllerFeed({ bridge, makeImage = () => new Image(), makeVideo = () => displayVideo(), now = () => Date.now() } = {}) {
  let pictures = [], consent = null, disposed = false, catalog = null, expires = 0;
  const listeners = new Set();
  const notify = () => { for (const fn of listeners) { try { fn(consent); } catch (e) { console.warn('[scrolller]', e); } } };
  /** Release every element; a clip also drops its poster and load() frees its decoder. */
  function drop(list) {
    for (const el of list) {
      try {
        el.removeAttribute('src');
        if (isVideo(el)) { el.removeAttribute('poster'); el.load?.(); }
      } catch { /* gone */ }
    }
  }
  function apply(msg) {
    if (disposed) return;
    const next = feedCatalog(msg);
    if (next && JSON.stringify(next) !== JSON.stringify(catalog)) { catalog = next; notify(); }
    const rows = consent === false ? [] : feedRows(msg), old = pictures;
    expires = now() + 24 * 60 * 60 * 1000;
    pictures = rows.map(({ url, kind, poster }) => {
      if (kind === 'video') {   // metadata only here: the world face that shows it loads the frames
        const v = makeVideo();
        if (!v) return null;
        v.preload = 'metadata';
        if (poster) v.poster = poster;
        v.src = url;
        return v;
      }
      const img = makeImage();
      img.decoding = 'async';
      img.src = url;
      return img;
    }).filter(Boolean);
    drop(old);
  }
  function echo(m) {
    if (disposed || m?.key !== 'media.remoteConsent') return;
    consent = m.value === true;
    if (!consent) { drop(pictures); pictures = []; }
    notify();
  }
  const offFeed = bridge?.on('scrolller-feed', apply);
  const offEcho = bridge?.on('setting', echo);
  const current = () => {
    if (now() >= expires || consent === false) { drop(pictures); pictures = []; }
    return pictures;
  };
  return {
    /** The stored consent from the last echo, or null before any echo. */
    get consent() { return consent; },
    get count() { return current().length; },
    get ready() { return current().filter(loaded).length; },
    /** A loaded display-only picture or clip (<video>) for a world panel, or null. */
    sourceFor(seed = 0) {
      const ready = current().filter(loaded);
      return ready.length ? ready[Math.abs(Math.trunc(seed)) % ready.length] : null;
    },
    /** A loaded still for the flash-burst page canvas, never a clip, or null. */
    stillFor(seed = 0) {
      const ready = current().filter((el) => !isVideo(el) && loaded(el));
      return ready.length ? ready[Math.abs(Math.trunc(seed)) % ready.length] : null;
    },
    /** The niches the host can serve ([{ id, label }]), from its last feed; null until one lands. */
    get catalog() { return catalog; },
    setConsent(on) { bridge?.send({ type: 'set-setting', key: 'media.remoteConsent', value: on === true }); },
    /** This course's niches (host/scrolllerNiches.js): the host draws the next pictures from them. */
    setNiches(track, niches) { bridge?.send({ type: 'scrolller-niches', track: String(track || ''), niches: [...niches] }); },
    subscribe(fn) { listeners.add(fn); return () => listeners.delete(fn); },
    dispose() { disposed = true; offFeed?.(); offEcho?.(); drop(pictures); pictures = []; listeners.clear(); },
  };
}
