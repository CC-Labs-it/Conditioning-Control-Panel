/* A pad must leave a turn the grip steering can actually follow, including its tail.
 * Apply to authored pads as well as generated trains. No road geometry is moved.
 */
export const BOOST_SAFETY_REVISION = 1;
export const BOOST_TURN = Object.freeze({ rolloutM: 110, forcedM: 55, sampleM: 2, radiusM: 100, cruiseRadiusM: 63, padSpeedMps: 36 });

/** Maximum horizontal bend on the actual lane, including the pad and its full runoff. */
export function boostTurnRadius(track, pad, clearance = false) {
  const s = pad.s ?? track.nodeS[pad.node], x = pad.x || 0;
  let peak = 0;
  const end = pad.len + BOOST_TURN.rolloutM;
  for (let d = -4; ; d = Math.min(end, d + BOOST_TURN.sampleM)) {
    const a = track.surfaceAt(s + d - 2, x).p, b = track.surfaceAt(s + d, x).p, c = track.surfaceAt(s + d + 2, x).p;
    const ax = b[0] - a[0], az = b[2] - a[2], bx = c[0] - b[0], bz = c[2] - b[2];
    const al = Math.hypot(ax, az), bl = Math.hypot(bx, bz);
    // Vertical loop crowns have no horizontal heading; they are intentional launches.
    const angle = al < .6 || bl < .6 ? 0 : Math.atan2(Math.abs(ax * bz - az * bx), ax * bx + az * bz);
    const curvature = angle / Math.max(.001, (al + bl) / 2);
    // Pad floor holds for 1s, then its 0.5s exponential tail releases. Reserve
    // 55m at the full boosted steering radius, then another 55m of braking room.
    // At 36m/s full-lock grip needs 97.37m; ordinary 30m/s needs 61.12m.
    const tail = Math.max(0, Math.min(1, (d - pad.len - BOOST_TURN.forcedM) / (BOOST_TURN.rolloutM - BOOST_TURN.forcedM)));
    const required = BOOST_TURN.radiusM + (BOOST_TURN.cruiseRadiusM - BOOST_TURN.radiusM) * tail;
    peak = Math.max(peak, curvature * (clearance ? required : 1));
    if (d === end) break;
  }
  return peak > 0 ? 1 / peak : Infinity;
}

export function safeBoostTurn(track, pad) {
  return boostTurnRadius(track, pad, true) >= 1;
}
