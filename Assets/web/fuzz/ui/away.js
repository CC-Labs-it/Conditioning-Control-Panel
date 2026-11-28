/* ============================================================================
 * ui/away.js - the away clock (owner 2026-11-08: "the fuzz over time keeps being counted even when the tab is not in
 * focus or ducked"). Browsers stop drawing frames in a hidden tab, a minimised window or a frame scrolled off screen,
 * and the game clock only moves on frames. This measures the gap on a monotonic clock and hands it to credit(ms)
 * (engine.away: per-second pay only). Pure: the clock and the credit are passed in.
 *
 * - frame(present): every drawn frame. A gap over minMs, or any gap while not present (a host focus suspend), is away
 *   time. Answers the ms the normal step may still use, 0 when the gap went to away pay, so nothing is paid twice.
 * - beat(): a timer while frames are stopped (hidden timers still run, about once a minute at worst), so a save taken
 *   while hidden already holds the pay and a hidden tab that is closed keeps it.
 * - One absence pays at most capMs. A clock that runs backwards pays nothing. The return note (onBack) needs a gain and
 *   an absence of at least noticeMs.
 * ==========================================================================*/

import { CONFIG } from '../data/config.js';

export function createAwayClock({ now, credit, onBack = null, minMs = CONFIG.AWAY_MIN_MS, capMs = CONFIG.AWAY_CAP_MS, noticeMs = CONFIG.AWAY_NOTICE_MS }) {
  let last = now(), spell = null;

  function take(t, absent) {
    const gap = Number.isFinite(t) ? Math.max(0, t - last) : 0;
    if (Number.isFinite(t) && t > last) last = t;
    if (!absent && gap <= minMs) return { gap, took: false };
    if (!spell) spell = { start: last - gap, ms: 0, gain: 0 };
    const cap = typeof capMs === 'function' ? capMs() : capMs;   // a function: Night Shift can raise it mid-run
    const ms = Math.min(gap, Math.max(0, cap - spell.ms));
    spell.ms += ms;
    if (ms > 0) { const got = credit(ms); if (got > 0) spell.gain += got; }
    return { gap, took: true };
  }

  return {
    frame(present = true) {
      const { gap, took } = take(now(), !present);
      if (present && spell) {
        const s = spell; spell = null;
        if (s.gain > 0 && last - s.start >= noticeMs) onBack?.(s.gain, last - s.start);
      }
      return took ? 0 : gap;
    },
    beat() {
      const t = now();
      if (!(t - last > minMs)) return 0;
      const before = spell ? spell.gain : 0;
      take(t, true);
      return spell ? spell.gain - before : 0;
    },
    get away() { return !!spell; },
  };
}
