/* guide.js - when the "How it works" card opens by itself: once per account, on a counter that is up and has nothing
 * else going on. Pure: no DOM, the storage goes in. Remembering is per viewer and best effort, so a storage that throws
 * or is missing reads as "already seen": the card never nags, and its door in the HUD still opens it. */

export const SEEN_PREFIX = 'pc.howto.seen.';
export const seenKey = (init) => SEEN_PREFIX + ((init && init.identity && init.identity.unifiedId) || 'anon');

/** A counter opened to put something on: `init.wear` names the piece, or the door is the dressing room itself. */
const WEAR_DOORS = ['rain-wardrobe'];
export const opensToWear = (init) => !!(init && (init.wear || WEAR_DOORS.includes(init.from)));

function seen(storage, key) {
  try { return !storage || storage.getItem(key) !== null; } catch (e) { return true; }
}

/** status = shop.status(), card = shop.card(). */
export function guideDue({ init, status, card, storage }) {
  return status === 'ready' && !card && !opensToWear(init) && !seen(storage, seenKey(init));
}

export function markGuideSeen(storage, init) {
  try { if (storage) storage.setItem(seenKey(init), '1'); } catch (e) { /* best effort */ }
}
