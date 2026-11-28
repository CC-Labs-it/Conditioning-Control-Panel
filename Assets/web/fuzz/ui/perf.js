/* ui/perf.js - phone budget helpers. Nothing here changes what the game does, only how often and how finely it draws.
 *
 * LITE: a phone or a small touch tablet (coarse pointer, no fine pointer, short screen side) or a device that reports
 * 2 GB or less. Canvas painters draw at a lower pixel ratio and frame rate there. `?lite=1` / `?lite=0` force it.
 *
 * frameRect(el): getBoundingClientRect, read at most once per element per frame. Every FX that aims at the button or
 * the counter used to read it on its own, between other writes, and each read forced a fresh style + layout pass (five
 * to seven a frame on a full page). app.js starts each frame with nextFrame() and reads the shared targets first, while
 * the page is still clean. Use it for aiming effects only, never for drag or resize maths. */

const win = typeof window !== 'undefined' ? window : null;

function detect() {
  if (!win) return false;
  try {
    const q = new URLSearchParams(win.location.search).get('lite');
    if (q === '1') return true;
    if (q === '0') return false;
    const mm = (s) => !!win.matchMedia?.(s).matches;
    const touchOnly = mm('(pointer: coarse)') && !mm('(any-pointer: fine)');
    const side = Math.min(win.screen?.width || win.innerWidth, win.screen?.height || win.innerHeight);
    const lowMemory = (win.navigator?.deviceMemory || 8) <= 2;
    return (touchOnly && side <= 900) || lowMemory;
  } catch { return false; }
}

export const LITE = detect();

/** Pixel ratio cap for canvases that redraw often. */
export const canvasDpr = (cap = 2) => Math.min(LITE ? Math.min(cap, 1.5) : cap, win?.devicePixelRatio || 1);

/** Milliseconds between repaints of a decorative canvas that runs at `fps` on a desktop. */
export const paintEvery = (fps = 60) => 1000 / (LITE ? Math.min(fps, 30) : fps);

let epoch = 0;
const cache = new WeakMap();

/** Start of a frame: rects taken before this are stale. */
export function nextFrame() { epoch++; }

export function frameRect(el) {
  const c = cache.get(el);
  if (c && c.epoch === epoch) return c.rect;
  const rect = el.getBoundingClientRect();
  cache.set(el, { epoch, rect });
  return rect;
}
