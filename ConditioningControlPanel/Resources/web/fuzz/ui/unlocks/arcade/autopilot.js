/* ui/unlocks/arcade/autopilot.js - Autopilot (Renewal Sky star idle_6, "Toys you never touch still pay half their
 * reward") for the toys that only pay on a player action: Prize Wheel, Mini Slots, Prize Dispenser, Race bonus.
 *
 *   const auto = autopilot(ctx, { dueMs: 30000 });
 *   tick: auto.step(dtMs, idle) -> true when one untaken reward came due (the toy then pays its share).
 *
 * `idle` means the reward sits ready and nobody takes it. After `dueMs` of game time ready and untaken, the toy pays
 * `share` (0.5, from boosts.js autopilotShare) of its usual reward through ctx.pay, the same path as the Toy Box and
 * Mystery Box hooks. The waiting time is kept in the toy's run store, so a reload does not restart it. Without the star
 * nothing runs. Breakout needs no hook: its autoplay already clears boards on its own at 8 of the 12 played seconds. */

import { autopilotShare } from '../prestige/boosts.js';

export const AUTOPILOT_KEY = 'autopilotMs';

/** Pure: advance a waiting clock. Answers { ms, due } with `due` = how many rewards came due (at most 1 per step). */
export function autopilotStep(ms, dtMs, idle, dueMs) {
  const now = Number.isFinite(ms) && ms > 0 ? ms : 0;
  if (!idle || !(dueMs > 0)) return { ms: 0, due: 0 };
  const next = now + (Number.isFinite(dtMs) && dtMs > 0 ? Math.min(dtMs, dueMs) : 0);
  return next >= dueMs ? { ms: next - dueMs, due: 1 } : { ms: next, due: 0 };
}

/** Expected Prize Wheel multiple, from the wedge weights (the odds the wheel prints). */
export function wheelMean(sectors) {
  let sum = 0, weight = 0;
  for (const s of sectors || []) { sum += (s.weight || 0) * (s.mult || 0); weight += s.weight || 0; }
  return weight ? sum / weight : 0;
}

/** Mini Slots: seconds of income one spin is worth on average (2 wins in 6, a quarter of wins the big sweep). */
export const SLOT_MEAN = Object.freeze({ fps: (2 / 6) * (0.75 * 45 + 0.25 * 90), click: (2 / 6) * (0.75 * 80 + 0.25 * 160) });

export function autopilot(ctx, { dueMs, key = AUTOPILOT_KEY }) {
  const share = () => autopilotShare(ctx.prestige);
  let ms = Number(ctx.store.get(key)) || 0;
  return {
    get share() { return share(); },
    step(dtMs, idle) {
      if (!share()) return false;
      const r = autopilotStep(ms, dtMs, idle, dueMs);
      if (r.ms !== ms) { ms = r.ms; ctx.store.set(key, ms); }
      return r.due > 0;
    },
    reset() { if (ms) { ms = 0; ctx.store.set(key, 0); } },
  };
}
