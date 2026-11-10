// Distance is measured along the rail, including its hills and switchbacks.
const STEP = 0.25;
const EXTENT = 640;
const YAW = [
  [0, 0], [36, -.22], [84, .58], [122, .82], [158, -.70],
  [188, -.72], [228, .12], [265, .42], [305, .2], [338, -.42],
  [365, -.92], [393, .50], [420, .74], [466, .12], [520, -.48],
  [553, -.15], [580, 0], [EXTENT, 0],
];
const HEIGHT = [
  [0, 0], [105, 0], [130, 2], [145, 4], [178, -3],
  [338, -3], [362, 3], [395, -4], [440, -4], [520, 0], [EXTENT, 0],
];
const smooth = t => t * t * t * (t * (t * 6 - 15) + 10);
const slope = t => 30 * t * t * (t - 1) * (t - 1);
function sample(keys, s, derivative = false) {
  if (s <= keys[0][0]) return derivative ? 0 : keys[0][1];
  for (let i = 1; i < keys.length; i++) {
    const [end, b] = keys[i], [start, a] = keys[i - 1];
    if (s <= end) {
      const t = (s - start) / (end - start);
      return derivative ? (b - a) * slope(t) / (end - start) : a + (b - a) * smooth(t);
    }
  }
  return derivative ? 0 : keys.at(-1)[1];
}
export function trackYaw(s) { return sample(YAW, s); }
export function trackGrade(s) { return sample(HEIGHT, s, true); }
export function trackPitch(s) { return Math.asin(trackGrade(s)); }

// Midpoint integration gives a stable, unit-speed path without a Three.js dependency.
const points = [{ x: 0, z: 0 }];
for (let i = 1; i <= EXTENT / STEP; i++) {
  const s = (i - .5) * STEP, yaw = trackYaw(s), grade = trackGrade(s);
  const horizontal = Math.sqrt(1 - grade * grade);
  const previous = points[i - 1];
  points.push({ x: previous.x - Math.sin(yaw) * horizontal * STEP, z: previous.z - Math.cos(yaw) * horizontal * STEP });
}
export function trackPoint(distance) {
  if (distance < 0) return { x: 0, y: 0, z: -distance };
  const s = Math.min(EXTENT, distance), n = s / STEP;
  const a = points[Math.floor(n)], b = points[Math.min(points.length - 1, Math.floor(n) + 1)];
  const t = n - Math.floor(n);
  return { x: a.x + (b.x - a.x) * t, y: sample(HEIGHT, s), z: a.z + (b.z - a.z) * t - Math.max(0, distance - EXTENT) };
}
export function trackPosition(distance, side = 0, height = 0) {
  const point = trackPoint(distance), yaw = trackYaw(distance);
  return { x: point.x + Math.cos(yaw) * side, y: point.y + height, z: point.z - Math.sin(yaw) * side };
}
const ROOMS = [
  { from: 0, id: 'intake', label: 'The Subject', tint: '#4c213c', accent: '#ff80be', secondary: '#ffd087', bg: '#160e20' },
  { from: 130, id: 'connector', label: 'The attention span', tint: '#15354c', accent: '#72efff', secondary: '#ffb368', bg: '#081b2b' },
  { from: 180, id: 'reflex', label: 'Reflex laboratory', tint: '#102c40', accent: '#68e6ef', secondary: '#ffcb71', bg: '#071624' },
  { from: 340, id: 'descent', label: 'Down the rabbit hole', tint: '#36204d', accent: '#ff67d9', secondary: '#9991ff', bg: '#180c2b' },
  { from: 400, id: 'wakeup', label: 'The outside world', tint: '#b5d2bc', accent: '#baffdf', secondary: '#ffdca5', bg: '#243c3c' },
  { from: 520, id: 'theatre', label: 'One more thing', tint: '#522537', accent: '#ffa7c8', secondary: '#ffd582', bg: '#1a0a20' },
].map(room => Object.freeze(room));
export function trackStyle(s) { return ROOMS.findLast(room => s >= room.from) || ROOMS[0]; }

/** Powered lifts and gravity sections live between the script's slow exhibit rooms. */
export function trackMotion(s, reduced = false) {
  let mode = 'manual', target = 0, cap = reduced ? 6 : 14, acceleration = 0;
  if (s >= 118 && s < 145) { mode = 'lift'; target = reduced ? 3 : 4.5; acceleration = 1.8; }
  if (s >= 145 && s < 177) { mode = 'descent'; target = reduced ? 5 : 15; acceleration = 2.6; cap = reduced ? 6 : 17; }
  if (s >= 177 && s < 190) { mode = 'brake'; target = 4; acceleration = 3; cap = 4.5; }
  if (s >= 338 && s < 363) { mode = 'lift'; target = reduced ? 3 : 4.8; acceleration = 1.8; }
  if (s >= 363 && s < 395) { mode = 'descent'; target = reduced ? 5 : 17; acceleration = 3; cap = reduced ? 6 : 19; }
  if (s >= 395 && s < 410) { mode = 'brake'; target = 3.5; acceleration = 4; cap = 4.5; }
  return { mode, target, cap, acceleration, grade: trackGrade(s) };
}
