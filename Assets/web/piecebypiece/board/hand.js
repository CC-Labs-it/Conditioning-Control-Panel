/* ============================================================================
 * board/hand.js - the three rules the hand in board/drag.js plays by, kept
 * free of three.js so node can test them (smoke/hand-aim.test.mjs).
 *
 *   isClick       a press whose pointer never travelled is a click, however
 *                 long it was held. A slow, still press used to turn into a
 *                 drag and could play the man one square back (ccp-bugs #1621).
 *   holdOffset    where a lifted man hangs off the cursor. It is measured on
 *                 the plane the cursor drags along, not at the grabbed point,
 *                 so a man lifted under a cursor that has not moved stays over
 *                 his own square. Measured at the grabbed point, the lift alone
 *                 put his base half a square to a square nearer the camera.
 *   clickTarget   the one answer to "which square does this click mean" while
 *                 a man waits for his square. The lit square and the square a
 *                 click plays both come from here, so a man standing in front
 *                 of the lit square cannot take the click.
 *
 * Points and rays are plain { x, y, z }; a THREE.Vector3 is one.
 * ==========================================================================*/

/** Record the browser's input time, even if its handler ran late. */
export function startPress(ev) {
  return { x: ev.clientX, y: ev.clientY, t: ev.timeStamp, far: 0 };
}

/**
 * Was this press a click? `press` is { x, y, far }, where `far` is the
 * farthest the pointer got from the press point, in client pixels.
 */
export function isClick(press, upX, upY, stillPx) {
  if (!press) return false;
  const far = Math.max(press.far || 0, Math.hypot(upX - press.x, upY - press.y));
  return far <= stillPx;
}

/** Where a ray crosses the level y, or null when it never does. */
export function crossAt(origin, dir, y) {
  if (Math.abs(dir.y) < 1e-9) return null;
  const t = (y - origin.y) / dir.y;
  if (t < 0) return null;
  return { x: origin.x + dir.x * t, y, z: origin.z + dir.z * t };
}

/**
 * The x/z offset from where the press ray meets the hold plane to the man's
 * own square, so that the same ray puts him straight back over it.
 */
export function holdOffset(base, origin, dir, holdY) {
  const p = crossAt(origin, dir, holdY);
  return p ? { x: base.x - p.x, z: base.z - p.z } : { x: 0, z: 0 };
}

/** Where a held man stands for a ray: its hold-plane crossing plus the offset. */
export function heldAt(offset, origin, dir, holdY) {
  const p = crossAt(origin, dir, holdY);
  return p ? { x: p.x + offset.x, z: p.z + offset.z } : null;
}

/**
 * With a man waiting, the square a cursor means. `manSq` is the square of the
 * man the ray hit first (any colour), `boardSq` the square the ray meets at
 * board level; they differ when a man's body covers the square behind him.
 *
 *   - one of your own men answers for himself: a click on him moves the
 *     selection over, and a king's own rook is how he castles;
 *   - a man you may take answers for himself;
 *   - otherwise a legal square on the board answers, even under a man's body;
 *   - otherwise nothing legal is meant, and the click lets the man go.
 */
export function clickTarget(manSq, boardSq, legal, canPick) {
  if (manSq && (canPick(manSq) || legal.includes(manSq))) return manSq;
  if (boardSq && legal.includes(boardSq)) return boardSq;
  return manSq || boardSq || null;
}
