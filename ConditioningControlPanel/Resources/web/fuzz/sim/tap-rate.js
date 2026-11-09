/* sim/tap-rate.js - how fast a person is pressing the main button. Pure, so the rules are testable without a page.
 * Only trusted manual presses on #btn go in (tap event `via: 'hand'`, ui/app.js): auto clicks, bouncing copies, the
 * keyboard and the Double Wielding right hand never do. Times are milliseconds on one steady clock (event timeStamp). */

/** A rolling list of press times, kept to the last spanMs. */
export function createTapWindow(spanMs = 3000) {
  const times = [];
  const trim = (now) => { while (times.length && now - times[0] > spanMs) times.shift(); };
  return {
    /** One press at time t. Answers how many presses the window holds now. */
    push(t) {
      if (times.length && t < times[times.length - 1]) t = times[times.length - 1];
      times.push(t);
      trim(t);
      return times.length;
    },
    /** Presses in the last ms (ms <= spanMs) up to now. */
    count(now, ms = spanMs) {
      let n = 0;
      for (let i = times.length - 1; i >= 0 && now - times[i] <= ms; i--) n += 1;
      return n;
    },
    /** Presses a second over the last ms, measured between the first and last press in it. Under 3 presses = 0. */
    rate(now, ms = 1500) {
      let first = -1, n = 0;
      for (let i = times.length - 1; i >= 0 && now - times[i] <= ms; i--) { first = times[i]; n += 1; }
      if (n < 3) return 0;
      const span = Math.max(1, now - first);
      return ((n - 1) * 1000) / span;
    },
    clear() { times.length = 0; },
  };
}

/* ---- hidden speed achievements: 8, 12 and 15 presses a second, each held for 3 seconds ---- */
export const TAP_BURSTS = Object.freeze([8, 12, 15]);
export const BURST_HOLD_MS = 3000;

/** The burst tiers a window of presses reaches at now: a tier counts when the last BURST_HOLD_MS held tier * 3 presses. */
export function burstTiers(win, now) {
  const n = win.count(now, BURST_HOLD_MS);
  return TAP_BURSTS.filter((tier) => n >= tier * (BURST_HOLD_MS / 1000));
}

/* ---- the EMI button's bell curve ----
 * Human tapping: about 4-7 a second is a comfortable sustained pace, 8-10 is a hard sprint, 12-15 is the top of the
 * range for a few seconds. The sweet spot sits in the comfortable middle, so the EMI style rewards a steady rhythm
 * over a frantic one. The bonus never goes under 1: today's base per tap is the floor at every rate. */
export const EMI_SWEET = 5.5;   // presses a second at the peak
export const EMI_WIDTH = 1.5;   // standard deviation, in presses a second
export const EMI_LIFT = 0.5;    // +50% per tap at the peak
/** In the sweet spot when the bonus is at least this share of its peak (about 4.4-6.6 a second). */
export const EMI_SWEET_SHARE = 0.75;

/** Per-tap multiplier for a recent tap rate: 1 + lift at the sweet spot, falling off both ways towards 1, never below. */
export function emiBell(rate) {
  if (!(rate > 0)) return 1;
  const z = (rate - EMI_SWEET) / EMI_WIDTH;
  return 1 + EMI_LIFT * Math.exp(-0.5 * z * z);
}

export const inEmiSweet = (mult) => mult >= 1 + EMI_LIFT * EMI_SWEET_SHARE;
