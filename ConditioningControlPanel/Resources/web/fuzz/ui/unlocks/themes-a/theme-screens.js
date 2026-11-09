/* ui/unlocks/themes-a/theme-screens.js - the first pictures a theme brings: exactly three picture screens (owner 2026-11-08: "Limit
 * the pictures at 3 random screens that rotate images, that they can move"). Shared by both theme lanes (themes-b imports it).
 *
 * Each one is a real dock object from ui/screens.js, like the Picture Frame: hold the casing to move, its own bottom tab, its
 * layout saved, resizable through the dock. Fresh ones open in free room on a random side (the dock keeps them off the button,
 * shop, counter and Wake Up, and off each other). One picture each, from the run's source (Scrolller credits ride along in the
 * screen's credit line); together they swap one at a time, so each changes about every 10 s and never two at once. Crossfade
 * in ui/screens.js mediaSlot; Gentle makes it a slower plain fade; nothing here moves, so reduced motion only keeps the swaps.
 * With No Pictures picked (f3 blind) they never mount, and they fold away if that pick comes later.
 * On a phone they open on their own too when the dock finds free room (owner 2026-11-09, Pixel 7: folded tabs read as
 * "no pictures"); a screen with no room waits as a tab wearing NEW. Other objects still start folded on a phone.
 * Saves from before that fix kept the three folded (a saved layout wins): once per save, on a phone, they open again
 * (REOPEN_KEY in the row's store), and after that the player's own fold sticks.
 *
 * One of the three (owner 2026-11-08: "1 in 3 should be a vid or gif") is a MOTION screen, picked at random once and kept in the
 * row's store: it asks for a gif or a clip (own files, or Scrolller video rows), muted, looped, inline. With no motion in the pool
 * it shows a still and asks for motion again on its next turn. Pause, Wake Up, Gentle and reduced motion hold it on a still frame
 * (ui/screens.js holdMedia). A clip may keep its turn until its first play is through, at most CLIP_HOLD_MS.
 *
 * The rest of a theme's picture dressing waits for that theme's own Pictures row (the *_feed items). */

import { screen } from '../../screens.js';
import { dockIsRail } from '../../dock.js';

/** Store key: the one-time reopen of screens a pre-2026-11-09 phone save kept folded has run. */
export const REOPEN_KEY = 'phoneReopened';

/** Should this mount open the folded screens once? Phones only, once per save, never with No Pictures picked. */
export function reopenOnce(store, { width, blind }) {
  if (blind || dockIsRail(width) || store?.get?.(REOPEN_KEY)) return false;
  store?.set?.(REOPEN_KEY, true);
  return true;
}

/** How often each of the three screens changes its picture. */
export const THEME_SCREEN_MS = 10000;
/** The longest a clip keeps its screen before the next turn swaps it. */
export const CLIP_HOLD_MS = 20000;

/** True while a clip on show should keep its turn: a video still in its first play, and under CLIP_HOLD_MS. */
export function holdsClip(media, shownAt, now = Date.now(), maxMs = CLIP_HOLD_MS) {
  if (media?.tagName !== 'VIDEO' || !shownAt) return false;
  const length = Number(media.duration) * 1000;
  if (!Number.isFinite(length) || length <= 0) return false;
  return now - shownAt < Math.min(length, maxMs);
}

/** Which of the screens moves: kept in the row's store so a reload keeps the same one. Cosmetic, so Math.random, never the run rng. */
export function motionIndex(store, count = 3, roll = Math.random) {
  const kept = store?.get?.('motionSpot');
  if (Number.isInteger(kept) && kept >= 0 && kept < count) return kept;
  const pick = Math.min(count - 1, Math.floor(roll() * count));
  store?.set?.('motionSpot', pick);
  return pick;
}
const SHAPES = [{ ratio: 1.2, scale: 1 }, { ratio: 0.82, scale: 0.92 }, { ratio: 1.05, scale: 0.84 }];

/**
 * spec: { niches: [...] base niches for the theme, frame: css for the screen face (light theme dressing),
 *         decorate(me): optional per-screen extra (bars, a lock) }
 * Returns the three screens (empty when No Pictures is picked).
 */
export function themeScreens(ctx, { niches, frame = '', decorate = null } = {}) {
  const blind = () => ctx.pick('f3') === 'blind';
  if (blind()) return [];
  const base = () => niches[Math.floor(ctx.rng() * niches.length) % niches.length];
  /* Random sides, but never all three on one: two sides are dealt, the third is a coin. */
  const sides = ['left', 'right', ctx.rng() < 0.5 ? 'left' : 'right'].sort(() => ctx.rng() - 0.5);
  /* Three short tabs that say which is which (the theme's icon says whose), not the row's long name three times. */
  const moving = motionIndex(ctx.store, SHAPES.length);
  const list = SHAPES.map((shape, i) => {
    const motion = i === moving;
    const me = screen(ctx, { kind: motion ? 'gif' : 'picture', stillFallback: motion, ...shape, niche: base, every: 0, first: i * 350, css: frame, key: `spot${i}`, pays: i === 0, edge: sides[i], title: `Picture ${i + 1}`, open: true });
    me.motion = motion;
    const next = me.next;
    me.next = () => (blind() || (motion && holdsClip(me.slot?.media, me.slot?.shownAt)) ? undefined : next());
    decorate?.(me, i);
    return me;
  });
  /* One swap every 10/3 s, round the three: each screen changes about every 10 s and never with another. The round starts
   * late enough that every first picture stays up a full turn. */
  if (reopenOnce(ctx.store, { width: ctx.bounds().w, blind: blind() })) {
    for (const me of list) { const d = me.el.parentElement?.dock; if (d && !d.visible) d.setVisible(true); }
  }
  const step = Math.round(THEME_SCREEN_MS / list.length);
  let k = 0;
  ctx.after(THEME_SCREEN_MS - step, () => ctx.every(step, () => list[k++ % list.length].next()));
  ctx.on('fork', ({ fork, lane }) => {
    if (fork !== 'f3' || lane !== 'blind') return;
    for (const me of list) { me.slot?.clear(); me.el.parentElement?.dock?.setVisible(false); }
  });
  return list;
}

/** True while a screen holds a decoded picture (for dressing that must only show over a real picture). */
export const hasPicture = (me) => !!me.slot?.media;
