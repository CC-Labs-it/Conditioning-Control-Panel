/* render/viewMath.js - the VIEW lane numbers (polish 6): kart visual scale, the chase framing check and the
 * near roadside cue plan. Pure (no three.js, no DOM), so node tests read it.
 *
 * Owner (2026-10-23, with feel references): the chase camera sits closer and lower and the kart is smaller
 * against the world, so the world feels bigger and the player more in control. The camera numbers live in
 * feel.json "camera"; the kart's world size lives here (KART_VIEW.scale, applied on every kart root by
 * render/karts.js, so the pose, sparks, wakes, skids, ring and aura all follow it). The sim's collision sizes
 * follow the same factor (feel.json contact.radiusM, sim/gaps.js kart.radiusM).
 *
 * The near cues (render/nearDressing.js): lamp posts just outside a railed edge, a banner on every other
 * post, streaming past the kart at a steady rhythm. They give the eye speed and a human scale next to the
 * road. Static scenery: nothing flashes or pulses, fog fades them with distance.
 *
 * Owner follow-up (2026-10-23, preview gaps): EMI read too small in the polish 6 lap. Racers 0.68 -> 0.8 and the
 * chase eye about 12% closer (1.5 up / 4.1 back -> 1.32 / 3.6, look 0.3 -> 0.05 up so the racer sits a little
 * below the middle, about 60% down). This supersedes the polish 6 preference for smaller racers.
 * Collision follows again: contact.radiusM 0.6, contact.maxHeightM 1.2, sim/gaps.js kart.radiusM 0.58.
 */
import { vFovForAspect } from './cameraMath.js';
import { SPECTRUM_BASE, sectionKey } from './spectrum.js';
import { onLoop } from './colossiMath.js';

/**
 * Kart look scale. modelRadiusM / modelUpM are the teacup GLB's saucer radius and its visual centre height at
 * scale 1 (art/models/emi-teacup-kart.glb bounds: saucer +-0.75 m, cup top 1.1 m, EMI above it).
 * keepSprites: the name tag and head mark keep their world size (they are read at range), only their height
 * follows the kart.
 */
export const KART_VIEW = Object.freeze({ scale: 0.8, modelRadiusM: 0.75, modelUpM: 0.62, keepSprites: true });

/** Collision radius that matches a visual radius change: the old radius times the scale, rounded to cm. */
export const scaledRadius = (radiusM, scale = KART_VIEW.scale) => Math.round(radiusM * scale * 100) / 100;

/**
 * Where the chase camera puts the kart on a flat straight (fractions of the viewport, 0 = top or left edge).
 * cam: the feel.json camera block (eyeUpM, eyeBackM, lookAheadM, lookUpM). Returns { horizonY, kartY (visual
 * centre), kartBottomY, kartW (saucer width share of the viewport width), vFov }.
 */
export function chaseFraming(cam, baseVDeg, aspect, { scale = KART_VIEW.scale, radiusM = KART_VIEW.modelRadiusM, upM = KART_VIEW.modelUpM } = {}) {
  const vFov = vFovForAspect(baseVDeg, aspect), th = Math.tan(vFov * Math.PI / 360), tw = th * aspect;
  // camera space: eye at (0, eyeUp, back) looking at (0, lookUp, -ahead); pitch about the X axis
  const pitch = Math.atan2(cam.eyeUpM - cam.lookUpM, cam.eyeBackM + cam.lookAheadM);
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
  poleM: 3.8, poleR: 0.075, poleSides: 6, armM: 1.1, armR: 0.05,
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
