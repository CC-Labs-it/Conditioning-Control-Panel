/* data/ads.js - the ad pool ctx.ads.next() rotates through: about three joke ads to one real CCP card, in a fixed order
 * so a run's first few ads are not all jokes. Each record says which it is in `ccp`. Theme ads: `CCP_THEME_ADS`. */
import { JOKE_ADS, CCP_ADS } from './content/ads.js';

function interleave(jokes, real, every = 3) {
  const out = [];
  let r = 0;
  jokes.forEach((ad, i) => {
    out.push({ ...ad, ccp: false });
    if ((i + 1) % every === 0) { out.push({ ...real[r % real.length], ccp: true }); r += 1; }
  });
  return out;
}

export const ADS = Object.freeze(interleave(JOKE_ADS, CCP_ADS));
