/* ============================================================================
 * ui/reactions-guard.js - the stands' anti-spam, on the client (owner,
 * 2026-10-14: "add an anti spam too").
 *
 * A small bucket of presses: three in a row, then one back every 1.5 s. When
 * a press empties the bucket the bar greys until the next one is back, so the
 * watcher sees why a press did nothing instead of guessing. The server keeps
 * its own rule (one POST per spectator per 2 s, at most 10 of an id per POST);
 * net/tape.js batches the presses that pass this guard into those POSTs.
 *
 *   createGuard({ burst, everyMs, now }) -> { take, left, waitMs, reset }
 *
 * Pure apart from the clock, which is injectable.
 * ==========================================================================*/

export const GUARD = Object.freeze({
  burst: 3,        // presses in a row before the bar rests
  everyMs: 1500,   // one press back this often
});

const monoNow = () => (typeof performance !== 'undefined' && performance.now ? performance.now() : Date.now());

export function createGuard({ burst = GUARD.burst, everyMs = GUARD.everyMs, now = monoNow } = {}) {
  let tokens = burst;
  let last = now();
  function refill() {
    const t = now();
    if (t > last) tokens = Math.min(burst, tokens + (t - last) / everyMs);
    last = t;
  }
  const wait = () => (tokens >= 1 ? 0 : Math.ceil((1 - tokens) * everyMs));
  return {
    /** One press. ok: it goes out. waitMs > 0: the bar rests that long (after this press, or instead of it). */
    take() {
      refill();
      if (tokens < 1) return { ok: false, waitMs: wait() };
      tokens -= 1;
      return { ok: true, waitMs: wait() };
    },
    /** Whole presses left right now. */
    left() { refill(); return Math.floor(tokens); },
    /** How long until the next press goes out, 0 when one would now. */
    waitMs() { refill(); return wait(); },
    reset() { tokens = burst; last = now(); },
  };
}

export default createGuard;
