/* sim/format.js - numbers the way the counter shows them: commas, no suffix.
 * The final number is 2,000,000 and reads as one. Locale is fixed on purpose
 * so a screenshot looks the same in every language. */

export function fuzz(n) {
  return Math.round(n).toLocaleString('en-US');
}

/** "1,234 Fuzz" / "1 Fuzz" */
export function fuzzLabel(n) { return `${fuzz(n)} Fuzz`; }

/** mm:ss for the lock and the final card. */
export function clock(ms) {
  const s = Math.max(0, Math.floor(ms / 1000));
  const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), r = s % 60;
  const two = (v) => String(v).padStart(2, '0');
  return `${two(h)}:${two(m)}:${two(r)}`;
}
