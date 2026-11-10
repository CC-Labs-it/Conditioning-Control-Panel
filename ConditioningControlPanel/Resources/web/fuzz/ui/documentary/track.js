/** The ride model measures distance along a gently winding gallery. */
export function trackPoint(distance) {
  return { x: 3 * Math.sin(distance / 36), y: 0, z: -distance };
}

export function trackYaw(distance) {
  return -Math.atan((3 / 36) * Math.cos(distance / 36));
}

export function trackPosition(distance, side = 0, height = 0) {
  const point = trackPoint(distance);
  const yaw = trackYaw(distance);
  return {
    x: point.x + Math.cos(yaw) * side,
    y: height,
    z: point.z - Math.sin(yaw) * side,
  };
}
