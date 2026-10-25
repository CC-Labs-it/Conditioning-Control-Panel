/* ============================================================================
 * backroom/room/screen-deal.js - which dealt picture each wall screen shows.
 *
 * ccp-bugs #1451: the same GIF sat on several screens at once while other dealt
 * pictures were nowhere. screens.js used to give screen i the pictures at
 * (turn + i), (turn + i + 1) and (turn + i + 2), so a screen showing two or three
 * panels repeated its neighbours' pictures, and the projector's six tiles repeated
 * the walls'.
 *
 * Now every screen face (the one to three pictures a screen shows for one turn,
 * or the projector's six tiles) is HELD here. A face that turns gives its
 * pictures back and draws new ones: never one already on the same face, never
 * the face's previous picture while another can take its place, then the picture
 * on the fewest screens, then the one shown longest ago. So screens up at the
 * same time show DISTINCT pictures whenever the deal has enough, and a deal with
 * fewer pictures than screens spreads them evenly instead of piling on one.
 *
 * Pure: indices only, no three.js, no DOM.
 * ==========================================================================*/

export function createScreenDeal(size = 0) {
  let uses = [], last = [], clock = 0;
  const held = new Map();

  /** A new gallery: every hold is dropped, the next hold draws from `n` pictures. */
  function reset(n) {
    size = Math.max(0, Math.floor(Number(n) || 0));
    uses = new Array(size).fill(0); last = new Array(size).fill(-1); clock = 0; held.clear();
  }

  function give(k) { uses[k]--; }
  function take(k) { uses[k]++; last[k] = ++clock; }

  /** The best picture outside `avoid`, or -1. Fewest screens first, then the longest unseen. */
  function best(avoid) {
    let pick = -1;
    for (let k = 0; k < size; k++) {
      if (avoid.has(k)) continue;
      if (pick < 0 || uses[k] < uses[pick] || (uses[k] === uses[pick] && last[k] < last[pick])) pick = k;
    }
    return pick;
  }

  function draw(items, previous) {
    const own = new Set(items);
    let k = best(new Set([...own, ...previous]));
    if (k < 0) k = best(own);          // the deal is smaller than the face plus its last turn
    if (k < 0) k = best(new Set());    // the deal is smaller than the face itself
    take(k); items.push(k);
  }

  /** Free a screen that is gone (its pack switched off), so its pictures go to the others. */
  function release(key) {
    const h = held.get(key);
    if (!h) return;
    h.items.forEach(give); held.delete(key);
  }

  /**
   * The pictures screen `key` shows for turn `face`. The same turn keeps the same pictures.
   * @param need a count, or (first picture) => count, read on every call so a panel count
   *             that follows the first picture's shape can grow or shrink inside a turn.
   */
  function hold(key, face, need = 1) {
    if (!size) return [];
    let h = held.get(key);
    if (!h || h.face !== face) {
      const previous = h ? h.items : [];
      if (h) h.items.forEach(give);
      h = { face, items: [] }; held.set(key, h);
      draw(h.items, previous);
      h.previous = previous;
    }
    const want = Math.max(1, Math.floor(Number(typeof need === 'function' ? need(h.items[0]) : need) || 1));
    while (h.items.length > want) give(h.items.pop());
    while (h.items.length < want) draw(h.items, h.previous);
    return h.items;
  }

  /** Test seam: how many screens hold each picture right now. */
  const counts = () => uses.slice();

  reset(size);
  return { reset, hold, release, counts, get size() { return size; } };
}
