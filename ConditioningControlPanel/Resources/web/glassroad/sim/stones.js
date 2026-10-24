/* sim/stones.js - stepping stones over the void (wave 2 STONES, BRIEF-STONES.md). Pure, no three.js.
 *
 * A field (track.fields, track/courts.js) replaces a stretch of road deck with lanes of stones over a void. Inside
 * it the track query answers `stone` (the stone row under the kart) on a stone and `hole.lip` over the void. Every
 * stone edge is a kicker lip: sim/kart.js throws a grounded kart that leaves a stone up by stones.kickUpMps along
 * the road normal and takes off as 'lip' (a hop there still pays the trick). Below the deck over the void the
 * ordinary hole fall runs (HOLE_FALL in kart.js, or the fall onto the road beneath through sim/falls.js).
 * The numbers: clearSpeed() is the design check (a test pins kick vs gap vs gravity vs snap), fieldRescueNode()
 * moves a rescue anchor (and its release point aheadNodes later) before the field, lipLeave() is the one lip test kart.js asks each tick.
 */

/**
 * The minimum forward speed that carries a kart kicked up by kickUpMps across gapM metres of void back onto a deck
 * at the same height. The kart is caught while it sits at most snapM under the deck (kart.snapM, the grounded snap
 * band sim/kart.js lands within), so the flight lasts until the kick's arc has fallen snapM below the lip.
 * @param {number} gapM metres of void between two stone edges
 * @param {number} kickUpMps the lip kick (stones.kickUpMps)
 * @param {number} gravity airborne gravity (hop.gravityMps2)
 * @param {number} snapM the landing snap band (kart.snapM)
 * @returns {number} m/s
 */
export function clearSpeed(gapM, kickUpMps, gravity, snapM) {
  const k = Math.max(0, kickUpMps), g = Math.max(1e-6, gravity), d = Math.max(0, snapM);
  const t = (k + Math.sqrt(k * k + 2 * g * d)) / g;   // k t - g t^2 / 2 = -d, the later root
  return gapM / t;
}

/** The field containing the position s (track.fields), or null. */
export function fieldAtS(track, s) {
  const fs = track.fields;
  if (!fs || !fs.length) return null;
  for (const f of fs) if (s >= f.fromS - 1e-6 && s < f.toS - 1e-6) return f;
  return null;
}

/**
 * A rescue anchor never sits inside a stone field: a node inside one moves back to the last node before the field
 * (the deck the kart left, so the rescued kart retries the stones with its release speed).
 * @param {object} track loaded track (fields with fromS/toS)
 * @param {number} index node index
 * @returns {number} node index
 */
export function fieldRescueNode(track, index, aheadNodes = 0) {
  const n = track.nodeCount;
  let i = ((index % n) + n) % n;
  // the kart is released aheadNodes past the anchor: none of those nodes may sit in a field either
  for (let k = 0; k <= aheadNodes; k++) {
    const f = fieldAtS(track, track.nodeS[(i + k) % n]);
    if (!f) continue;
    let j = (i + k) % n;
    for (let t = 0; t < n && track.nodeS[j] >= f.fromS - 1e-6 && track.nodeS[j] < f.toS; t++) j = (j - 1 + n) % n;
    return (j - aheadNodes + n * 2) % n;
  }
  return i;
}

/**
 * True when a grounded kart just stepped off supported ground into the field's void: the previous ground query was
 * on road (the deck before the field or a stone), the new one answers hole.lip. The deck hands over to a void on the
 * staggered lanes (lane 0's first stone starts 3 m in), so the deck's edge at the field start kicks too. The road's
 * own open edges and court holes (no lip) never kick.
 * @param {object} prevQ the query the kart stood on at the start of the tick
 * @param {object} q the query after the move
 */
export function lipLeave(prevQ, q) {
  return !!(prevQ && prevQ.onRoad && q && q.hole && q.hole.lip === true && !q.stone);
}

/** A broad landing deck renews an early hop once at its actual lip, never over the rest of the void. */
export function broadPlatformEdgeKick(track, prevQ, q, normalSpeed, kickUpMps) {
  if (track?.id !== 'keep' || !prevQ?.stone || prevQ.stone.w < 24 || prevQ.stone.len < 24 ||
      !lipLeave(prevQ, q) || prevQ.height < -.3 || q.height < -.3 || q.height > 1.5) return 0;
  const stone = prevQ.stone, end = stone.s + stone.len;
  if (!Number.isFinite(end) || !Number.isFinite(prevQ.s) || !Number.isFinite(q.s) || !Number.isFinite(q.lateral) ||
      prevQ.s > end || q.s < end || q.s <= prevQ.s || Math.abs(q.lateral - stone.x) > stone.w / 2) return 0;
  return Math.max(0, Math.max(0, kickUpMps) - normalSpeed);
}
