/* render/warmGroups.js - how the boot warm splits the scene into passes (render/renderer.js warmAll). Pure.
 *
 * Firefox can link WebGL programs serially. The warm shows groups of renderables with a painted frame between,
 * each group bringing at most perGroup materials the warm has not met yet (a renderable sharing only known
 * materials rides along free). Async shader construction also yields within a group. Larger groups avoid
 * repeatedly rebuilding receiver, shadow and bloom passes while the loading card still gets updates.
 */

// WebGPU pipelines already share a bounded asynchronous queue. Larger scene batches
// avoid repeating receiver/shadow/post preparation; serial WebGL keeps shorter tasks.
export const WARM = Object.freeze({ perGroup: 24, webgpuPerGroup: 96 });

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
