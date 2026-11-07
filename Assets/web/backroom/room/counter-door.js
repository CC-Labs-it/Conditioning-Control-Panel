/** THE 3D PRIZE COUNTER DOOR (7.2, prizecounter CONTRACT + shop lane brief, contract 1).
 *
 * Walking to the counter and pressing E opens the Prize Counter WINDOW over the room on its Back Room tab
 * instead of the 2D station cards: the page posts `{ type:'open-prize-counter', tab:'backroom', from:'backroom' }`,
 * the host suspends the room and opens the counter over it, and when the counter closes the host sends
 * `{ type:'owned', grants }`. The room then re-reads the counter's state and plays one shelf stamp per row
 * bought meanwhile, one after another (the shelf-stamp queue), exactly as a 2D purchase stamped its row.
 *
 * Only a host that says `init.prizeCounter: true` gets the window; any other host (the web shim today)
 * keeps the 2D station. The server keeps the four 2D Racing Thoughts rows hidden on both counters. */

export const COUNTER_TAB = 'backroom';
export const COUNTER_FROM = 'backroom';
/** The shelf's own left-to-right order (room/prize-display.js): stamps land in this order. */
export const SHELF_ORDER = Object.freeze(['jackpot_remix', 'rt_demo', 'high_roller', 'flashes_v2', 'bubbles_v2', 'rt_bundle_1', 'rt_bundle_2', 'rt_bundle_3']);
/** A little past one prize flight (shared/prize-state.js prizeFrame runs 1.25 s), so stamps never overlap. */
export const STAMP_GAP_MS = 1400;

const rank = (id) => { const i = SHELF_ORDER.indexOf(id); return i < 0 ? SHELF_ORDER.length : i; };
const ownedOf = (snapshot) => (Array.isArray(snapshot?.owned) ? snapshot.owned.filter((id) => typeof id === 'string') : []);

/** Rows owned in `after` and not in `before`, in shelf order, each once. */
export function newlyOwned(before, after) {
  const had = new Set(ownedOf(before));
  return [...new Set(ownedOf(after).filter((id) => !had.has(id)))].sort((a, b) => rank(a) - rank(b));
}

/** One snapshot per stamp: each step owns one more of the fresh rows, so the stamps land one after another.
 * Nothing new = one plain step (a refresh, no stamp). */
export function stampSteps(before, after) {
  if (!after) return [];
  const fresh = newlyOwned(before, after);
  if (!fresh.length) return [{ snapshot: after, bought: null }];
  const base = ownedOf(after).filter((id) => !fresh.includes(id));
  return fresh.map((id, i) => ({ snapshot: { ...after, owned: [...base, ...fresh.slice(0, i + 1)] }, bought: id }));
}

/** The queue: `play(before, after)` applies each step and waits a flight between stamps. A newer play
 * (or cancel) drops what is left of the older one; still motion lands every stamp at once. */
export function createStampQueue({ apply, still = () => false, wait = (ms) => new Promise((r) => setTimeout(r, ms)) }) {
  let run = 0;
  async function play(before, after) {
    const token = ++run;
    const steps = stampSteps(before, after);
    for (let i = 0; i < steps.length; i++) {
      if (token !== run) return false;
      apply(steps[i].snapshot, steps[i].bought);
      if (i < steps.length - 1 && !still()) await wait(STAMP_GAP_MS);
    }
    return token === run;
  }
  return { play, cancel() { run++; } };
}

/** The door itself. `enabled()` is the host's `init.prizeCounter`; `onOpen` holds the room, `onReturn(m)`
 * runs once per close (`owned`). An `owned` with no counter open is still handed on (a fresh grants frame). */
export function createCounterDoor({ send, on, enabled = () => false, onOpen, onReturn }) {
  let open = false, disposed = false;
  const stop = typeof on === 'function' ? on('owned', (m) => {
    if (disposed) return;
    const was = open;
    open = false;
    onReturn?.(m, was);
  }) : null;
  return {
    get isOpen() { return open; },
    available() { return !disposed && enabled() === true && typeof send === 'function'; },
    open() {
      if (open || !this.available()) return false;
      open = true;
      try { onOpen?.(); } catch { /* the room still asks */ }
      send({ type: 'open-prize-counter', tab: COUNTER_TAB, from: COUNTER_FROM });
      return true;
    },
    dispose() { disposed = true; open = false; stop?.(); },
  };
}
