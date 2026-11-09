/* ui/platform.js - rows that only work on some devices (owner 2026-11-09). An item with `device` in data/items.js
 * (set in dev/gen-items.mjs DEVICE) is never offered where it cannot work: not in the shop, not as "on offer" on the
 * Guide map. A save that already owns one keeps it; this only decides what is offered.
 *   toys:  Toy Link and Phone Buzz (owner 2026-11-10). Offered only when the host's init says a toy is connected at run
 *          start: caps.toysConnected === true. Only the CCP desktop app will ever send it (its host is wired later), so
 *          the web and phones never offer either row, Android web included. The player connects the toy first.
 *   mouse: Double Wielding. The right mouse button: needs a fine pointer (mouse, trackpad). A touch-only phone has none.
 * Pure rules (`offerable`) plus one cached reading of the device (`devicePlatform`), so tests need no page. */

import { BY_ID } from '../data/items.js';

/** Why a row is not offered here, for the map card. Plain words. */
export const DEVICE_WHY = Object.freeze({
  toys: 'Connect a toy in the CCP desktop app before you start.',
  mouse: 'Not on this device: it needs a mouse.',
});

/** What this device can do: { toys, mouse }. Read from a window (and the host's caps); missing APIs count as no.
 *  `toys` needs the boolean true: 'yes', 1 or a missing key offer nothing. Caps arrive once, in init, so this is the
 *  reading at run start. */
export function readPlatform(win, caps = {}) {
  const mm = (q) => { try { return !!win?.matchMedia?.(q)?.matches; } catch { return false; } };
  const toys = caps?.toysConnected === true;
  /* No matchMedia at all (an old or odd browser): assume a mouse rather than hide a row that may work. */
  const mouse = typeof win?.matchMedia !== 'function' || mm('(any-pointer: fine)');
  return { toys, mouse };
}

/** True when this row can work on a device with these abilities. Rows without `device` always can. */
export function offerable(item, platform) {
  const it = typeof item === 'string' ? BY_ID[item] : item;
  const need = it?.device;
  if (!need) return true;
  return !!platform?.[need];
}

/** The reason a row is not offered here, or ''. */
export function whyNot(item, platform) {
  const it = typeof item === 'string' ? BY_ID[item] : item;
  return it && !offerable(it, platform) ? DEVICE_WHY[it.device] || 'Not on this device.' : '';
}

/** Rows that carry a device need: the most the shop can ever hide from one window. */
export const DEVICE_ROWS = Object.freeze(Object.values(BY_ID).filter((it) => it.device).map((it) => it.id));

/** A live reading for this page, re-read at most once a second (a mouse can be plugged in mid-run). */
export function devicePlatform(win, caps = () => ({})) {
  let at = -1e9, cur = null;
  const now = () => win?.performance?.now?.() ?? Date.now();
  const read = () => {
    const t = now();
    if (!cur || t - at > 1000) { at = t; cur = readPlatform(win, typeof caps === 'function' ? caps() : caps); }
    return cur;
  };
  return {
    get abilities() { return read(); },
    canOffer: (item) => offerable(item, read()),
    whyNot: (item) => whyNot(item, read()),
  };
}

/** The shop window with rows this device cannot use taken out, and the next rows moved up in their place.
 *  `shop(n)` is engine.shop. The goal is never a device row on the main path; in a Journey shelf it can be. */
export function offeredWindow(shop, size, canOffer) {
  const small = shop(size);
  const hidden = (it) => it && !canOffer(it);
  if (!hidden(small.goal) && !small.rows.some(hidden)) return small;
  const full = shop(size + DEVICE_ROWS.length + 1);
  const ok = full.rows.filter((it) => !hidden(it));
  let goal = full.goal;
  if (hidden(goal)) goal = ok.shift() || null;
  const cap = small.rows.length;   // the window's own cap: the size asked for, or a mood's smaller window
  return { goal, rows: ok.slice(0, cap), more: Math.max(0, ok.length - cap) + full.more };
}
