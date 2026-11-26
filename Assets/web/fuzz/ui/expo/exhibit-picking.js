// Targets can overlap in the hierarchy: a moving button and the exhibit which owns it
// may both be registered. Visit each visible drawable once, even when its parent is also a target.
export function pickExhibits(targets, ray, hits) {
  hits.length = 0;
  function visit(node) {
    if (!node.visible) return;
    if (node.layers.test(ray.layers) && node.raycast?.(ray, hits) === false) return;
    for (const child of node.children) visit(child);
  }
  for (const target of targets.keys()) {
    let parent = target.parent;
    while (parent && parent.visible && !targets.has(parent)) parent = parent.parent;
    if (!parent) visit(target);
  }
  hits.sort((a,b) => a.distance-b.distance);
}
