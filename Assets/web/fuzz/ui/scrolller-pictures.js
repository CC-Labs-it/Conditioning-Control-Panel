import { deviceRows, feedRows } from '../../glassroad/host/scrolllerFeed.js';

const FEED_CAPACITY = 36;

// Only the authenticated host can approve a source. Keep animated media out of
// the standalone photo pool, even when a host also supplies a poster.
export function scrolllerMedia(frame, capability, pageOrigin) {
  if (!capability || frame?.v !== 1 || !Array.isArray(frame.rows)) return [];
  if (capability.mediaOrigin && capability.mediaOrigin !== 'https://ccp.assets') return [];
  const origin = capability.mediaOrigin || (capability.origin === 'device' ? pageOrigin : null);
  if (capability.origin === 'device' && !origin) return [];
  // A host may supply an image thumbnail alongside its movie for other games.
  // Keep credit and playback on the movie; only standalone posts supply photos.
  const posters = new Set(frame.rows.filter(row => row?.kind === 'video' && typeof row.poster === 'string').map(row => row.poster));
  const candidates = frame.rows.filter(row => {
    if (typeof row?.url !== 'string' || /\.svgz?(?:[?#]|$)/i.test(row.url)) return false;
    if (row.kind === 'video') return (!origin || capability.clips === true) &&
      (row.mime == null || /^video\/(mp4|webm|ogg)$/i.test(row.mime));
    if (row.isPoster === true || row.isThumbnail === true || posters.has(row.url)) return false;
    return row.kind === 'image' && !/\.(mp4|webm|mov)(?:[?#]|$)/i.test(row.url) &&
      (row.mime == null || /^image\/(jpeg|jpg|png|webp|avif|bmp|gif)$/i.test(row.mime));
  });
  const message = { v: 1, rows: candidates };
  const rows = origin ? deviceRows(message, origin, capability.clips === true, FEED_CAPACITY) : feedRows(message, FEED_CAPACITY);
  const byUrl = new Map(candidates.map(row => [row.url, row]));
  return rows.map(row => ({
    url: row.url,
    kind: row.kind === 'video' ? 'clip' : /\.gif(?:[?#]|$)/i.test(row.url) || byUrl.get(row.url)?.mime === 'image/gif' ? 'gif' : 'picture',
  }));
}
