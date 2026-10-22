/* render/warmGroups.js - how the boot warm splits the scene into passes (render/renderer.js warmAll). Pure.
 *
 * Firefox links every WebGL program serially, so one warm pass over the whole scene was a ~7 s task with
 * the page frozen. The warm instead shows one group of renderables per pass with a painted frame between,
 * each group bringing at most perGroup materials the warm has not met yet (a renderable sharing only known
 * materials rides along free), so the warming card's bar moves and no single task runs long.
 */

export const WARM = Object.freeze({ perGroup: 6 });

/** Renderables in scene order, grouped by materials new to the warm. */
export function warmGroups(items, perGroup = WARM.perGroup) {
  const seen = new Set(), groups = [];
  let cur = [], fresh = 0;
  for (const o of items) {
    let n = 0;
    for (const m of [].concat(o.material || [])) if (m && !seen.has(m.id ?? m)) { seen.add(m.id ?? m); n++; }
    if (cur.length && n && fresh + n > perGroup) { groups.push(cur); cur = []; fresh = 0; }
    cur.push(o); fresh += n;
  }
  if (cur.length) groups.push(cur);
  return groups;
}
