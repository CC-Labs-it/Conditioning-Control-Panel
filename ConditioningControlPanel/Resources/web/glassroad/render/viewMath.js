/* render/viewMath.js - the VIEW lane numbers (polish 6): kart visual scale, the chase framing check and the
 * near roadside cue plan. Pure (no three.js, no DOM), so node tests read it.
 *
 * Owner follow-up (2026-10-23): bring the chase camera closer and make every racer slightly larger so EMI
 * reads clearly during play, held below the middle to match the supplied reference. The camera distances
 * live in feel.json "camera"; shared size and composition live in framing.js (KART_VIEW.scale, applied on every kart root by
 * render/karts.js, so the pose, sparks, wakes, skids, ring and aura all follow it). The sim's collision sizes
 * retain the accepted P6 values (feel.json contact.radiusM, sim/gaps.js kart.radiusM).
 *
 * The near cues (render/nearDressing.js): lamp posts just outside a railed edge, a banner on every other
 * post, streaming past the kart at a steady rhythm. They give the eye speed and a human scale next to the
 * road. Static scenery: nothing flashes or pulses, fog fades them with distance.
 */
import { vFovForAspect } from './cameraMath.js';
import { SPECTRUM_BASE, sectionKey } from './spectrum.js';
import { onLoop } from './colossiMath.js';
import { RACER_FRAMING } from './framing.js';
import { CHASE_NEUTRAL_PITCH_DEG } from '../core/chaseView.js';

/**
 * Kart look scale. modelRadiusM / modelUpM are conservative framing dimensions at scale 1, including
 * a little room around the R4 saucer's measured 0.72 m radius and the different vehicle silhouettes.
 * keepSprites: the name tag and head mark keep their world size (they are read at range), only their height
 * follows the kart.
 */
export const KART_VIEW = Object.freeze({ scale: RACER_FRAMING.scale, modelRadiusM: 0.75, modelUpM: RACER_FRAMING.centerUpM, keepSprites: true });

/** Collision radius that matches a visual radius change: the old radius times the scale, rounded to cm. */
export const scaledRadius = (radiusM, scale = KART_VIEW.scale) => Math.round(radiusM * scale * 100) / 100;

/**
 * Where the chase camera puts the kart on a flat straight (fractions of the viewport, 0 = top or left edge).
 * cam: the feel.json camera block (eyeUpM, eyeBackM, lookAheadM, lookUpM). Returns { horizonY, kartY (visual
 * centre), kartBottomY, kartW (saucer width share of the viewport width), vFov }.
 */
export function chaseFraming(cam, baseVDeg, aspect, { scale = KART_VIEW.scale, radiusM = KART_VIEW.modelRadiusM, upM = KART_VIEW.modelUpM } = {}) {
  const vFov = vFovForAspect(baseVDeg, aspect), th = Math.tan(vFov * Math.PI / 360), tw = th * aspect;
  // Aim above the visual centre by its screen offset, matching the moving chase rig's composition.
  const pitch = Math.atan2(cam.eyeUpM - upM * scale, cam.eyeBackM)
    - Math.atan((RACER_FRAMING.screenY * 2 - 1) * th) - CHASE_NEUTRAL_PITCH_DEG * Math.PI / 180;
  const proj = (y, z) => {
    // a point at height y, z metres ahead of the kart (negative = behind)
    const dy = y - cam.eyeUpM, dz = z + cam.eyeBackM;
    const depth = dz * Math.cos(pitch) - dy * Math.sin(pitch), up = dy * Math.cos(pitch) + dz * Math.sin(pitch);
    return { depth, y: 0.5 - up / depth / th / 2 };
  };
  const horizonY = 0.5 - Math.tan(pitch) / th / 2;
  const c = proj(upM * scale, 0), bottom = proj(0, -radiusM * scale);
  return { horizonY, kartY: c.y, kartBottomY: bottom.y, kartW: (2 * radiusM * scale) / c.depth / tw / 2, vFov };
}

/**
 * Near cue look numbers (metres). phoneSpacingM thins them on the phone tier. outM keeps the pole clear of the
 * chase eye (a drift bias swings the eye up to about 0.9 m past the kart's line); footM sinks the pole below
 * the road plane so it reads as fixed to the deck's side; the banner's lower edge stays well above the eye.
 */
export const NEAR_LOOK = Object.freeze({
  spacingM: 13, phoneSpacingM: 26, outM: 1.0, staggerM: 6.5, footM: 1.6,
  poleM: 4, poleR: 0.075, poleSides: 6, armM: 1.1, armR: 0.05,
  lampR: 0.24, bannerEvery: 2, bannerW: 0.62, bannerH: 1.1, bannerDropM: 0.25, bannerInM: 0.6,
  poleHex: '#2b1838', lampHex: '#fff3dc',
  minUpY: 0.8,             // the road normal must stand this upright (no posts on the loop, steep banks or walls)
  loopPadM: 6,             // polish 6: and none this far (plus colossiMath LOOP_PAD_M) round a loop: the wheel stands over its straights
  skipFlags: Object.freeze(['magnet', 'jump', 'open', 'pit']),
});

const railed = (flags, side) => !!flags && (flags.includes('rail') || flags.includes(side > 0 ? 'railR' : 'railL'));

/**
 * The near cue plan over a track (track.frameAt, track.lengthM, track.sectionAt, track.sections, track.routes).
 * A post stands on a side only where that side carries a rail, never on a skip flag, a near-vertical road or
 * a route's open stretch, nor round a vertical loop (colossiMath onLoop). side +1 = the frame's +b side. Returns [{ s, side, p, x (unit, toward the road),
 * y (road up), z (x cross y), banner, hex }], p = the post foot.
 */
export function nearCuePlan(track, { phone = false, L = NEAR_LOOK } = {}) {
  const out = [], len = track.lengthM, step = phone ? L.phoneSpacingM : L.spacingM;
  const routes = track.routes || [];
  const inOpen = (s) => routes.some((r) => s >= r.openS0 - 2 && s <= r.openS1 + 2);
  for (const side of [1, -1]) {
    let n = 0;
    for (let s = (side > 0 ? 0 : L.staggerM) + step / 2; s < len; s += step) {
      const f = track.frameAt(s);
      if (!railed(f.flags, side) || (f.flags || []).some((x) => L.skipFlags.includes(x))) continue;
      if (!(f.n[1] >= L.minUpY) || inOpen(s) || onLoop(track, s, L.loopPadM)) continue;
      const half = f.w / 2 + L.outM;
      const p = [f.p[0] + f.b[0] * half * side, f.p[1] + f.b[1] * half * side, f.p[2] + f.b[2] * half * side];
      const x = [-f.b[0] * side, -f.b[1] * side, -f.b[2] * side], y = f.n.slice();
      const z = [x[1] * y[2] - x[2] * y[1], x[2] * y[0] - x[0] * y[2], x[0] * y[1] - x[1] * y[0]];
      const sec = track.sections && track.sectionAt ? track.sections[track.sectionAt(s)] : null;
      const base = SPECTRUM_BASE[sectionKey(sec ? sec.palette : 0)];
      const banner = n % L.bannerEvery === 0;
      out.push({ s, side, p, x, y, z, banner, hex: base[(n >> 1) % 2] });
      n++;
    }
  }
  return out;
}

/** Column-major 4x4 (three.js Matrix4.elements order) from a basis and a position. */
export function basisMatrix(out, x, y, z, p) {
  out[0] = x[0]; out[1] = x[1]; out[2] = x[2]; out[3] = 0;
  out[4] = y[0]; out[5] = y[1]; out[6] = y[2]; out[7] = 0;
  out[8] = z[0]; out[9] = z[1]; out[10] = z[2]; out[11] = 0;
  out[12] = p[0]; out[13] = p[1]; out[14] = p[2]; out[15] = 1;
  return out;
}
