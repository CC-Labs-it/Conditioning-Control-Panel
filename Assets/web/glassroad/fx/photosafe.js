/* fx/photosafe.js - the luminance budget (CPU side, never a readback).
 *
 * Every effect that changes screen brightness (bloom pulse, band flash, pad glow, edge glow,
 * palette swing, finish swell) asks request(kind) first and skips or softens itself when refused.
 * A rolling photosafe.windowS window grants at most photosafe.maxTransitions. Red
 * swings also spend a stricter red budget of their own. The clock is the sim's: the
 * window only moves through update(dt), so a paused page spends nothing.
 */

/** @returns {{request(kind: string): boolean, update(dt: number): void, used(): number, reset(): void}} */
export function createPhotosafe(feel) {
  let now = 0;
  const all = [];   // times of granted transitions
  const red = [];   // times of granted red transitions

  const prune = (list, windowS) => { while (list.length && now - list[0] >= windowS) list.shift(); };

  function request(kind) {
    const windowS = feel.get('photosafe.windowS');
    prune(all, windowS); prune(red, windowS);
    if (all.length >= feel.get('photosafe.maxTransitions')) return false;
    if (kind === 'red') {
      if (red.length >= feel.get('photosafe.redMax')) return false;
      red.push(now);
    }
    all.push(now);
    return true;
  }

  function update(dt) { if (dt > 0) now += dt; }

  function used() { prune(all, feel.get('photosafe.windowS')); return all.length; }

  function reset() { all.length = 0; red.length = 0; }

  return { request, update, used, reset };
}
