/* sim/marqueeView.js - where a dropping MARQUEE screen hangs so the player's own chase view sees it whole.
 * Pure (no three.js, no DOM): the cue transport picks the fixed road spot with it once, render/marquee.js shares
 * the face size and rest height. Owner (2026-10-26): the screens falling on "keep watching" were not visible,
 * either slipped to the side or not fully in view.
 * The chase eye looks down at the racer, so a face kept above the seven-metre road clearance always sits in the
 * top band of the picture. The spot is far enough that the face (and its bounce) fits under the top edge at the
 * impact, the rig hangs on the player's own line (the kart still passes under it), leaning toward the view
 * centre on curves, and the drop starts just above the top edge so the whole fall reads in frame.
 * Nothing here moves the camera: it only predicts the steady chase pose (no shake, push or zoom).
 */
import { CHASE_NEUTRAL_PITCH_DEG } from '../core/chaseView.js';

export const CLEAR_M = 7.2, HANG_M = 7.8, DROP_S = 1.05;   // road clearance, hang height, the drop's fall time
export const MARQUEE_VIEW = Object.freeze({
  // feel.json camera / fov and render/framing.js numbers; boot passes the live ones (aspect included).
  eyeUpM: 2.2, eyeBackM: 3.9, eyeLiftM: .25, centreUpM: .493, screenY: .6, vFovDeg: 60, aspect: 16 / 9,
  neutralPitchDeg: CHASE_NEUTRAL_PITCH_DEG,
  topNdc: .95,    // the face top, bounce included, stays under this share of the upper half
  sideNdc: .86,   // and every face inside this share of each side
  aimNdc: .25,    // a face centre further off-centre than this costs score (curves)
  centreNdc: .35, // and past this it counts as slipped: a smaller face hung nearer the view centre wins
  bounceM: 1.2,   // most of the first bounce (render/marquee.js peaks near 1.7 m for a tenth of a second)
  stepM: 5, spanM: 170, maxM: 280,
  scales: Object.freeze([1, .8, .65]),   // smaller faces only where no full-size spot fits (climbs, tight turns)
  laneInM: 3,     // the kart's own line stays this far inside the face edge
  fallPadM: .6, minFallM: 6, maxFallM: 39,
  deckPadM: 3,    // another road within the face half-width plus this, overhead, is a deck over the fall column
  deckGapM: 3,    // and the screen (its fall start too) stays this far under that deck's surface
});

/** Face width and height for a road width and a slot (side -1/0/1); the size render/marquee.js draws. */
export function marqueeFace(frameW = 24, side = 0, scale = 1) {
  const w = Math.max(14, Math.min(32, (frameW || 24) * .9)) * (side ? .78 : 1) * (scale || 1);
  return { w, h: w * .5625 };
}
/** Rest centre height over the road for an unrolled face: its bottom never enters the road clearance. */
export const restY = h => Math.max(CLEAR_M + h * .54, HANG_M + h / 2);
/** Lateral centre of one face of a rig; flanks sit beside the road edges, and a shifted rig moves as one. */
export const faceX = (frameW, side, shiftM = 0, scale = 1) =>
  (shiftM || 0) + side * ((frameW || 24) / 2 + marqueeFace(frameW, side, scale).w / 2 + 2);

const dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const cross = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
const unit = a => { const l = Math.hypot(a[0], a[1], a[2]) || 1; return [a[0] / l, a[1] / l, a[2] / l]; };

/** The steady chase camera behind a kart road frame k ({p,t,n}): eye, forward f, right r, up u, tangents. */
export function chaseCamera(k, V = MARQUEE_VIEW) {
  const W = [0, 1, 0], th = Math.tan(V.vFovDeg * Math.PI / 360), eyeH = V.eyeUpM + V.eyeLiftM;
  const eye = [0, 1, 2].map(i => k.p[i] - k.t[i] * V.eyeBackM + W[i] * eyeH);
  const a = unit([0, 1, 2].map(i => k.p[i] + k.n[i] * V.centreUpM - eye[i]));
  // Like the rig's aim: the racer centre projects at screenY, the horizon stays level (world up).
  const r = unit(cross(a, Math.abs(a[1]) > .98 ? k.t : W)), ua = cross(r, a);
  const lift = Math.atan((2 * V.screenY - 1) * th) + V.neutralPitchDeg * Math.PI / 180;
  const c = Math.cos(lift), s = Math.sin(lift);
  const f = [0, 1, 2].map(i => a[i] * c + ua[i] * s), u = [0, 1, 2].map(i => ua[i] * c - a[i] * s);
  return { eye, f, r, u, th, tw: th * V.aspect };
}
/** Normalised device position of a world point: x right, y up, both -1..1 inside the view. */
export function viewNdc(cam, P) {
  const v = [P[0] - cam.eye[0], P[1] - cam.eye[1], P[2] - cam.eye[2]], depth = dot(v, cam.f);
  return { x: dot(v, cam.r) / (Math.max(depth, 1e-3) * cam.tw), y: dot(v, cam.u) / (Math.max(depth, 1e-3) * cam.th), depth };
}
const at = (f, x, y) => [0, 1, 2].map(i => f.p[i] + f.b[i] * x + f.n[i] * y);

/** Every face corner of a rig hung at spot frame f (rest pose; tops include the first bounce). */
export function rigBox(cam, f, sides, shiftM, liftM, V = MARQUEE_VIEW, scale = 1) {
  let top = -Infinity, bottom = Infinity, left = Infinity, right = -Infinity, near = Infinity, height = 0;
  for (const side of sides) {
    const { w, h } = marqueeFace(f.w, side, scale), x = faceX(f.w, side, shiftM, scale), y = liftM + restY(h);
    const lo = [x - w / 2, x + w / 2].map(cx => viewNdc(cam, at(f, cx, y - h / 2)));
    const hi = [x - w / 2, x + w / 2].map(cx => viewNdc(cam, at(f, cx, y + h / 2 + V.bounceM)));
    for (const q of [...lo, ...hi]) {
      top = Math.max(top, q.y); bottom = Math.min(bottom, q.y); left = Math.min(left, q.x); right = Math.max(right, q.x); near = Math.min(near, q.depth);
    }
    height = Math.max(height, Math.min(hi[0].y, hi[1].y) - Math.max(lo[0].y, lo[1].y));
  }
  return { top, bottom, left, right, near, main: { height, x: (left + right) / 2 } };
}

/**
 * Pick the hang of one dropping rig at its impact.
 * spot(d): the road frame d metres past the kart's impact position ({p,t,n,b,w}, or null when there is none);
 * kart: the kart's road frame at the impact; lane: its lateral offset (+ = frame.b); sides: the rig's faces;
 * minM: the approach floor (owner: at least three seconds and 60 metres); lift(d, x): road surface rise at x;
 * deck(d, x, radius): height over the spot's road of the lowest OTHER road passing within radius of the column at
 * x (Infinity when none; sim/marqueeDecks.js). Without decks the pick is the plain view pick.
 * Returns { aheadM, shiftM, liftM, scale, fallM, fits, clear, roomM } or null when no spot exists.
 */
export function fitMarquee({ spot, kart, lane = 0, sides = [0], minM = 60, lift = () => 0, deck = null, view = null }) {
  const V = view ? { ...MARQUEE_VIEW, ...view } : MARQUEE_VIEW, cam = chaseCamera(kart, V);
  const end = Math.max(minM, Math.min(V.maxM, minM + V.spanM));
  // The drop starts with every face bottom at the impact view's top edge, so the whole fall reads in frame
  // (render/marquee.js fades the screen in as the drop begins, never earlier).
  const fallOf = c => {
    let fallM = V.minFallM;
    for (const side of sides) {
      const { h } = marqueeFace(c.f.w, side, c.scale), x = faceX(c.f.w, side, c.shiftM, c.scale);
      const bottom = c.liftM + restY(h) - h / 2, base = at(c.f, x, 0);
      const v0 = [0, 1, 2].map(i => base[i] - cam.eye[i]), n = c.f.n;
      const den = dot(n, cam.u) - cam.th * dot(n, cam.f);
      if (Math.abs(den) > 1e-6) fallM = Math.max(fallM, (cam.th * dot(v0, cam.f) - dot(v0, cam.u)) / den - bottom + V.fallPadM);
    }
    return Math.max(V.minFallM, Math.min(V.maxFallM, fallM));
  };
  const groups = [], group = k => groups[k] ??= sweep(V.scales[k]);
  function sweep(scale) {
    const list = [];
    for (let d = minM; d <= end; d += V.stepM) {
      const f = spot(d);
      if (!f || !(f.w > 0)) continue;
      const { w } = marqueeFace(f.w, sides.includes(0) ? 0 : sides[0], scale);
      const road = Math.max(0, f.w / 2 - 1), line = Math.max(0, w / 2 - V.laneInM);
      const clampShift = x => Math.max(-road, Math.min(road, Math.max(lane - line, Math.min(lane + line, x))));
      // The face centre moves almost linearly across the view with its shift: lean the rig toward the centre.
      const x0 = rigBox(cam, f, [0], lane, 0, V, scale).main.x, x1 = rigBox(cam, f, [0], lane + 1, 0, V, scale).main.x;
      const shiftM = clampShift(Math.abs(x1 - x0) > 1e-6 ? lane - x0 / (x1 - x0) : lane);
      let liftM = 0;
      for (const side of sides) {
        const fx = faceX(f.w, side, shiftM, scale), half = marqueeFace(f.w, side, scale).w / 2;
        for (const x of [fx - half, fx, fx + half]) liftM = Math.max(liftM, lift(d, x) || 0);
      }
      const box = rigBox(cam, f, sides, shiftM, liftM, V, scale);
      const over = Math.max(0, box.top - V.topNdc) + Math.max(0, -box.left - V.sideNdc) + Math.max(0, box.right - V.sideNdc)
        + Math.max(0, Math.abs(box.main.x) - V.centreNdc) * .5 + (box.near > 1 ? 0 : 10);
      const score = over > 0 ? -over : box.main.height * Math.max(.05, 1 - 1.6 * Math.max(0, Math.abs(box.main.x) - V.aimNdc));
      const c = { aheadM: d, shiftM, liftM, scale, over, score, f: { p: f.p.slice(), t: f.t.slice(), n: f.n.slice(), b: f.b.slice(), w: f.w } };
      c.fallM = fallOf(c); c.roomM = Infinity; c.tier = 2;
      if (deck) {
        // Room between each hanging face top and the lowest other road over its column (GOONWORKS runs 660 m
        // under its own upper deck): 2 = the whole fall clears it, 1 = the hanging screen at least fits under it.
        for (const side of sides) {
          const { w: fw, h } = marqueeFace(f.w, side, scale), fx = faceX(f.w, side, shiftM, scale);
          c.roomM = Math.min(c.roomM, deck(d, fx, fw / 2 + V.deckPadM) - (liftM + restY(h) + h / 2));
        }
        c.tier = c.roomM >= c.fallM + V.deckGapM ? 2 : c.roomM >= V.bounceM + V.deckGapM ? 1 : 0;
      }
      list.push(c);
    }
    return list;
  }
  const better = (c, b) => !b || (c.over > 0) < (b.over > 0) || ((c.over > 0) === (b.over > 0) && c.score > b.score);
  // The plain view pick: a full-size fit always wins; smaller faces are the fallback.
  const choose = ok => {
    let best = null;
    for (let k = 0; k < V.scales.length; k++) {
      for (const c of group(k)) if (ok(c) && better(c, best)) best = c;
      if (best && best.over <= 0) break;
    }
    return best;
  };
  const plain = choose(() => true);
  if (!plain) return null;
  let best = plain;
  if (plain.tier < 2) {
    // A deck over the fall column: the nearest clear spot that still fits the view, later first, then earlier
    // (same face size first); else the best spot the hanging screen fits under, its fall capped under the deck.
    const k0 = V.scales.indexOf(plain.scale), order = [k0, ...V.scales.keys()].filter((k, i, a) => a.indexOf(k) === i);
    let moved = null;
    for (const k of order) {
      const ok = group(k).filter(c => c.tier === 2 && c.over <= 0);
      moved = ok.find(c => c.aheadM > plain.aheadM) || ok.filter(c => c.aheadM < plain.aheadM).pop() || null;
      if (moved) break;
    }
    best = moved || choose(c => c.tier === 1) || choose(c => c.tier === 2) || plain;
  }
  const capped = best.tier < 2 ? Math.max(0, Math.min(best.fallM, best.roomM - V.deckGapM)) : best.fallM;
  return { aheadM: best.aheadM, shiftM: best.shiftM, liftM: best.liftM, scale: best.scale, fits: best.over <= 0,
    fallM: capped, clear: best.tier === 2, roomM: best.roomM };
}
