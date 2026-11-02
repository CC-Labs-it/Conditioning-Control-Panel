/* ui/pictures.js - everything that shows a picture asks here, through ctx.pictures. One source at a time:
 *   online  signed in, the real Scrolller wire and its consent card come later behind this file. Until then: placeholder
 *           art, generated here, no network at all.
 *   own     the player's own files. Blob urls on this device only, never uploaded, gone on reload for now.
 *   none    next() resolves null. A frame stays empty.
 * The choice is kept in the save. No screen sets it yet; a later lane adds the press and the consent. */

import { CONFIG } from '../data/config.js';

export const PICTURE_SOURCES = Object.freeze(['online', 'own', 'none']);

const TINTS = ['#4a3a7a', '#7a3a5a', '#3a6a7a', '#7a5a3a', '#5a7a3a', '#3a3a7a'];
let n = 0;

export function placeholder(niche = 'picture') {
  const tint = TINTS[n++ % TINTS.length];
  const label = String(niche).replace(/[^a-z0-9 -]/gi, '').slice(0, 24) || 'picture';
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="480" height="640"><defs><pattern id="h" width="24" height="24" patternUnits="userSpaceOnUse" patternTransform="rotate(45)"><rect width="12" height="24" fill="#fff" opacity=".12"/></pattern></defs><rect width="480" height="640" fill="${tint}"/><rect width="480" height="640" fill="url(#h)"/><text x="24" y="612" font-family="sans-serif" font-size="28" fill="#fff">${label}</text></svg>`;
  return `data:image/svg+xml,${encodeURIComponent(svg)}`;
}

export function createPictures({ source = CONFIG.PICTURES_DEFAULT, onChange = () => {} } = {}) {
  let current = PICTURE_SOURCES.includes(source) ? source : 'none';
  let own = [];
  let i = 0;

  const dropOwn = () => { for (const url of own) URL.revokeObjectURL?.(url); own = []; i = 0; };

  return {
    get source() { return current; },
    get ownCount() { return own.length; },
    /** Answers whether the source changed. Fires onChange so frames on screen can refill. */
    setSource(kind) {
      if (!PICTURE_SOURCES.includes(kind) || kind === current) return false;
      current = kind;
      onChange(kind);
      return true;
    },
    /** Files from the player's own press. Images only. Answers how many were kept. */
    addOwn(files) {
      const before = own.length;
      for (const f of files || []) if (String(f.type).startsWith('image/')) own.push(URL.createObjectURL(f));
      if (own.length !== before && current === 'own') onChange(current);
      return own.length - before;
    },
    clearOwn() { dropOwn(); if (current === 'own') onChange(current); },
    /** Resolves {url, credit} or null. Show credit when it is set ("via Scrolller"). */
    async next({ niche } = {}) {
      if (current === 'none') return null;
      if (current === 'own') return own.length ? { url: own[i++ % own.length], credit: null } : null;
      return { url: placeholder(niche), credit: null };
    },
  };
}
