// A changing road tangent must not hide a real crossing of its curved surface.
export function crossedSupportedRoad(track, before, after) {
  if (!before.onRoad || !after.onRoad || before.height < -.03 || after.height > 0 ||
      (before.forkId || null) !== (after.forkId || null)) return false;
  const distance = Math.abs(after.seg - before.seg);
  // Adjacent main segments also meet at the lap seam. Forks do not wrap.
  return distance <= 1 || !before.forkId && distance === track.nodeCount - 1;
}
