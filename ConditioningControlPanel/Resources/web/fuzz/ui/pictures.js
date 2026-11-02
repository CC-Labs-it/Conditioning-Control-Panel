/* ui/pictures.js - everything that shows a picture, a gif or a clip asks here, through ctx.pictures. One source at a time:
 *   online  signed in, the real Scrolller wire and its consent card come later behind this file. Until then: placeholder
 *           art, generated here, no network at all, reachable only with the dev door ?pictures=online.
 *   own     the player's own files. Blob urls on this device only, never uploaded, gone on reload.
 *   none    next() resolves null. A frame stays empty. The default.
 * The player chooses on the pictures card (ui/picture-card.js) or on a screen's test card (ui/screens.js). The choice is
 * kept in the save, the files are not.
 *
 * Kinds: picture, gif, clip. A caller that leaves `kind` out gets something it can use as a css background, so never a
 * clip, and never a gif under Gentle (nothing can still a gif there). ui/screens.js asks by kind and draws the real element. */

import { CONFIG } from '../data/config.js';

export const PICTURE_SOURCES = Object.freeze(['online', 'own', 'none']);
export const OWN_KINDS = Object.freeze(['picture', 'gif', 'clip']);

/** What a screen of that kind may show, best first. A clip screen never shows a still; a picture screen never a clip. */
const CHAINS = Object.freeze({ picture: ['picture', 'gif'], gif: ['gif', 'clip'], clip: ['clip', 'gif'] });

const TINTS = ['#4a3a7a', '#7a3a5a', '#3a6a7a', '#7a5a3a', '#5a7a3a', '#3a3a7a'];
let n = 0;

export function placeholder(niche = 'picture') {
  const tint = TINTS[n++ % TINTS.length];
  const label = String(niche).replace(/[^a-z0-9 -]/gi, '').slice(0, 24) || 'picture';
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="480" height="640"><defs><pattern id="h" width="24" height="24" patternUnits="userSpaceOnUse" patternTransform="rotate(45)"><rect width="12" height="24" fill="#fff" opacity=".12"/></pattern></defs><rect width="480" height="640" fill="${tint}"/><rect width="480" height="640" fill="url(#h)"/><text x="24" y="612" font-family="sans-serif" font-size="28" fill="#fff">${label}</text></svg>`;
  return `data:image/svg+xml,${encodeURIComponent(svg)}`;
}

/** Which kind a file is, or null. The type first; a file with no type is judged by its name. */
export function ownKind(file) {
  const type = String(file?.type || '').toLowerCase(), name = String(file?.name || '').toLowerCase();
  if (type === 'image/gif') return 'gif';
  if (type.startsWith('image/')) return 'picture';
  if (type.startsWith('video/')) return 'clip';
  if (type) return null;
  if (name.endsWith('.gif')) return 'gif';
  if (/\.(png|jpe?g|webp|avif|bmp)$/.test(name)) return 'picture';
  if (/\.(mp4|webm|m4v|mov|ogv)$/.test(name)) return 'clip';
  return null;
}

const emptyOwn = () => ({ picture: [], gif: [], clip: [] });

export function createPictures({ source = CONFIG.PICTURES_DEFAULT, onChange = () => {}, gentle = () => false } = {}) {
  let current = PICTURE_SOURCES.includes(source) ? source : 'none';
  let own = emptyOwn();
  let seen = { picture: 0, gif: 0, clip: 0 };

  const revoke = (bag) => { for (const k of OWN_KINDS) for (const url of bag[k]) URL.revokeObjectURL?.(url); };
  const total = (bag) => OWN_KINDS.reduce((sum, k) => sum + bag[k].length, 0);
  const sort = (files, bag) => {
    let kept = 0;
    for (const f of files || []) {
      const kind = ownKind(f);
      if (kind) { bag[kind].push(URL.createObjectURL(f)); kept += 1; }
    }
    return kept;
  };

  return {
    get source() { return current; },
    get ownCount() { return total(own); },
    get ownCounts() { return { picture: own.picture.length, gif: own.gif.length, clip: own.clip.length }; },
    /** Answers whether the source changed. Fires onChange so frames on screen can refill. */
    setSource(kind) {
      if (!PICTURE_SOURCES.includes(kind) || kind === current) return false;
      current = kind;
      onChange(kind);
      return true;
    },
    /** Files from the player's own press. Pictures, gifs and videos. Answers how many were kept. */
    addOwn(files) {
      const kept = sort(files, own);
      if (kept && current === 'own') onChange(current);
      return kept;
    },
    /** A new pick replaces the last one. Nothing usable in it changes nothing. Answers how many were kept. */
    replaceOwn(files) {
      const next = emptyOwn();
      const kept = sort(files, next);
      if (!kept) return 0;
      const old = own;
      own = next;
      seen = { picture: 0, gif: 0, clip: 0 };
      if (current === 'own') onChange(current);
      revoke(old);
      return kept;
    },
    clearOwn() {
      revoke(own);
      own = emptyOwn();
      seen = { picture: 0, gif: 0, clip: 0 };
      if (current === 'own') onChange(current);
    },
    /** Resolves {url, credit, kind} or null. Show credit when it is set ("via Scrolller"). */
    async next({ niche, kind } = {}) {
      if (current === 'none') return null;
      if (current === 'online') return { url: placeholder(niche), credit: null, kind: 'picture' };
      const chain = kind ? CHAINS[kind] || CHAINS.picture : gentle() ? ['picture'] : CHAINS.picture;
      for (const k of chain) {
        const list = own[k];
        if (list.length) return { url: list[seen[k]++ % list.length], credit: null, kind: k };
      }
      return null;
    },
  };
}
