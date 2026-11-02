/* ui/ads.js - the next ad record for every slot in every lane, so one switch binds them all. Rotates through data/ads.js,
 * with the picked theme's CCP cards mixed in. Jokes off (Joke Ads Gone) leaves only real CCP cards. */
import { ADS } from '../data/ads.js';
import { CCP_THEME_ADS } from '../data/content/ads.js';

const pools = new Map();

/** The pool for a theme: every other real card becomes one of the theme's, so the mix of jokes to real stays the same. */
export function adPool(theme) {
  const cards = (theme && CCP_THEME_ADS[theme]) || null;
  if (!cards) return ADS;
  if (!pools.has(theme)) {
    let slot = 0, n = 0;
    pools.set(theme, Object.freeze(ADS.map((ad) => {
      if (!ad.ccp) return ad;
      slot += 1;
      return slot % 2 ? { ...cards[n++ % cards.length], ccp: true } : ad;
    })));
  }
  return pools.get(theme);
}

export function createAds() {
  let i = 0;
  const holders = new Set();
  return {
    get jokesOff() { return holders.size > 0; },
    /** Each holder switches jokes off until it says otherwise or goes away. */
    hold(owner, on) { if (on) holders.add(owner); else holders.delete(owner); },
    /** A kind with no match falls back to any kind, but real-only never falls back to a joke. */
    next({ kind, theme, ccpOnly = false } = {}) {
      const pool = adPool(theme);
      const allowed = ccpOnly || holders.size ? pool.filter((a) => a.ccp) : pool;
      const list = kind ? allowed.filter((a) => a.kind === kind) : allowed;
      const from = list.length ? list : allowed;
      return from[i++ % from.length];
    },
  };
}
