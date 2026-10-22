/* render/scene.js - buildWorld (CONTRACT.md section 6): road, rails, pads and jump lips from the
 * track MeshData, the spiral sky, the teacup kart, floating props for speed reference, particles and
 * speed lines. update(view) moves everything from the interpolated kart and FxState.
 * opts.art (render/art.js) dresses the world in the wave 1 art: textured road, rails, pads and sky,
 * the EMI kart, countdown rings, the finish arch and a seeded scatter (render/dressing.js). Without
 * it, or for any asset that failed, the greybox stand-in stays.
 */
import {
  Scene, Group, Mesh, InstancedMesh, Object3D, BufferGeometry, BufferAttribute, Float32BufferAttribute, Color, Vector2,
  SphereGeometry, LatheGeometry, TorusGeometry, CylinderGeometry, RingGeometry, IcosahedronGeometry,
  HemisphereLight, DirectionalLight,
} from 'three/webgpu';
import { S } from '../sim/state.js';
import { clamp, lerp, smoothstep, qrotate, v3cross, v3norm } from '../core/math.js';
import { LOOK, SKIES, FALLBACK_SKY } from './look.js';
import { createPalette } from './materials/palette.js';
import { createRoadMaterial } from './materials/road.js';
import { createRailMaterial } from './materials/rails.js';
import { createPadMaterial } from './materials/pads.js';
import { createSkyMaterial, SKY_PALETTES } from './materials/sky.js';
import { buildKart, buildCountdown, buildFinish, buildScatter } from './dressing.js';
import { createKartMaterial, createPropMaterial } from './materials/kart.js';
import { createGlowMaterial } from './materials/glow.js';
import { createParticles } from './particles.js';
import { createSpeedLines } from './speedlines.js';

/** Track MeshData -> Mesh, or null when the data is empty. */
export function meshFrom(d, material) {
  if (!d || !d.indices || !d.indices.length) return null;
  const g = new BufferGeometry();
  g.setAttribute('position', new BufferAttribute(d.positions, 3));
  g.setAttribute('normal', new BufferAttribute(d.normals, 3));
  g.setAttribute('uv', new BufferAttribute(d.uvs, 2));
  g.setIndex(new BufferAttribute(d.indices, 1));
  g.computeBoundingSphere();
  return new Mesh(g, material);
}

/** Merge [geometry, hex] pairs into one non-indexed geometry with a `color` attribute. */
function merge(parts) {
  const pos = [], nor = [], col = [], c = new Color();
  for (const [geo, hex] of parts) {
    const g = geo.index ? geo.toNonIndexed() : geo;
    c.set(hex);
    pos.push(...g.attributes.position.array); nor.push(...g.attributes.normal.array);
    for (let i = 0; i < g.attributes.position.count; i++) col.push(c.r, c.g, c.b);
  }
  const out = new BufferGeometry();
  out.setAttribute('position', new Float32BufferAttribute(pos, 3));
  out.setAttribute('normal', new Float32BufferAttribute(nor, 3));
  out.setAttribute('color', new Float32BufferAttribute(col, 3));
  return out;
}

/** Greybox teacup on a saucer with an EMI placeholder bust. Origin = saucer base centre, forward -Z. */
function kartGeometry() {
  const R = LOOK.saucerR, v = (x, y) => new Vector2(x * R, y * R);
  const saucer = new LatheGeometry([v(0, 0), v(0.45, 0), v(1, 0.1), v(0.97, 0.16), v(0.5, 0.12), v(0, 0.12)], 32);
  const cup = new LatheGeometry([v(0, 0.12), v(0.42, 0.12), v(0.7, 0.55), v(0.78, 1.1), v(0.72, 1.1), v(0.64, 0.6), v(0, 0.3)], 32);
  const handle = new TorusGeometry(0.22 * R, 0.06 * R, 8, 16, Math.PI).rotateY(Math.PI / 2).translate(0, 0.7 * R, 0.78 * R);
  const torso = new CylinderGeometry(0.16 * R, 0.3 * R, 0.6 * R, 14).translate(0, 1.1 * R, 0);
  const head = new SphereGeometry(0.28 * R, 18, 12).translate(0, 1.62 * R, 0);
  const eye = (x) => new SphereGeometry(0.05 * R, 8, 6).translate(x * R, 1.68 * R, -0.25 * R);
  return merge([[saucer, LOOK.saucerColour], [cup, LOOK.cupColour], [handle, LOOK.cupColour],
    [torso, LOOK.emiColour], [head, LOOK.emiColour], [eye(-0.1), LOOK.faceColour], [eye(0.1), LOOK.faceColour]]);
}

function props(track) {
  const geo = new IcosahedronGeometry(1, 0), { material } = createPropMaterial(LOOK.propColour);
  const n = LOOK.props, im = new InstancedMesh(geo, material, n), o = new Object3D();
  let seed = 7;
  const rnd = (a) => { seed = (seed * 16807) % 2147483647; return a[0] + (seed / 2147483647) * (a[1] - a[0]); };
  for (let i = 0; i < n; i++) {
    const f = track.frameAt((i + 0.5) / n * track.lengthM), side = i & 1 ? 1 : -1, off = f.w / 2 + rnd(LOOK.propSideM);
    const up = rnd(LOOK.propUpM);
    o.position.set(f.p[0] + f.b[0] * side * off + f.n[0] * up, f.p[1] + f.b[1] * side * off + f.n[1] * up, f.p[2] + f.b[2] * side * off + f.n[2] * up);
    o.rotation.set(rnd([0, 6.28]), rnd([0, 6.28]), 0);
    o.scale.setScalar(rnd(LOOK.propSizeM));
    o.updateMatrix(); im.setMatrixAt(i, o.matrix);
  }
  return im;
}

export function buildWorld(track, feel, { art = null, bus = null, seed = 1 } = {}) {
  const scene = new Scene();
  const pal = createPalette();
  const owned = [], offs = [];
  const add = (m) => { if (m) { scene.add(m); owned.push(m); } return m; };

  if (art) {
    // the glTF props and kart are lit (standard / physical materials); everything else is unlit
    const L = LOOK.artLight;
    scene.add(new HemisphereLight(L.sky, L.ground, L.hemi));
    const sun = new DirectionalLight(L.sky, L.sun);
    sun.position.set(LOOK.lightDir[0], LOOK.lightDir[1], LOOK.lightDir[2]);
    scene.add(sun);
  }
  const sky = createSkyMaterial(pal, art);
  const skyMesh = add(new Mesh(new SphereGeometry(LOOK.skyRadiusM, 48, 32), sky.material));
  skyMesh.renderOrder = -1; skyMesh.frustumCulled = false;
  const road = createRoadMaterial(pal, art);
  add(meshFrom(track.meshes.road, road.material));
  add(meshFrom(track.meshes.rails, createRailMaterial(pal, art).material));
  const pad = createPadMaterial(LOOK.padColour, art && art.tex('boost-chevron-mask')), lip = createPadMaterial(LOOK.lipColour);
  add(meshFrom(track.meshes.pads, pad.material));
  add(meshFrom(track.meshes.jumps, lip.material));
  const scatter = buildScatter(art, track, seed);
  add(scatter ? scatter.object : props(track));
  const countdown = buildCountdown(art, track, pal), finish = buildFinish(art, track, pal);
  if (countdown) {
    add(countdown.object);
    if (bus) {
      offs.push(bus.on('countdown', ({ n = 3 } = {}) => countdown.pulse(n, feel.get('juice.countdownPulses'))));
      offs.push(bus.on('go', () => countdown.go()));
    }
  }
  if (finish) add(finish.object);

  const kart = add(new Group());
  const body = buildKart(art) || new Mesh(kartGeometry(), createKartMaterial().material);
  kart.add(body);
  const ring = createGlowMaterial(LOOK.ringColour);
  const ringMesh = new Mesh(new RingGeometry(LOOK.saucerR * 0.9, LOOK.saucerR * 1.1, 48).rotateX(-Math.PI / 2), ring.material);
  ringMesh.position.y = 0.03; kart.add(ringMesh);
  const tendril = createGlowMaterial(LOOK.tendrilColour);
  const tendrilMesh = add(new Mesh(new CylinderGeometry(0.15, 0.6, 1, 12, 1, true).translate(0, 0.5, 0), tendril.material));
  const particles = createParticles(feel), lines = createSpeedLines();
  add(particles.object); add(lines.object);

  let lastT = null, padPhase = 0, eyeSet = false;
  const fwd = [0, 0, -1], head = [0, 0, -1], eye = [0, -1, 0], eR = [1, 0, 0], eU = [0, 0, -1], q = [0, 0, 0, 1], F = [0, 0, -1], UP = [0, 1, 0];
  /** The spiral eye: tilted from the nadir toward a slow (skyEyeTcS) flat heading, below the horizon ahead. */
  function aimEye(k, dt) {
    q[0] = k[S.quatX]; q[1] = k[S.quatY]; q[2] = k[S.quatZ]; q[3] = k[S.quatW];
    qrotate(fwd, q, F); fwd[1] = 0;
    if (Math.hypot(fwd[0], fwd[2]) < 1e-3) return;
    v3norm(fwd, fwd);
    const a = eyeSet ? Math.min(1, dt / LOOK.skyEyeTcS) : 1;
    eyeSet = true;
    head[0] += (fwd[0] - head[0]) * a; head[2] += (fwd[2] - head[2]) * a; head[1] = 0; v3norm(head, head);
    const tilt = LOOK.skyEyeTiltDeg * Math.PI / 180;
    eye[0] = head[0] * Math.sin(tilt); eye[1] = -Math.cos(tilt); eye[2] = head[2] * Math.sin(tilt);
    v3norm(eR, v3cross(eR, eye, UP)); v3cross(eU, eR, eye);
    sky.uniforms.eye.value.set(eye[0], eye[1], eye[2]);
    sky.uniforms.eyeR.value.set(eR[0], eR[1], eR[2]);
    sky.uniforms.eyeU.value.set(eU[0], eU[1], eU[2]);
  }
  const secs = track.sections;
  const sec = (i) => secs[clamp(i | 0, 0, secs.length - 1)];

  function update(view) {
    const { kart: k, fx } = view;
    const dt = lastT === null ? 0 : clamp(view.timeS - lastT, 0, 0.1);
    lastT = view.timeS;
    const gentle = !!feel.get('gentle.on');
    const scroll = gentle ? feel.get('gentle.scrollMul') : 1, contrast = gentle ? feel.get('gentle.contrastMul') : 1;

    kart.position.set(k[S.posX], k[S.posY], k[S.posZ]);
    kart.quaternion.set(k[S.quatX], k[S.quatY], k[S.quatZ], k[S.quatW]);
    const sq = fx.squash || 0;
    body.scale.set(1 + sq / 2, 1 - sq, 1 + sq / 2);

    const pm = fx.paletteMix || { from: view.sectionIdx, to: view.sectionIdx, t: 1 };
    // the from side is a weighted blend when a gate came mid-fade (fx/reactions.js shownWeights)
    const fromW = pm.base ? Object.entries(pm.base).map(([k, w]) => [sec(+k), w]) : [[sec(pm.from), 1]];
    const b = sec(pm.to), t = clamp(pm.t, 0, 1);
    const mixOf = (get) => lerp(fromW.reduce((acc, [x, w]) => acc + get(x) * w, 0), get(b), t);
    pal.set(fromW.map(([x, w]) => [x.palette, w]), b.palette, t);
    const gw = [0, 0, 0], gi = (id) => Math.max(0, SKY_PALETTES.indexOf(id));
    for (const [x, w] of fromW) gw[gi(x.palette)] += w * (1 - t);
    gw[gi(b.palette)] += t;
    sky.uniforms.galaxy.value.set(gw[0], gw[1], gw[2]);
    const band = mixOf((x) => x.bandSpeed) * (fx.bandSpeedMul || 1) * scroll;
    road.uniforms.phase.value = (road.uniforms.phase.value + dt * LOOK.bandScrollMps * band) % LOOK.bandLenM;
    road.uniforms.contrast.value = contrast;
    // beat breath: a soft swell with no step at the beat (rises over breathRise of the beat, falls to
    // the next); under 10% luminance, so it is not a photosafe transition (owner call, see CONTRACT)
    const ph = view.beat.phase, r = LOOK.beatBreathRise;
    const breath = gentle ? 0 : LOOK.beatBreath * (ph < r ? smoothstep(0, 1, ph / r) : ((1 - ph) / (1 - r)) ** 2);
    road.uniforms.bright.value = (fx.bandBright || 0) + breath;

    const skyOf = (x) => SKIES[x.sky] || SKIES[FALLBACK_SKY];
    sky.uniforms.twist.value = mixOf((x) => skyOf(x).twist);
    sky.uniforms.turn.value = (sky.uniforms.turn.value + dt * mixOf((x) => skyOf(x).turnRadps) * scroll) % (Math.PI * 2);
    sky.uniforms.contrast.value = LOOK.skyContrast * contrast;
    sky.uniforms.rescue.value = fx.rescueGlow || 0;
    skyMesh.position.copy(kart.position);
    aimEye(k, dt);

    padPhase = (padPhase + dt * LOOK.padScrollMps * scroll / LOOK.padStripeM) % 1;
    // pad ring: scale from padRing, brightness from the padGlow envelope (photosafe + Gentle rise)
    const pr = fx.padRing || 0, glow = fx.padGlow || 0;
    pad.uniforms.phase.value = lip.uniforms.phase.value = padPhase;
    pad.uniforms.pulse.value = glow;
    ringMesh.visible = pr > 0 && glow > 0;
    ringMesh.scale.setScalar(1 + pr * LOOK.ringMaxM);
    ring.uniforms.opacity.value = glow;

    tendrilMesh.visible = (fx.tendril || 0) > 0;
    tendrilMesh.position.copy(kart.position);
    tendrilMesh.scale.set(1, LOOK.tendrilM, 1);
    tendril.uniforms.opacity.value = (fx.tendril || 0) * LOOK.tendrilGain;

    const turn = LOOK.artRingTurnRadps * scroll;
    if (countdown) countdown.update(dt, turn, gentle);
    if (finish) finish.update(dt, turn * 0.5);
    if (scatter) scatter.update(dt, scroll);

    particles.update(dt, k, fx, kart);
    lines.update(dt, k, fx, kart);
  }

  function dispose() {
    for (const o of owned) o.traverse((m) => { if (m.geometry) m.geometry.dispose(); if (m.material) m.material.dispose(); });
    particles.dispose(); lines.dispose();
    for (const off of offs) off();
  }
  return { scene, kart, update, dispose };
}
