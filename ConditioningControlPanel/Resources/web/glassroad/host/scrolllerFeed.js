/* host/scrolllerFeed.js - the web host's Scrolller pictures (CONTRACT.md section 7, `scrolller-feed`).
 *
 * DISPLAY ONLY. Scrolller's media hosts send no CORS headers, so the browser shows their pictures but never
 * lets them become a WebGL texture or be read back. These elements go to the flash-burst canvas only
 * (fx/flashMedia.js: a plain page canvas, never uploaded, never read), never to cardMedia, the item cards,
 * the marquee or any world screen. Nothing here fetches: each <img> is loaded by the browser itself, and
 * the rows come from the host, which asks api.scrolller.com from this same browser (no CC Labs server
 * touches a byte). The desktop host never sends the message, so there it stays empty.
 *
 * Consent is the host's: the page asks with `set-setting` { key: 'media.remoteConsent', value } and only the
 * `setting` echo (what is stored) moves the switch. A closed gate arrives as an empty feed.
 */
export const MAX_FEED_ROWS = 12;
const SCROLLLER_MEDIA = /^https:\/\/([a-z0-9-]+\.)*scrolller\.com\//i;
const loaded = (img) => img.complete && img.naturalWidth > 0;

/** The rows a feed message may carry: v1, https on a scrolller.com host, stills only, deduped, capped. */
export function feedRows(msg) {
  if (!msg || msg.v !== 1 || !Array.isArray(msg.rows)) return [];
  const out = [], seen = new Set();
  for (const r of msg.rows) {
    if (out.length >= MAX_FEED_ROWS) break;
    const url = r && typeof r.url === 'string' ? r.url : '';
    if (r?.kind !== 'image' || !SCROLLLER_MEDIA.test(url) || seen.has(url)) continue;
    seen.add(url); out.push({ url, kind: 'image' });
  }
  return out;
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

export function createScrolllerFeed({ bridge, makeImage = () => new Image() } = {}) {
  let pictures = [], consent = null, disposed = false, catalog = null;
  const listeners = new Set();
  const notify = () => { for (const fn of listeners) { try { fn(consent); } catch (e) { console.warn('[scrolller]', e); } } };
  function drop(list) { for (const img of list) { try { img.removeAttribute('src'); } catch { /* gone */ } } }
  function apply(msg) {
    if (disposed) return;
    const next = feedCatalog(msg);
    if (next && JSON.stringify(next) !== JSON.stringify(catalog)) { catalog = next; notify(); }
    const rows = feedRows(msg), old = pictures;
    pictures = rows.map(({ url }) => {
      const img = makeImage();
      img.decoding = 'async';
      img.src = url;
      return img;
    });
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
  return {
    /** The stored consent from the last echo, or null before any echo. */
    get consent() { return consent; },
    get count() { return pictures.length; },
    get ready() { return pictures.filter(loaded).length; },
    /** A loaded picture for a flash burst, or null. Never hand this to a texture. */
    sourceFor(seed = 0) {
      const ready = pictures.filter(loaded);
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
