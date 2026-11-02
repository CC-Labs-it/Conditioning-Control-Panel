/* ui/ads.js - the next ad record. data/ads.js belongs to the content lane; until it lands (or if it fails to load) the
 * page runs on two stand-ins, so an ad slot never breaks a run. */

const STAND_INS = [
  { id: 'standin-joke', kind: 'joke', text: 'Your ad here. It would not help.' },
  { id: 'standin-ccp', kind: 'ccp', text: 'Conditioning Control Panel. The app this game is advertising.', url: 'https://cclabs.app' },
];

let pool = STAND_INS;
let i = 0;
let loaded = false;

export function loadAds() {
  if (loaded) return;
  loaded = true;
  import('../data/ads.js').then((m) => {
    const list = m.ADS || m.default;
    if (Array.isArray(list) && list.length) pool = list;
  }).catch(() => {});
}

/** Rotates through the records of a kind; any kind when none match. */
export function nextAd({ kind } = {}) {
  loadAds();
  const list = kind ? pool.filter((a) => a.kind === kind) : pool;
  const from = list.length ? list : pool;
  return from[i++ % from.length];
}
