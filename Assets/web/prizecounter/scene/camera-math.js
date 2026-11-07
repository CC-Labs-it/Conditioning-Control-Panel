/* camera-math.js - the numbers behind the counter camera. Pure: no DOM, no three. */

// How much of each move the motion setting keeps: full, less (half), off (cuts).
export const motionAmp = (motion) => (motion === 'off' ? 0 : motion === 'less' ? 0.5 : 1);

// Frame-rate independent ease toward a target: the share of the gap closed in dt seconds at `rate` per second.
export const damp = (dt, rate) => 1 - Math.exp(-Math.max(0, dt) * rate);

// Vertical field of view (degrees) that fits a box w x h seen from distance d on a screen of this aspect.
export function fitFov(aspect, w, h, d) {
  const half = Math.max(h / 2, w / 2 / Math.max(0.2, aspect));
  return (2 * Math.atan(half / Math.max(0.01, d)) * 180) / Math.PI;
}

/* The camera empties are Blender empties: their view axis is local -Y, up is local -Z (glTF). A three.js camera
 * looks down local -Z with +Y up, so its quaternion is the empty's times a -90 degree turn about X. [x,y,z,w]. */
export function emptyToCameraQuat([x, y, z, w]) {
  const s = Math.SQRT1_2, bx = -s, bw = s;               // Rx(-90deg)
  return [w * bx + x * bw, y * bw + z * bx, z * bw - y * bx, w * bw - x * bx];
}

// Forward (-Z) of a quaternion [x,y,z,w].
export function forwardOf([x, y, z, w]) {
  return [-(2 * (x * z + w * y)), -(2 * (y * z - w * x)), -(1 - 2 * (x * x + y * y))];
}

// Where the view ray from `pos` along `dir` crosses the plane z = planeZ (the shelf fronts).
export function lookPoint(pos, dir, planeZ) {
  const t = Math.abs(dir[2]) > 1e-6 ? (planeZ - pos[2]) / dir[2] : 6;
  const k = t > 0 ? t : 6;
  return [pos[0] + dir[0] * k, pos[1] + dir[1] * k, pos[2] + dir[2] * k];
}

/* Lazy follow: the look target drifts a share of the way from the bay's centre toward the focused item, and the
 * eye slides a smaller share so the bay never leaves the frame. Returns { look, eyeShift }. */
export const FOLLOW = { look: [0.4, 0.18], eye: [0.2, 0.08] };   // [x, y]: rows drift less than columns
export function follow(bayLook, item, amp = 1) {
  const d = [item[0] - bayLook[0], item[1] - bayLook[1]];
  return {
    look: [bayLook[0] + d[0] * FOLLOW.look[0] * amp, bayLook[1] + d[1] * FOLLOW.look[1] * amp, bayLook[2]],
    eyeShift: [d[0] * FOLLOW.eye[0] * amp, d[1] * FOLLOW.eye[1] * amp, 0],
  };
}

/* Portrait (a phone): the whole bay fits by width and leaves the shelf small and low under a tall empty band. So a
 * portrait view frames the shelf's item block instead (three columns edge to edge), looks at its middle rather than
 * the empty's look point, and drops the lazy follow (the columns already fill the width). `share` blends in from
 * aspect 1 to fully portrait at 0.65, so a landscape screen keeps the bay pose exactly. */
export const PORTRAIT = { frame: [2.62, 3.1], below: 1.45, from: 1, full: 0.65 };
export const portraitShare = (aspect) => Math.min(1, Math.max(0, (PORTRAIT.from - aspect) / (PORTRAIT.from - PORTRAIT.full)));
export function bayView(pose, item, bayTop, aspect, amp = 1) {
  const p = portraitShare(aspect), mix = (a, b) => a + (b - a) * p;
  const f = follow(pose.look, item, amp * Math.min(1, aspect) * (1 - p));
  return { look: [f.look[0], mix(f.look[1], bayTop - PORTRAIT.below), f.look[2]], eyeShift: f.eyeShift,
    frame: [mix(pose.frame[0], PORTRAIT.frame[0]), mix(pose.frame[1], PORTRAIT.frame[1])], share: p };
}

/* Fit a w x h box into the band a HUD leaves free (top / bottom insets in px of a view viewH px tall): the full view's
 * fov, and the vertical view offset in px (three's setViewOffset y) that puts the look point in the band's middle. */
export function bandFit(aspect, w, h, d, viewH, top = 0, bottom = 0) {
  const band = Math.max(1, viewH - top - bottom), k = viewH / band;
  const half = Math.tan((fitFov(aspect * k, w, h, d) * Math.PI) / 360) * k;
  return { fov: (2 * Math.atan(half) * 180) / Math.PI, offsetY: (bottom - top) / 2 };
}

// Zoom-out dip while a jump pan is still far from home: grows with the distance left, capped; 0 once close.
export const DIP = { perMetre: 0.32, max: 1.4, settle: 0.12 };
export function dipFor(distLeft, amp = 1) {
  if (!(distLeft > DIP.settle) || amp <= 0) return 0;
  return Math.min(DIP.max, distLeft * DIP.perMetre) * amp;
}

// The focused item's bob: metres up at time t (seconds), period 1.3 s like the 2D counter.
export const BOB = { height: 0.03, period: 1.3 };
export const bob = (t, amp = 1) => ((1 - Math.cos((2 * Math.PI * t) / BOB.period)) / 2) * BOB.height * amp;
