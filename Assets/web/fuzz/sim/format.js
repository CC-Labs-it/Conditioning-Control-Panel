/* sim/format.js - numbers the way the counter shows them: commas, no suffix. Locale is fixed on purpose so a screenshot
 * looks the same in every language. Numbers under 100 with a fraction keep one decimal, so a small rate does not read 0. */

import { BRAND } from '../data/brand.js';

export function fmt(n) {
  if (!Number.isFinite(n)) return '0';
  if (Math.abs(n) < 100 && !Number.isInteger(n)) return (Math.floor(n * 10) / 10).toLocaleString('en-US');
  return Math.floor(n).toLocaleString('en-US');
}

/** The counter: whole numbers only. */
export const whole = (n) => (Number.isFinite(n) ? Math.floor(n).toLocaleString('en-US') : '0');

export const money = (n) => `${whole(n)} ${BRAND.currencyPlural}`;

/** m:ss, or h:mm:ss past the hour. */
export function clock(ms) {
  const t = Math.max(0, Math.floor(ms / 1000));
  const h = Math.floor(t / 3600), m = Math.floor((t % 3600) / 60), r = t % 60;
  const two = (v) => String(v).padStart(2, '0');
  return h ? `${h}:${two(m)}:${two(r)}` : `${m}:${two(r)}`;
}
