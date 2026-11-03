/* ui/pictures.js - everything that shows a picture, a gif or a clip asks here, through ctx.pictures. One source at a time:
 *   online  signed in, the real Scrolller wire and its consent card come later behind this file. Until then: placeholder
 *           art, generated here, no network at all, reachable only with the dev door ?pictures=online.
 *   own     the player's own files. Blob urls on this device only, never uploaded, gone on reload.
 *   none    next() resolves null. A frame stays empty. The default.
 * The player chooses on the pictures card (ui/picture-card.js) or on a screen's test card (ui/screens.js). The choice is
 * kept in the save, the files are not.
 *
 * Kinds: picture, gif, clip. A caller that leaves `kind` out gets something it can use as a css background or a plain img, so a
 * still only: nothing can stop a gif there, for Gentle or for Wake Up. ui/screens.js asks by kind and draws the real element. */

import { CONFIG } from '../data/config.js';

export const PICTURE_SOURCES = Object.freeze(['online', 'own', 'none']);
export const OWN_KINDS = Object.freeze(['picture', 'gif', 'clip']);

/** What a screen of that kind may show, best first. A clip screen never shows a still; a picture screen never a clip. */
const CHAINS = Object.freeze({ picture: ['picture', 'gif'], gif: ['gif', 'clip'], clip: ['clip', 'gif'] });

const SCENES = [
  '<rect width="480" height="640" fill="#202b4b"/><circle cx="340" cy="135" r="67" fill="#ffe6ad"/><path d="M0 460 150 225 330 505 480 320V640H0Z" fill="#7688b8"/><path d="m95 310 55-85 58 90-62-30Z" fill="#f4e9df"/><path d="M0 520 200 400 480 560V640H0Z" fill="#405c74"/>',
  '<rect width="480" height="640" fill="#f8c568"/><path d="M240 560V250m0 180q-125 0-130-100 125 0 130 100m0 35q125 0 130-100-125 0-130 100" fill="#497b58" stroke="#243f3c" stroke-width="12"/><g fill="#eb6c88" stroke="#743c59" stroke-width="7"><ellipse cx="240" cy="170" rx="56" ry="90"/><ellipse cx="240" cy="170" rx="56" ry="90" transform="rotate(60 240 245)"/><ellipse cx="240" cy="170" rx="56" ry="90" transform="rotate(120 240 245)"/><ellipse cx="240" cy="170" rx="56" ry="90" transform="rotate(180 240 245)"/><ellipse cx="240" cy="170" rx="56" ry="90" transform="rotate(240 240 245)"/><ellipse cx="240" cy="170" rx="56" ry="90" transform="rotate(300 240 245)"/></g><circle cx="240" cy="245" r="63" fill="#ffe5a0" stroke="#743c59" stroke-width="7"/>',
  '<rect width="480" height="640" fill="#70bbc0"/><circle cx="240" cy="230" r="125" fill="#ffd98a"/><path d="M0 380q120-70 240 0t240 0v260H0" fill="#36779d"/><path d="M0 450q120-70 240 0t240 0v190H0" fill="#294c76"/><path d="M0 530q120-70 240 0t240 0v110H0" fill="#212d51"/>',
  '<rect width="480" height="640" fill="#eee4d2"/><path d="M80 540V260a160 160 0 0 1 320 0v280" fill="none" stroke="#b95e73" stroke-width="65"/><path d="M145 540V260a95 95 0 0 1 190 0v280" fill="none" stroke="#d9a85e" stroke-width="55"/><path d="M200 540V260a40 40 0 0 1 80 0v280" fill="none" stroke="#567b7e" stroke-width="50"/>',
];
let n = 0;

export function placeholder(niche = 'picture') {
  const scene = SCENES[n++ % SCENES.length];
  const label = String(niche).replace(/[^a-z0-9 -]/gi, '').slice(0, 24) || 'picture';
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="480" height="640">${scene}<rect x="18" y="584" width="444" height="38" rx="8" fill="#17212c" opacity=".8"/><text x="34" y="610" font-family="sans-serif" font-size="20" fill="#fff">Sample picture · ${label}</text></svg>`;
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

export function createPictures({ source = CONFIG.PICTURES_DEFAULT, onChange = () => {} } = {}) {
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
      const chain = kind ? CHAINS[kind] || CHAINS.picture : ['picture'];
      for (const k of chain) {
        const list = own[k];
        if (list.length) return { url: list[seen[k]++ % list.length], credit: null, kind: k };
      }
      return null;
    },
  };
}
