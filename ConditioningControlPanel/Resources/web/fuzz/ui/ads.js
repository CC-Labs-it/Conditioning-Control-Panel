/* ui/ads.js - the next ad record for every slot in every lane, so one switch binds them all. Rotates through data/ads.js,
 * with the picked theme's CCP cards mixed in. Jokes off (Joke Ads Gone) leaves only real CCP cards. */
import { ADS } from '../data/ads.js';
import { CCP_ADS, CCP_THEME_ADS, JOKE_ADS } from '../data/content/ads.js';

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

/* Ad accident check (owner 2026-11-09): from the second real CCP ad press of a run on, a small card asks before a tab
 * opens. The press has paid already, whatever the answer. The per-run tab cap still holds: past it nothing opens or asks. */
export const AD_ASK_FROM = 2;
export const AD_ASK = Object.freeze({ title: 'Open the ad in a new tab?', open: 'Open', stay: 'Stay here' });

/** One real ad press against the run's tab record { n: tabs opened, clicks: presses so far }. Counts the press and
 *  answers 'open' (open now), 'ask' (show the card first) or 'none' (the cap is spent). */
export function adLinkStep(used, cap) {
  used.clicks = (used.clicks || 0) + 1;
  if ((used.n || 0) >= cap) return 'none';
  return used.clicks >= AD_ASK_FROM ? 'ask' : 'open';
}

/** Basic and Prime players never see a CCP card (owner 2026-11-08): the host says the plan in init caps.plan. */
export const subscriberPlan = (caps) => caps?.plan === 'basic' || caps?.plan === 'prime';

// Empty captions use the original ad headline. These campaigns each have three drawings.
const illustratedCaptions = {
  j_apol: '', j_regret: '', j_goose: '', j_stairs: '', j_soup: '', j_lamp: 'A lamp with opinions',
  c_emi: 'EMI on your desktop', c_bubbles: 'Pop desktop bubbles',
};

function illustratedRotation(records, ccp) {
  const campaigns = records.filter(ad => Object.hasOwn(illustratedCaptions, ad.id))
    .map(ad => ({ ad, order: Math.random(), take: 1 + Math.floor(Math.random() * 3) }))
    .sort((a, b) => a.order - b.order);
  let index = 0;
  return () => {
    const campaign = campaigns[index];
    const { ad, take } = campaign;
    index = (index + 1) % campaigns.length;
    campaign.take = take % 3 + 1;
    return { ...ad, ccp, caption: illustratedCaptions[ad.id] || ad.headline,
      illustration: `assets/classifieds/${ad.id}-${take}.webp` };
  };
}

export function createAds({ noCcp = () => false } = {}) {
  let i = 0;
  const holders = new Set();
  const jokePicture = illustratedRotation(JOKE_ADS, false);
  const ccpPicture = illustratedRotation(CCP_ADS, true);
  return {
    // A subscriber has no real cards to fall back on, so the jokes stay whatever holds them off.
    get jokesOff() { return holders.size > 0 && !noCcp(); },
    /** Each holder switches jokes off until it says otherwise or goes away. */
    hold(owner, on) { if (on) holders.add(owner); else holders.delete(owner); },
    /** A kind with no match falls back to any kind, but real-only never falls back to a joke. */
    next({ kind, theme, ccpOnly = false, illustrated = false } = {}) {
      if (illustrated) return !noCcp() && (ccpOnly || holders.size) ? ccpPicture() : jokePicture();
      const pool = adPool(theme);
      const allowed = noCcp() ? pool.filter((a) => !a.ccp) : ccpOnly || holders.size ? pool.filter((a) => a.ccp) : pool;
      const list = kind ? allowed.filter((a) => a.kind === kind) : allowed;
      const from = list.length ? list : allowed;
      return from[i++ % from.length];
    },
  };
}
