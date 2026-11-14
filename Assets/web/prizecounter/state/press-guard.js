/* press-guard.js - a confirm (Space, Enter, pad A) only counts when it was meant. Pure: no DOM, a clock goes in.
 *
 * Desk bug 2026-10-30: the counter opened while the player was typing in another app, took focus, and stray
 * Spaces bought an item (no refunds) and wore it. Owner rule: tap + confirm. So a keyboard or pad confirm counts
 * only when the key went DOWN and came back UP inside the counter, the counter had focus for FOCUS_MS before the
 * press, the press is not an auto-repeat, and the confirm step (the card, or a new face on it) has been up for
 * CARD_MS before the press. A key already held when the window gained focus never confirms. Pointer clicks are
 * not keys: they only wait out CARD_MS on a fresh card (ready()).
 *
 *   const g = createPressGuard({ now, focused });
 *   g.focus(true|false); g.card();               // window focus, a confirm step appeared or changed its action
 *   g.down('Space', ev.repeat); if (g.up('Space')) confirm();
 *   g.ready()                                     // a pointer click on the card's action counts */

export const FOCUS_MS = 600, CARD_MS = 400;

export function createPressGuard({ now = () => Date.now(), focused = false, focusMs = FOCUS_MS, cardMs = CARD_MS } = {}) {
  let focusAt = focused ? now() : null;   // null = the counter does not have focus
  let cardAt = -Infinity;
  const held = new Map();                 // key -> { at, ok }
  return {
    focus(on) {
      if (!on) { focusAt = null; held.clear(); return; }
      if (focusAt === null) focusAt = now();
    },
    /** A confirm step appeared (or its action changed): presses that began before CARD_MS from now never count. */
    card() { cardAt = now(); },
    down(key, repeat = false) {
      const t = now(), was = held.get(key);
      if (was && repeat) { was.ok = false; return; }   // held: the repeat taints the whole press
      // Keys only reach a focused page, so a key down proves focus when the focus event went missing (pads do not).
      if (focusAt === null && !String(key).startsWith('pad:')) focusAt = t;
      const ok = !repeat && focusAt !== null && t - focusAt >= focusMs && t - cardAt >= cardMs;
      held.set(key, { at: t, ok });
    },
    /** True only when this release ends a deliberate press. */
    up(key) {
      const p = held.get(key);
      held.delete(key);
      return !!(p && p.ok && focusAt !== null && p.at - cardAt >= cardMs);
    },
    ready() { return now() - cardAt >= cardMs; },
  };
}

/** The guard's name for a key event: Space, Enter, NumpadEnter (code first, key as the fallback). */
export const keyName = (ev) => String((ev && (ev.code || ev.key)) || '').replace(/^ $/, 'Space');
