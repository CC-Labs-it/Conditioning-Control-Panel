/* ============================================================================
 * ui/fade.js - a short IN and OUT for the stands' blocks (owner rule: every
 * effect needs an in and an out). The block rises a few pixels into place and
 * sinks out of it; under reduced motion it simply appears and goes.
 *
 *   setShown(node, show, still)   still = reduced motion: no animation at all
 *
 * The page's [hidden] rule is display:none, so the OUT runs first and the
 * node is hidden when it ends (styles.css .pbp-fade-in / .pbp-fade-out). A
 * node without a classList (a smoke's fake) just has `hidden` set.
 * ==========================================================================*/

export const FADE_MS = 180;
const timers = new WeakMap();

export function setShown(node, show, still = false) {
  if (!node) return;
  if (!node.classList) { node.hidden = !show; return; }
  const t = timers.get(node);
  if (show) {
    if (t) { clearTimeout(t); timers.delete(node); }
    const leaving = node.classList.contains('pbp-fade-out');
    node.classList.remove('pbp-fade-out');
    if (!node.hidden && !leaving) return;
    node.hidden = false;
    node.classList.remove('pbp-fade-in');
    if (still) return;
    void node.offsetWidth;   // restart the run
    node.classList.add('pbp-fade-in');
    return;
  }
  if (node.hidden) return;
  if (still) { if (t) { clearTimeout(t); timers.delete(node); } node.classList.remove('pbp-fade-in', 'pbp-fade-out'); node.hidden = true; return; }
  if (node.classList.contains('pbp-fade-out')) return;
  node.classList.remove('pbp-fade-in');
  node.classList.add('pbp-fade-out');
  timers.set(node, setTimeout(() => {
    timers.delete(node);
    node.classList.remove('pbp-fade-out');
    node.hidden = true;
  }, FADE_MS));
}

/** Shown, or on its way in; not on its way out. */
export function isShown(node) {
  return !!node && !node.hidden && !(node.classList && node.classList.contains('pbp-fade-out'));
}
