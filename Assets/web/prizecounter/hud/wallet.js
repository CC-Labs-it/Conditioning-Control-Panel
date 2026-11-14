/* wallet.js - the wallet chip's count down and the week tag, pure enough to test with a fake clock. */

/** Counts from -> to over ms (ease out), calling onStep with whole numbers; the last step is always `to`.
 *  Resolves with `to`. ms <= 0 is a cut. stop() ends it early on `to`. */
export function countDown(from, to, { ms = 700, now = () => performance.now(), frame = (f) => requestAnimationFrame(f), onStep }) {
  let stopped = false, resolve;
  const done = new Promise((r) => { resolve = r; });
  const t0 = now();
  const step = () => {
    const p = stopped || ms <= 0 ? 1 : Math.min(1, (now() - t0) / ms);
    onStep(p >= 1 ? to : Math.round(from + (to - from) * (1 - (1 - p) ** 3)));
    if (p < 1) frame(step); else resolve(to);
  };
  step();
  return { done, stop() { stopped = true; } };
}

/** '2026-W44' -> '44'; anything else -> null (no tag). */
export function weekNo(week) {
  const m = /W(\d{1,2})$/.exec(String(week || ''));
  return m ? String(Number(m[1])) : null;
}
