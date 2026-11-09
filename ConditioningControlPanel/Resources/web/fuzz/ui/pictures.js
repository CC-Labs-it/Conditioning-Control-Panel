/* ui/pictures.js - everything that shows a picture, a gif or a clip asks here, through ctx.pictures. One source at a time:
 *   online  local placeholder art for the dev door ?pictures=online.
 *   scrolller  metered stills and motion from the signed-in host. The game never saves media URLs.
 *   own     the player's own files. Blob urls on this device only, never uploaded, gone on reload.
 *   none    next() resolves null. A frame stays empty. The default.
 * The player chooses on the pictures card (ui/picture-card.js) or on a screen's test card (ui/screens.js). The choice is
 * kept in the save, the files are not.
 *
 * Kinds: picture, gif, clip. A caller that leaves `kind` out gets something it can use as a css background or a plain img, so a
 * still only: nothing can stop a gif there, for Gentle or for Wake Up. ui/screens.js asks by kind and draws the real element. */

import { CONFIG } from '../data/config.js';
import { scrolllerMedia } from './scrolller-pictures.js';
import { localImageInfo } from './local-image-motion.js';
import { createMediaCache } from './media-cache.js';

export const PICTURE_SOURCES = Object.freeze(['online', 'own', 'none', 'scrolller']);

/** The niche ids a host knows (CONTRACT.md scrolller-niches): the eight themes, Locked sent as 'chastity'. */
export const FEED_NICHES = Object.freeze(['trance', 'bimbo', 'drone', 'sissy', 'goon', 'chastity', 'censored', 'infection']);
const NICHE_OF_THEME = Object.freeze({ locked: 'chastity' });
/** The run's themes ([] before Pick a Theme, [theme], [theme, second]) as the niches the feed asks for. */
export function feedNiches(themes) {
  const out = [];
  for (const t of Array.isArray(themes) ? themes : []) {
    const id = NICHE_OF_THEME[t] || t;
    if (FEED_NICHES.includes(id) && !out.includes(id)) out.push(id);
  }
  return out.slice(0, 2);
}
export const OWN_KINDS = Object.freeze(['picture', 'gif', 'clip']);

/** What a screen of that kind may show, best first. A clip screen never shows a still; a picture screen never a clip. */
const CHAINS = Object.freeze({ picture: ['picture'], gif: ['gif', 'clip'], clip: ['clip', 'gif'] });

const SCENES = [
  '<rect width="480" height="640" fill="#202b4b"/><circle cx="340" cy="135" r="67" fill="#ffe6ad"/><path d="M0 460 150 225 330 505 480 320V640H0Z" fill="#7688b8"/><path d="m95 310 55-85 58 90-62-30Z" fill="#f4e9df"/><path d="M0 520 200 400 480 560V640H0Z" fill="#405c74"/>',
  '<rect width="480" height="640" fill="#f8c568"/><path d="M240 560V250m0 180q-125 0-130-100 125 0 130 100m0 35q125 0 130-100-125 0-130 100" fill="#497b58" stroke="#243f3c" stroke-width="12"/><g fill="#eb6c88" stroke="#743c59" stroke-width="7"><ellipse cx="240" cy="170" rx="56" ry="90"/><ellipse cx="240" cy="170" rx="56" ry="90" transform="rotate(60 240 245)"/><ellipse cx="240" cy="170" rx="56" ry="90" transform="rotate(120 240 245)"/><ellipse cx="240" cy="170" rx="56" ry="90" transform="rotate(180 240 245)"/><ellipse cx="240" cy="170" rx="56" ry="90" transform="rotate(240 240 245)"/><ellipse cx="240" cy="170" rx="56" ry="90" transform="rotate(300 240 245)"/></g><circle cx="240" cy="245" r="63" fill="#ffe5a0" stroke="#743c59" stroke-width="7"/>',
  '<rect width="480" height="640" fill="#70bbc0"/><circle cx="240" cy="230" r="125" fill="#ffd98a"/><path d="M0 380q120-70 240 0t240 0v260H0" fill="#36779d"/><path d="M0 450q120-70 240 0t240 0v190H0" fill="#294c76"/><path d="M0 530q120-70 240 0t240 0v110H0" fill="#212d51"/>',
  '<rect width="480" height="640" fill="#eee4d2"/><path d="M80 540V260a160 160 0 0 1 320 0v280" fill="none" stroke="#b95e73" stroke-width="65"/><path d="M145 540V260a95 95 0 0 1 190 0v280" fill="none" stroke="#d9a85e" stroke-width="55"/><path d="M200 540V260a40 40 0 0 1 80 0v280" fill="none" stroke="#567b7e" stroke-width="50"/>',
];
let n = 0;

export function placeholder(niche = 'picture') {
  return samplePicture(n++ % SCENES.length, niche);
}

function samplePicture(index, niche) {
  const scene = SCENES[index];
  const label = String(niche).replace(/[^a-z0-9 -]/gi, '').slice(0, 24) || 'picture';
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="480" height="640">${scene}<rect x="18" y="584" width="444" height="38" rx="8" fill="#17212c" opacity=".8"/><text x="34" y="610" font-family="sans-serif" font-size="20" fill="#fff">Sample picture · ${label}</text></svg>`;
  return `data:image/svg+xml,${encodeURIComponent(svg)}`;
}

/** Which kind a file is, or null. The type first; a file with no type is judged by its name. */
export function ownKind(file) {
  const type = String(file?.type || '').toLowerCase(), name = String(file?.name || '').toLowerCase();
  if (type === 'image/gif' || type === 'image/apng' || /\.apng$/.test(name)) return 'gif';
  if (type.startsWith('image/')) return 'picture';
  if (type.startsWith('video/')) return 'clip';
  if (type) return null;
  if (name.endsWith('.gif')) return 'gif';
  if (/\.(png|jpe?g|webp|avif|bmp)$/.test(name)) return 'picture';
  if (/\.(mp4|webm|m4v|mov|ogv)$/.test(name)) return 'clip';
  return null;
}

/** At most this many own files from one pick: a whole photo folder can hold tens of thousands (owner 2026-11-08, folders). */
export const OWN_MAX = 3000;
/* Picture types a browser cannot show (phone HEIC, scans) or that are rarely photos (icons, SVG): a folder is full of them. */
const UNSHOWABLE = /^image\/(hei[cf]|tiff|x-icon|vnd\.microsoft\.icon|svg\+xml)$|\.(hei[cf]|tiff?|ico|svg)$/i;

/** The usable files of a pick: pictures, GIFs and videos, no hidden files (macOS "._" twins), nothing a browser cannot
 * show, and past OWN_MAX a random spread, so a big folder is not just its first subfolder. */
export function ownUsable(files, max = OWN_MAX, rand = Math.random) {
  const list = [...(files || [])].filter((f) => {
    const name = String(f?.name || '');
    return ownKind(f) && !name.startsWith('.') && f.size !== 0 && !UNSHOWABLE.test(String(f?.type || '')) && !UNSHOWABLE.test(name);
  });
  if (list.length <= max) return list;
  for (let i = 0; i < max; i++) { const j = i + Math.floor(rand() * (list.length - i)); [list[i], list[j]] = [list[j], list[i]]; }
  return list.slice(0, max);
}

const emptyOwn = () => ({ picture: [], gif: [], clip: [], pending: 0, retired: false, ready: Promise.resolve() });

export function createPictures({ source = null, onChange = () => {}, onAdded = () => {}, onProgress = () => {}, onMeter = () => {}, scrolller = null, send = () => {},
  pageOrigin = globalThis.location?.origin, niches = () => [] } = {}) {
  const supported = typeof scrolller?.consent === 'boolean';
  let gameplayActive = false, poolId = null, expiresAt = 0;
  const mediaCache = createMediaCache(() => onAdded(current));
  let consent = supported && scrolller.consent;
  const initial = source ?? (consent ? 'scrolller' : CONFIG.PICTURES_DEFAULT);
  let current = PICTURE_SOURCES.includes(initial) && (initial !== 'scrolller' || consent) ? initial : 'none';
  // One reward deck per game, shared by every prize machine. Ordinary picture
  // screens have their own cursor and cannot consume a reward reservation.
  let revision = 0, firstReward = null, lastReward = null;
  const used = new Set(), held = new Map(), ownKeys = new Map();
  const looseBlobs = new WeakMap();
  const rotations = new Map();
  let stillShare = 0;
  function resetDeck() {
    used.clear(); held.clear(); firstReward = lastReward = null;
    rotations.clear(); stillShare = 0;
  }
  const notifyPictures = () => { revision++; onChange(current); };
  function keyFor(url) {
    if (ownKeys.has(url)) return ownKeys.get(url);
    if (current === 'scrolller') {
      const parsed = new URL(url);
      parsed.hash = '';
      // CDN access signatures can change while the still's pathname stays
      // fixed. Device feeds may identify a file using their query string.
      if (/(^|\.)scrolller\.com$/i.test(parsed.hostname) && /\.\w+$/.test(parsed.pathname)) parsed.search = '';
      return parsed.href;
    }
    return url;
  }
  function pruneRemoteDeck() {
    const keys = new Set(remote.map(row => keyFor(row.url)));
    for (const key of used) if (!keys.has(key)) used.delete(key);
    for (const key of held.keys()) if (!keys.has(key)) held.delete(key);
    for (const rotation of rotations.values()) {
      for (const key of rotation.seen) if (!keys.has(key)) rotation.seen.delete(key);
    }
    if (!keys.has(firstReward)) firstReward = null;
    if (!keys.has(lastReward)) lastReward = null;
  }
  let remote = [], expiry = null, awaiting = null;
  const cursors = new Map();
  let usedPercent = null, meterUpgrade = false, meterUpgradeX = null, building = false;
  function updateMeter(value, upgrade = false, upgradeX = null) {
    const next = Number.isInteger(value) && value >= 0 && value <= 100 ? value : null;
    const up = next !== null && upgrade === true;
    // How many times more Prime gets (host-computed from the live shares); kept only when it says something.
    const times = up && Number.isInteger(upgradeX) && upgradeX >= 2 && upgradeX <= 1000 ? upgradeX : null;
    if (next === usedPercent && up === meterUpgrade && times === meterUpgradeX) return;
    usedPercent = next; meterUpgrade = up; meterUpgradeX = times;
    onMeter();
  }
  const clearRemote = () => {
    clearTimeout(expiry); expiry = null;
    remote = []; cursors.clear(); poolId = null; expiresAt = 0;
  };
  const releaseFeed = () => { if (scrolller?.sessionOptIn) send('scrolller-release'); };
  let askedNiches = null;
  const currentNiches = () => feedNiches(niches());
  const requestFeed = () => {
    if (current !== 'scrolller' || !consent) return;
    const list = currentNiches();
    askedNiches = list.join(',');
    send('scrolller-niches', { niches: list });
  };
  /** The theme set changed (fork, Second Theme, a switch): ask again only when the niches differ from the last ask. */
  const refreshNiches = () => { if (askedNiches !== null && currentNiches().join(',') !== askedNiches) requestFeed(); };
  function warmLibrary() {
    if (current === 'own') mediaCache.update(OWN_KINDS.flatMap(kind => own[kind].map(url => ({url,kind}))));
    else if (current === 'scrolller' && scrolller?.playWarmup && scrolller.origin === 'device') {
      mediaCache.update(remote.filter(row => new URL(row.url).origin === pageOrigin), true);
    } else mediaCache.update([]);
  }
  function syncHostPlay() {
    if (scrolller?.playWarmup && current === 'scrolller' && consent) send('scrolller-play', {active:gameplayActive});
  }
  function expireLibrary() {
    clearRemote(); mediaCache.clear(); resetDeck(); notifyPictures(); requestFeed();
  }
  function setGameplayActive(active) {
    if (active && expiresAt && Date.now() >= expiresAt) expireLibrary();
    if (gameplayActive === active) return;
    gameplayActive = active;
    mediaCache.setActive(active);
    syncHostPlay();
  }
  const setSource = kind => {
    if (!PICTURE_SOURCES.includes(kind) || (kind === 'scrolller' && !consent) || kind === current) return false;
    if (current === 'scrolller') releaseFeed();
    current = kind;
    building = false;
    updateMeter(null);
    mediaCache.clear();
    clearRemote();
    warmLibrary();
    resetDeck(); notifyPictures();
    syncHostPlay();
    requestFeed();
    return true;
  };
  let own = emptyOwn();
  let seen = { picture: 0, gif: 0, clip: 0 };

  // Compatibility for callers which used to request a poster for any row.
  const asStill = row => row?.kind === 'picture' ? row : null;
  const revoke = bag => {
    bag.retired = true;
    const urls = OWN_KINDS.flatMap(kind => bag[kind]);
    for (const url of urls) {
      ownKeys.delete(url);
      URL.revokeObjectURL(url);
    }
  };
  const total = (bag) => OWN_KINDS.reduce((sum, k) => sum + bag[k].length, bag.pending);
  function includeFile(file, bag, kind, mime = file.type) {
    const blob = mime === file.type ? file : file.slice(0, file.size, mime);
    const url = URL.createObjectURL(blob);
    bag[kind].push(url);
    // Selecting the same file twice creates two distinct object URLs.
    if (file.name && Number.isFinite(file.lastModified)) ownKeys.set(url,JSON.stringify([file.name,file.size,file.lastModified,file.type]));
    else {
      if (!looseBlobs.has(file)) looseBlobs.set(file,Symbol());
      ownKeys.set(url,looseBlobs.get(file));
    }
  }
  const sort = (files, bag) => {
    let kept = 0;
    const checks = [];
    for (const file of ownUsable(files)) {
      const kind = ownKind(file);
      kept++;
      if (kind === 'picture') checks.push(localImageInfo(file).then(info => ({file,...info})));
      else includeFile(file,bag,kind);
    }
    if (checks.length) {
      bag.pending += checks.length;
      bag.ready = Promise.all([bag.ready, Promise.all(checks)]).then(([, rows]) => {
        if (bag.retired) return;
        for (const {file,kind,mime} of rows) includeFile(file,bag,kind,mime);
        bag.pending -= rows.length;
        if (bag === own && current === 'own') { warmLibrary(); onAdded(current); }
      });
    }
    return kept;
  };

  function stillPool(allowScrolller) {
    let urls=[];
    if (current==='scrolller' && allowScrolller && consent) {
      urls=remote.filter(row => row.kind === 'picture').map(row => row.url);
    }
    if (current==='own') urls=own.picture;
    if (current==='online') urls=SCENES.map((_,i)=>samplePicture(i,'picture'));
    const pool=new Map();
    for (const url of urls) {
      const key=keyFor(url);
      if (!pool.has(key))pool.set(key,url);
    }
    return pool;
  }
  const stillRow=url=>({url,credit:current==='scrolller'?'via Scrolller':null,kind:'picture'});
  // Each group deals every available item before reshuffling. New rows join the
  // current round immediately, even if it started with a single warmed clip.
  function drawMedia(rows, group, previousUrl) {
    if (!rows.length) return null;
    const byKey = new Map(rows.map(row => [keyFor(row.url), row]));
    if (!rotations.has(group)) rotations.set(group, { seen: new Set(), last: null });
    const bag = rotations.get(group);
    let keys = [...byKey.keys()].filter(key => !bag.seen.has(key));
    if (!keys.length) {
      bag.seen.clear();
      keys = [...byKey.keys()];
    }
    const previousKey = previousUrl ? keyFor(previousUrl) : null;
    if (keys.length > 1 && keys.includes(previousKey)) keys = keys.filter(key => key !== previousKey);
    if (keys.length > 1 && keys.includes(bag.last)) keys = keys.filter(key => key !== bag.last);
    const decoded = keys.filter(key => mediaCache.prepared(byKey.get(key).url));
    if (decoded.length) keys = decoded;
    const key = keys[Math.floor(Math.random() * keys.length)];
    bag.seen.add(key);
    bag.last = key;
    return byKey.get(key);
  }
  function playable(kind, previousUrl) {
    const rows = current === 'scrolller' ? remote.filter(row => mediaCache.available(row.url)) :
      OWN_KINDS.flatMap(type => own[type].map(url => ({ url, kind: type })));
    const motion = rows.filter(row => row.kind !== 'picture');
    const stills = rows.filter(row => row.kind === 'picture');
    let row;
    if (kind === 'mixed' && motion.length && stills.length) {
      // Stills punctuate the feed. A library with a lone photo shows it less
      // often, in proportion to how many different moving items are available.
      stillShare += Math.min(1 / 3, stills.length / rows.length);
      if (stillShare >= 1 - Number.EPSILON) {
        stillShare = Math.max(0, stillShare - 1);
        row = drawMedia(stills, 'feed-stills', previousUrl);
      }
    }
    row ??= drawMedia(motion, 'motion', previousUrl);
    if (!row && (kind === 'mixed' || current === 'scrolller')) row = drawMedia(stills, 'feed-stills', previousUrl);
    return row && { ...row, credit: current === 'scrolller' ? 'via Scrolller' : null };
  }
  function stillsForDisplay(count,{allowScrolller=false}={}) {
    return [...stillPool(allowScrolller).values()].slice(0,Math.max(0,count)).map(stillRow);
  }
  function rewardBatch(count=3, preferredUrl=null, signal) {
    if (signal?.aborted) return [];
    const available=stillPool(true);
    for (const key of held.keys())available.delete(key);
    const picks=[], keys=[];
    const limit=Math.min(3,Math.max(0,Math.floor(count)));
    const preferred=preferredUrl && [...available.values()].includes(preferredUrl) ? keyFor(preferredUrl) : null;
    while (available.size && picks.length<limit) {
      let candidates=[...available.keys()].filter(key=>!used.has(key));
      if (!candidates.length) { used.clear(); candidates=[...available.keys()]; }
      if (candidates.length>1 && !picks.length) {
        candidates=candidates.filter(key=>key!==lastReward);
        if (candidates.length>1 && candidates.includes(firstReward)) candidates=candidates.filter(key=>key!==firstReward);
      }
      const chosen=!picks.length && candidates.includes(preferred) ? preferred : candidates[0];
      picks.push(stillRow(available.get(chosen)));
      keys.push(chosen); used.add(chosen); available.delete(chosen);
    }
    if (keys.length) {
      firstReward=keys[0]; lastReward=keys[keys.length-1];
      if (signal) {
        for (const key of keys) held.set(key,signal);
        signal.addEventListener('abort',()=>{
          for (const key of keys) if (held.get(key)===signal) held.delete(key);
        },{once:true});
      }
    }
    return picks;
  }

  return {
    rewardBatch, stillsForDisplay, asStill, setGameplayActive,
    acquire: mediaCache.acquire, takePlayer: mediaCache.take,
    noteView() { if (gameplayActive && current === 'scrolller' && consent && scrolller?.playWarmup) send('scrolller-view'); },
    get rewardRevision() { return revision; },
    get source() { return current; },
    get usedPercent() { return usedPercent; },
    /** The host says a bigger daily share exists for this account (frame.upgrade); never true without a meter. */
    get meterUpgrade() { return meterUpgrade; },
    /** frame.upgradeX when 2 or more beside an upgrade, else null. */
    get meterUpgradeX() { return meterUpgradeX; },
    get loadingCount() { return current === 'scrolller' && building ? remote.length : 0; },
    get ownCount() { return total(own); },
    get ownCounts() { return { picture: own.picture.length, gif: own.gif.length, clip: own.clip.length }; },
    /** Answers whether the source changed. Fires onChange so frames on screen can refill. */
    setSource, requestFeed, refreshNiches,
    /** The niches the feed asks for now (CONTRACT.md scrolller-niches). */
    get niches() { return currentNiches(); },
    get hasScrolllerHost() { return supported; },
    chooseScrolller(done) {
      if (consent && (!scrolller?.sessionOptIn || current === 'scrolller')) { setSource('scrolller'); done(true); }
      else if (supported) {
        awaiting = done;
        send('set-setting', { key: 'media.remoteConsent', value: true, ask: true });
      } else done(false);
      return () => { if (awaiting === done) { awaiting = null; releaseFeed(); } };
    },
    setRemoteConsent(value, reason) {
      if (!supported) return;
      consent = value === true;
      const callback = awaiting; awaiting = null;
      if (!consent) { clearRemote(); if (current === 'scrolller') setSource('none'); }
      else if (callback) setSource('scrolller');
      else requestFeed();
      callback?.(consent, reason);
    },
    receiveScrolller(frame) {
      if (!consent || current !== 'scrolller') return;
      updateMeter(frame?.usedPercent, frame?.upgrade, frame?.upgradeX);
      const rows = scrolllerMedia(frame, scrolller, pageOrigin);
      const wasBuilding = building;
      building = frame?.building === true;
      const nextId = scrolller?.playWarmup && typeof frame?.poolId === 'string' && frame.poolId || null;
      const sameGeneration = poolId === nextId;
      if (sameGeneration && rows.length === remote.length && rows.every((row, i) => row.kind === remote[i].kind && row.url === remote[i].url)) {
        if (wasBuilding !== building) onProgress();
        return;
      }
      const included = new Map(rows.map(row => [row.url, row.kind]));
      // A metered host rotates entries within the same pool. Existing readers
      // can finish while subsequent draws use only the newly published list.
      const added = sameGeneration && rows.length > 0 && remote.length > 0 &&
        (Boolean(nextId) || remote.every(row => included.get(row.url) === row.kind));
      if (!added) { clearRemote(); mediaCache.clear(); }
      poolId = nextId;
      remote = rows;
      warmLibrary();
      pruneRemoteDeck();
      if (remote.length) {
        const deadline = Math.min(expiresAt || Date.now() + 86400000, ...frame.rows.map(row => row?.expiresAt).filter(Number.isFinite));
        if (expiresAt !== deadline) {
          expiresAt = deadline;
          clearTimeout(expiry);
          expiry = setTimeout(expireLibrary, Math.max(0,deadline-Date.now()));
          expiry.unref?.();
        }
      }
      if (added) onAdded(current); else notifyPictures();
    },
    /** Files from the player's own press. Pictures, gifs and videos. Answers how many were kept. */
    addOwn(files) {
      const kept = sort(files, own);
      if (kept && current === 'own') {
        warmLibrary();
        if (total(own) === kept) notifyPictures(); else onAdded(current);
      }
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
      if (current === 'own') { mediaCache.clear(); warmLibrary(); resetDeck(); notifyPictures(); }
      revoke(old);
      return kept;
    },
    clearOwn() {
      revoke(own);
      own = emptyOwn();
      seen = { picture: 0, gif: 0, clip: 0 };
      if (current === 'own') { mediaCache.clear(); resetDeck(); notifyPictures(); }
    },
    /** Resolves {url, credit, kind} or null. Show credit when it is set ("via Scrolller"). */
    async next({ niche, kind, still = false, allowScrolller = false, previousUrl = null } = {}) {
      if (current === 'none') return null;
      if (current === 'own' && own.pending) {
        const bag = own;
        await bag.ready;
        if (bag !== own || current !== 'own') return null;
      }
      if (current === 'scrolller' && (!allowScrolller || !consent)) return null;
      if (current === 'online') return { url: placeholder(niche), credit: null, kind: 'picture' };
      if (!still && (kind === 'mixed' || kind === 'gif')) return playable(kind, previousUrl);
      if (current === 'scrolller') {
        // A licensed surface owns its visible attribution and removal on a source change.
        const kinds = !kind || still ? ['picture'] : [...new Set([...(CHAINS[kind] || CHAINS.picture), 'picture'])];
        for (const type of kinds) {
          const list = remote.filter(row => row.kind === type);
          if (!list.length) continue;
          const cursor = cursors.get(type) || 0;
          cursors.set(type, cursor + 1);
          return { ...list[cursor % list.length], credit: 'via Scrolller' };
        }
        return null;
      }
      const chain = still || !kind ? ['picture'] : CHAINS[kind] || CHAINS.picture;
      for (const k of chain) {
        const list = own[k];
        if (list.length) {
          const row = { url: list[seen[k]++ % list.length], credit: null, kind: k };
          return row;
        }
      }
      return null;
    },
  };
}
