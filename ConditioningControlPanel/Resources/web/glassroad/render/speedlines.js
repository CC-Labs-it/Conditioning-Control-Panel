/* render/speedlines.js - the speed field: streaks, glitter and soft light orbs with depth (polish 4).
 *
 * Polish 3 drew lines on a cylinder round the kart, and the owner read them as straight lines too close to
 * the eye. Polish 4 lives in CAMERA space (cameraWorldMatrix in the shader, so it follows whatever camera
 * renders it with no CPU copy): each element is born P4_LOOK.fieldNearM..fieldFarM ahead at a screen spot
 * whose |x| is never inside the centre third (atlas.js fieldBirth), then streams toward the camera with the
 * kart's real speed, so it only ever moves OUTWARD on screen and leaves past the edge before it nears the
 * lens; a near fade covers the rest. Three kinds in one draw call: tapered streaks (bright head, thin tail,
 * bent by the track's curve), small twinkling glitter, and big faint orbs. Different birth depths give the
 * parallax. Density and streak length grow with speed and the eased boost (a drift tier boost by its tier;
 * polish 5: with an fx state the boost size is its speedLines, which climbs the boost ladder of fx/cues.js, mini
 * to tier 3; without one, atlas.js boostTarget by kind);
 * the boost tints the field in its colour (drift tiers blue, orange, pink). Each element has a seed and shows
 * when its seed is under the density, so density changes the count and never flickers one.
 * Photosafe: thin, small-area elements at the screen sides, the boost eases in over boostRiseS (no step).
 * Gentle halves the alpha, the flow and the curve. Pause: dt 0 freezes the flow and the twinkle.
 * The x clamp mirrors atlas.js fieldPoint (tests/render-particles4.test.mjs checks the centre third rule).
 * Feel wave 2026-10-26 (lane C, render/worldFeel.js): speed sense without the camera. Near top speed the
 * streaks grow longer and the field thicker (topSpeedShare); the final-lap wake (setWake 0..1) lowers the
 * speed threshold. Both scale with the motion level (setMotion) and are gone under 'off'.
 */
import { Mesh, BufferGeometry, BufferAttribute, MeshBasicNodeMaterial, AdditiveBlending, DoubleSide, Color } from 'three/webgpu';
import {
  attribute, uniform, fract, sin, vec2, vec3, vec4, step, float, abs, mix, select, smoothstep, max, clamp, normalize, length,
  cameraWorldMatrix, cameraProjectionMatrix,
} from 'three/tsl';
import { S, BOOST } from '../sim/state.js';
import { LOOK } from './look.js';
import { Q } from './quality.js';
import { juiceHues, p4Look, fieldBirth, fieldDensity, boostTarget, tierColour } from './atlas.js';
import { finalLapMods, topSpeedShare, feelVal, WORLD_FEEL, motionMul } from './worldFeel.js';

const KIND = { streak: 0, glitter: 1, orb: 2 };

/** Fade the camera field out around a busy enclosed section, then restore it outside. */
export function speedFieldVisibility(s, quietRange = null) {
  if (!quietRange || !Number.isFinite(s)) return 1;
  const distance = Math.max(quietRange.s0 - s, s - quietRange.s1);
  const t = Math.max(0, Math.min(1, distance / 20));
  return t * t * (3 - 2 * t);
}

export function createSpeedLines(feel = null) {
  const P = p4Look(LOOK), HUES = juiceHues();
  const n = Math.max(3, Math.round(Q.speedLines * P.fieldCountMul));
  const A = new Float32Array(n * 16), B = new Float32Array(n * 16), corner = new Float32Array(n * 8), tint = new Float32Array(n * 12), idx = new Uint32Array(n * 6);
  const cc = new Color(), sh = P.fieldShare, sum = sh.streak + sh.glitter + sh.orb;
  for (let i = 0; i < n; i++) {
    const f = (i + 0.5) / n * sum, kind = f < sh.streak ? KIND.streak : f < sh.streak + sh.glitter ? KIND.glitter : KIND.orb;
    const e = fieldBirth(Math.random(), Math.random(), Math.random(), i & 1 ? 1 : -1, P);
    const a = [e.nx, e.ny, e.depth, Math.random()], b = [kind, Math.random(), Math.random(), Math.random()];
    cc.set(HUES[(i * 7 + ((Math.random() * 3) | 0)) % HUES.length]);
    for (let k = 0; k < 4; k++) {
      const v = i * 4 + k;
      A.set(a, v * 4); B.set(b, v * 4);
      corner.set([k & 1 ? 1 : -1, k >> 1], v * 2);
      tint.set([cc.r, cc.g, cc.b], v * 3);
    }
    idx.set([0, 1, 2, 2, 1, 3].map((v) => v + i * 4), i * 6);
  }
  const g = new BufferGeometry();
  g.setAttribute('position', new BufferAttribute(new Float32Array(n * 12), 3));
  g.setAttribute('aField', new BufferAttribute(A, 4));
  g.setAttribute('aKind', new BufferAttribute(B, 4));
  g.setAttribute('aCorner', new BufferAttribute(corner, 2));
  g.setAttribute('aTint', new BufferAttribute(tint, 3));
  g.setIndex(new BufferAttribute(idx, 1));

  const u = {
    dist: uniform(0), density: uniform(0), boost: uniform(0), curve: uniform(0), alpha: uniform(P.fieldAlpha),
    lenScale: uniform(1), time: uniform(0), boostCol: uniform(new Color('#ffffff')),
  };
  const a = attribute('aField', 'vec4'), b = attribute('aKind', 'vec4'), c = attribute('aCorner', 'vec2'), tn = attribute('aTint', 'vec3');
  const isStreak = b.x.lessThan(0.5), isOrb = b.x.greaterThan(1.5);
  const travel = a.z.sub(P.fieldEndM);
  const t = fract(a.w.add(u.dist.div(travel)));
  const d = a.z.sub(t.mul(travel));                                   // head depth, metres ahead of the camera
  const len = mix(P.streakLenM[0], P.streakLenM[1], b.y).mul(u.lenScale);
  const dd = d.add(select(isStreak, c.y.mul(len), float(0)));         // the tail runs back toward the vanishing point
  const tanH = float(1).div(cameraProjectionMatrix.element(0).x), tanV = float(1).div(cameraProjectionMatrix.element(1).y);
  const sg = select(a.x.lessThan(0), float(-1), float(1));
  const x0 = a.x.mul(a.z).mul(tanH), ys = a.y.mul(a.z).mul(tanV);
  const lim = dd.mul(tanH).mul(P.curveMaxNdc);
  const bend = clamp(dd.mul(dd).mul(0.5 * P.curveGain).mul(u.curve), lim.negate(), lim);
  const xs = sg.mul(max(x0.add(bend).mul(sg), dd.mul(tanH).mul(P.fieldCentre)));   // never inside the centre third
  const rad = normalize(vec2(x0, ys)), tang = vec2(rad.y.negate(), rad.x);
  const wS = mix(P.streakWidthM[0], P.streakWidthM[1], b.z).mul(c.y.mul(P.streakTaper).oneMinus()).mul(u.boost.mul(0.4).add(1));
  const size = select(isOrb, mix(P.orbSizeM[0], P.orbSizeM[1], b.z), mix(P.glitterSizeM[0], P.glitterSizeM[1], b.z));
  const streakP = vec3(xs.add(tang.x.mul(c.x).mul(wS)), ys.add(tang.y.mul(c.x).mul(wS)), dd.negate());
  const billP = vec3(xs.add(c.x.mul(size)), ys.add(c.y.mul(2).sub(1).mul(size)), d.negate());
  const m = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, blending: AdditiveBlending, side: DoubleSide });
  m.positionNode = cameraWorldMatrix.mul(vec4(select(isStreak, streakP, billP), 1)).xyz;

  // shapes: a streak is bright at its head and soft at its edges; glitter a twinkling cross; an orb a faint soft disc
  const q = vec2(c.x, c.y.mul(2).sub(1)), r = length(q);
  const streakA = smoothstep(0, 0.12, c.y).mul(c.y.oneMinus().pow(1.6)).mul(abs(c.x).oneMinus()).mul(1.6);
  const arms = smoothstep(0, 0.18, abs(q.x)).oneMinus().max(smoothstep(0, 0.18, abs(q.y)).oneMinus()).mul(r.oneMinus().max(0));
  const twinkle = sin(u.time.mul(Math.PI * 2 * P.glitterTwinkleHz).add(a.w.mul(40))).mul(0.3).add(0.7);
  const glitA = smoothstep(0, 0.55, r).oneMinus().max(arms.mul(0.8)).mul(twinkle);
  const orbA = smoothstep(0, 1, r).oneMinus().pow(2).mul(P.orbAlpha);
  const shape = select(isStreak, streakA, select(isOrb, orbA, glitA));
  const show = step(fract(a.w.mul(7.31).add(b.x.mul(0.37))), u.density);
  const fade = smoothstep(0, 0.12, t).mul(smoothstep(P.fieldEndM, P.fieldEndM * 2.2, d));
  m.opacityNode = show.mul(fade).mul(shape).mul(u.alpha);
  const core = select(isStreak, abs(c.x).mul(0.5).add(0.5), select(isOrb, float(1), float(0.35))).mul(P.fieldTint);
  m.colorNode = mix(mix(vec3(1, 1, 1), tn, core), vec3(u.boostCol), u.boost.mul(P.fieldBoostTint)).mul(u.boost.mul(0.35).add(1));
  const mesh = new Mesh(g, m);
  mesh.frustumCulled = false; mesh.renderOrder = 3; mesh.castShadow = false; mesh.receiveShadow = false;

  let boost = 0, curve = 0, wake = 0, motion = 'full';
  const top = () => (feel && feel.get ? feel.get('speed.topMps') : 0) || 39;
  /** Per frame (signature kept from polish 3; obj unused, the field follows the rendering camera). */
  function update(dt, kart, fx, obj, visibility = 1) {
    const gentle = !!(feel && feel.get && feel.get('gentle.on')), pct = Math.max(0, kart[S.speedPct] || 0), mps = pct * top();
    const bk = kart[S.boostKind] | 0, rescue = kart[S.respawnPhase] > 0;
    const want = rescue ? 0 : fx ? Math.min(1, Math.max(0, fx.speedLines || 0)) : boostTarget(bk, BOOST);
    if (dt > 0) boost += (want - boost) * (1 - Math.exp(-dt / (want > boost ? P.boostRiseS : P.boostFallS)));
    if (bk) u.boostCol.value.set(bk >= BOOST.DRIFT1 && bk <= BOOST.DRIFT3 ? tierColour(bk - BOOST.DRIFT1 + 1, P) : bk === BOOST.LAUNCH ? '#ffffff' : LOOK.padTrail);
    const flow = gentle ? ((feel && feel.get('gentle.scrollMul')) || 0.5) : 1;
    u.dist.value = (u.dist.value + mps * dt * flow) % 20000;
    u.time.value = (u.time.value + dt) % 1000;
    const k = Math.max(-0.05, Math.min(0.05, (kart[S.yawRate] || 0) / Math.max(mps, 8)));
    if (dt > 0) curve += (k * (gentle ? 0.5 : 1) - curve) * (1 - Math.exp(-dt / P.curveTcS));
    u.curve.value = curve;
    u.boost.value = boost;
    const topK = topSpeedShare(pct, feel, motion) * (gentle ? 0.5 : 1), shift = finalLapMods(wake * motionMul(motion), feel).fieldShift;
    const S2 = WORLD_FEEL.speedSense;
    u.lenScale.value = (0.35 + pct * P.streakSpeedLen) * (1 + boost * P.streakBoostLen) * (1 + topK * feelVal(feel, 'speedSense.topLenMul', S2.topLenMul));
    const dens = Math.min(1, fieldDensity(pct + shift, boost, P) + topK * feelVal(feel, 'speedSense.topDensity', S2.topDensity));
    u.density.value = rescue ? 0 : dens * Math.min(1, Q.speedLines * P.fieldCountMul / n);
    u.alpha.value = P.fieldAlpha * (gentle ? P.fieldGentle : 1) * visibility;
    mesh.visible = u.density.value > 0.002 && visibility > .002;
  }
  return { object: mesh, update, setWake(v) { wake = Math.max(0, Math.min(1, v || 0)); }, setMotion(m) { motion = m || 'full'; }, dispose() { g.dispose(); m.dispose(); } };
}
