/* ui/ads.js - the next ad record, rotating through data/ads.js. */
import { ADS } from '../data/ads.js';

let i = 0;

/** Rotates through the records of a kind; any kind when none match. */
export function nextAd({ kind } = {}) {
  const list = kind ? ADS.filter((a) => a.kind === kind) : ADS;
  const from = list.length ? list : ADS;
  return from[i++ % from.length];
}
