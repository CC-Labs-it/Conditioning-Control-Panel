/* ============================================================================
 * skins/mount.js - where an insignia sits on a man (no three.js here, so node
 * can test it).
 *
 * Astra's insignias are authored on the piece's FRONT (-Z, the way white looks
 * down the board), so on the board they faced away from the player who owns
 * them (desk run 2026-10-30, 09c-insignia-zoom). Each side's camera sits behind
 * its own men (white at +Z; black's men and black's camera are both turned
 * half round), so the badge belongs on the man's BACK: the side its owner sees.
 *
 * attachments.js turns the badge half round about the up axis (+Z now, still
 * upright, the flex reads position.y unchanged), and backMountShift() then
 * slides it along Z so it rests just off the body's back inside its own
 * footprint. The sculpted piece is only read, never moved or rebuilt.
 * ==========================================================================*/

/** Clearance between a badge's inner face and the body, board units (a square is 1.0). */
export const MOUNT_GAP = Object.freeze({ min: 0.006, max: 0.045, rest: 0.018 });

/** The union box of several boxes ({ min: {x,y,z}, max: {x,y,z} }), or null for none. */
export function unionBox(boxes) {
  let out = null;
  for (const b of boxes || []) {
    if (!b || !b.min || !b.max) continue;
    if (!out) { out = { min: { ...b.min }, max: { ...b.max } }; continue; }
    for (const k of ['x', 'y', 'z']) {
      out.min[k] = Math.min(out.min[k], b.min[k]);
      out.max[k] = Math.max(out.max[k], b.max[k]);
    }
  }
  return out;
}

/**
 * The Z slide that seats a back-mounted badge. `body` is the man's position
 * attribute (anything with count/getX/getY/getZ), `box` the badge's box after
 * the turn. Only body points inside the badge's X/Y footprint count, so a
 * slim waist or a cut in the back never lets the badge sink and a bulge never
 * lets it clip. 0 when the badge already rests inside the gap, or when no body
 * point falls in the footprint (a quiet no: the badge stays as authored-turned).
 */
export function backMountShift(body, box) {
  if (!body || !box || !(body.count > 0)) return 0;
  let back = -Infinity;
  for (let i = 0; i < body.count; i++) {
    const x = body.getX(i), y = body.getY(i);
    if (x < box.min.x || x > box.max.x || y < box.min.y || y > box.max.y) continue;
    const z = body.getZ(i);
    if (z > back) back = z;
  }
  if (!Number.isFinite(back)) return 0;
  const gap = box.min.z - back;
  if (gap >= MOUNT_GAP.min && gap <= MOUNT_GAP.max) return 0;
  return back + MOUNT_GAP.rest - box.min.z;
}
