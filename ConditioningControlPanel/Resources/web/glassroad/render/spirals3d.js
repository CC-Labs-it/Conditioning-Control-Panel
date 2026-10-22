/* render/spirals3d.js - the spiral gates (polish 3, SPIRALS lane).
 *
 * At each gate of tracks/<id>.spirals.json a Loom field is stood up across the road and materialised as
 * a cloud of glitter: thousands of additive sprites sitting on the field's covered bands, turning with
 * the Loom's own loop timing (render/spiralMath.js has the maths; the Loom itself is imported, never
 * forked). Some gates have depth: a short helix tunnel whose arms twist along the road.
 * Karts part the curtain: the particles near a kart's line swirl outward ahead of it and close in behind
 * (four kart slots per gate, the player first). The player crossing a gate gets a sparkle burst round the
 * camera; it asks photosafe first and is cut when refused. Per-particle twinkle with spread phases (the
 * gate's mean brightness stays flat), depth faded near the lens and far away. One draw per visible gate
 * plus one for the burst; only gates within SPIRAL_LOOK.activeM update, farther ones draw a quarter.
 * Gentle: half the turn speed, half the twinkle, a softer parting and a slower, smaller burst.
 *
 * Use (the integrator wires it):
 *   const layout = await fetchSpiralLayout(track.id);
 *   const spirals = createSpiralGates(scene, track, { layout, photosafe, feel });
 *   each frame: spirals.update(dt, { camera, karts: kartObjs.map((o) => o.position), player: 0, gentle });
 */
import { Group, Sprite, SpriteNodeMaterial, InstancedBufferAttribute, AdditiveBlending, Vector3, Vector4, Frustum, Matrix4, Sphere } from 'three/webgpu';
import {
  instancedBufferAttribute, uniform, float, vec3, sin, cos, exp, fract, mix, smoothstep, select, dot, cross, length, max, abs, uv,
  cameraPosition, step, mod,
} from 'three/tsl';
import { Q } from './quality.js';
import {
  SPIRAL_LOOK, loadLoom, loadSpectrum, paletteFor, gateParams, loopS, buildParticles, loomState, hueRows, gateFrames, axialOf, lateralOf, pickSlots, crossed,
  budgetFor, burstCount, depthAlpha, burstEnvelope, makeRng, toHex, hexRgb,
} from './spiralMath.js';

const TAU = Math.PI * 2, GOLD_K = 1.9459101;
const LOOM = await loadLoom();
if (!LOOM) console.warn('[glassroad/spirals] the Loom is not served beside glassroad; gates draw from their stored params');
// the SPECTRUM lane's colour system; until it lands the seeded gates use the lane's own full spectrum
const spectrumMod = await loadSpectrum();

/** tracks/<id>.spirals.json, or null. */
export async function fetchSpiralLayout(trackId, base = new URL('../tracks/', import.meta.url)) {
  try {
    const r = await fetch(new URL(trackId + '.spirals.json', base));
    return r.ok ? await r.json() : null;
  } catch (e) { return null; }
}

const xyz = (p) => (!p ? null : Array.isArray(p) ? p : p.position ? [p.position.x, p.position.y, p.position.z] : [p.x, p.y, p.z]);

function particleShape(glitter) {
  const q = uv().sub(0.5).mul(2), r = q.length();
  const disc = smoothstep(0.15, 1, r).oneMinus();
  const arm = (a, b) => smoothstep(0, 0.12, abs(a)).oneMinus().mul(smoothstep(0.1, 1, abs(b)).oneMinus());
  const star = max(smoothstep(0, 0.35, r).oneMinus(), max(arm(q.x, q.y), arm(q.y, q.x)));
  return mix(disc, star, glitter);
}

function buildGate(g, gf, params, seed, L) {
  const P = buildParticles(params, g, L.perGate.high, seed, L);
  const a0 = instancedBufferAttribute(new InstancedBufferAttribute(P.a0, 4), 'vec4');
  const a1 = instancedBufferAttribute(new InstancedBufferAttribute(P.a1, 4), 'vec4');
  const a2 = instancedBufferAttribute(new InstancedBufferAttribute(P.a2, 4), 'vec4');
  const v3 = (a) => uniform(new Vector3(a[0], a[1], a[2]));
  const u = {
    c: v3(gf.c), t: v3(gf.t), b: v3(gf.b), n: v3(gf.n),
    rot1: uniform(0), rot2: uniform(0), zoom: uniform(1), wobPhase: uniform(0), time: uniform(0),
    h0: uniform(new Vector3(1, 0, 0)), h1: uniform(new Vector3(0, 1, 0)), h2: uniform(new Vector3(0, 0, 1)),
    twAmp: uniform(L.twinkleAmp), alpha: uniform(L.alpha * depthAlpha(g.depthM || 0, L)), part: uniform(1),
    slots: Array.from({ length: L.slots }, () => uniform(new Vector4(0, -1e5, 0, 0))),
  };
  const depth = g.depthM || 0, twist = depth > 0 ? (g.helixTurns || 0) * TAU / depth : 0, taper = depth > 0 ? (g.taper || 0) / depth : 0;
  const W = params.wobble, wobOn = W.amp > 0.001;
  const wob = (rc) => (wobOn ? sin(rc.clamp(0, 1).mul(TAU * W.freq).add(u.wobPhase)).mul(W.amp) : float(0));
  const l2 = step(0.5, mod(a0.w, 2)), glitter = step(1.5, a0.w), radial = a2.w.greaterThan(0.5);
  const rot = mix(u.rot1, u.rot2, l2), arms = mix(float(params.layer.arms), float(params.layer2.arms), l2);
  // tunnel style: the bands ride the radius (t moves by rot / 2pi), else the field turns rigidly
  const rOfT = (t) => exp(t.mul(GOLD_K)).sub(1).div(6);
  const tPre = fract(a0.x.add(rot.div(TAU)));
  const tR = fract(a0.x.add(rot.div(TAU)).sub(wob(rOfT(tPre)).mul(0.5).div(arms)));
  const rc = select(radial, rOfT(tR), a0.x);
  const th = select(radial, a0.y, a0.y.add(rot).sub(wob(a0.x))).add(a0.z.mul(twist));
  const rW = rc.mul(g.radiusM).mul(u.zoom).mul(a0.z.mul(taper).add(1));
  // field x = road right, field y (down on the Loom canvas) = minus road up: the driver sees the Loom as drawn
  const base = u.c.add(u.b.mul(rW.mul(cos(th)))).add(u.n.mul(rW.mul(sin(th)).negate())).add(u.t.mul(a0.z));
  // the curtain: each kart slot pushes the particles near its line outward with a swirl
  let off = vec3(0, 0, 0);
  for (const s of u.slots) {
    const d = base.sub(s.xyz), da = dot(d, u.t), dl = d.sub(u.t.mul(da)), dist = length(dl).add(1e-3);
    const wAx = select(da.greaterThanEqual(0), smoothstep(0, L.partLeadM, da).oneMinus(), smoothstep(0, L.partTrailM, da.negate()).oneMinus());
    const wLat = smoothstep(L.partInnerM, L.partRadiusM, dist).oneMinus();
    const push = s.w.mul(wAx).mul(wLat).mul(u.part).mul(L.partM);
    const dir = dl.div(dist);
    off = off.add(dir.add(cross(u.t, dir).mul(L.swirlShare)).mul(push));
  }
  const pos = base.add(off);
  const m = new SpriteNodeMaterial({ transparent: true, depthWrite: false, blending: AdditiveBlending });
  m.positionNode = pos;
  const tw = sin(u.time.mul(a2.y).add(a2.x).mul(TAU)).mul(0.5).add(0.5);
  const shine = select(glitter.greaterThan(0.5), tw.mul(tw).mul(tw).mul(1.6).add(0.25).mul(u.twAmp.div(L.twinkleAmp)).add(u.twAmp.div(L.twinkleAmp).oneMinus()),
    u.twAmp.mul(tw).oneMinus());
  m.scaleNode = a1.w.mul(tw.mul(0.25).add(0.9));
  const col = vec3(dot(u.h0, a1.xyz), dot(u.h1, a1.xyz), dot(u.h2, a1.xyz)).clamp(0, 1);
  m.colorNode = col.mul(mix(float(L.gain), float(L.glitterGain), glitter));
  const camD = pos.distance(cameraPosition);
  const near = smoothstep(L.nearM, L.nearM + L.nearSpanM, camD), far = smoothstep(L.farFadeM * 0.6, L.farFadeM, camD).oneMinus();
  const edge = smoothstep(L.holeFrac * 0.5, L.holeFrac * 1.5, rc).mul(smoothstep(0.93, 1.0, rc).oneMinus());
  // a tunnel field flows outward so its particles crowd the eye: thin them toward the centre
  const flow = select(radial, rc.mul(2).clamp(L.radialFloor, 1), float(1));
  // the road window (spiralMath corridorFade): the road and the next turn stay readable through the gate
  const rel = pos.sub(u.c), halfW = (gf.w || 12) / 2, upM = g.upM ?? L.upM;
  const inU = smoothstep(halfW + L.corridorSideM, halfW + L.corridorSideM + L.corridorSoftM, abs(dot(rel, u.b))).oneMinus();
  const inH = smoothstep(L.corridorUpM, L.corridorUpM + L.corridorSoftM, dot(rel, u.n).add(upM)).oneMinus();
  const road = float(1).sub(inU.mul(inH).mul(1 - L.corridorAlpha));
  m.opacityNode = u.alpha.mul(a2.z).mul(shine).mul(near).mul(far).mul(edge).mul(flow).mul(road).mul(particleShape(glitter));
  const sprite = new Sprite(m);
  sprite.count = Math.min(P.n, budgetFor(Q.name, 0, L));
  sprite.frustumCulled = false; sprite.renderOrder = 2;
  sprite.name = 'spiral-gate-' + g.id;
  const loop = loopS(params, LOOM);
  // looking down a helix tunnel every depth layer stacks: thin the alpha so it stays coloured, not white
  const depthK = depthAlpha(depth, L);
  return {
    id: g.id, g, gf, params, sprite, material: m, u, n: P.n, loop, phaseT: 0, prevAxial: null,
    depthK,
    sphere: new Sphere(new Vector3(gf.c[0], gf.c[1], gf.c[2]), g.radiusM * (1 + (g.taper || 0)) * 1.3 + depth / 2 + L.partM),
    burstCols: [0, 1, 2].map((i) => hexRgb(params.layer.colors[i % params.layer.colors.length])),
  };
}

function buildBurst(L) {
  const B = L.burst, n = B.count.high, rnd = makeRng(4242);
  const d = new Float32Array(n * 4), e = new Float32Array(n * 4);
  for (let i = 0; i < n; i++) {
    let x, y, z, l;
    do { x = rnd() * 2 - 1; y = rnd() * 2 - 1; z = rnd() * 2 - 1; l = Math.hypot(x, y, z); } while (l > 1 || l < 0.1);
    d.set([x / l, y / l, z / l, B.speedMps[0] + rnd() * (B.speedMps[1] - B.speedMps[0])], i * 4);
    e.set([B.r0M[0] + rnd() * (B.r0M[1] - B.r0M[0]), B.sizeM[0] + rnd() * (B.sizeM[1] - B.sizeM[0]), rnd(), rnd()], i * 4);
  }
  const ad = instancedBufferAttribute(new InstancedBufferAttribute(d, 4), 'vec4');
  const ae = instancedBufferAttribute(new InstancedBufferAttribute(e, 4), 'vec4');
  const u = { centre: uniform(new Vector3()), age: uniform(0), env: uniform(0),
    c0: uniform(new Vector3(1, 0.3, 0.8)), c1: uniform(new Vector3(0, 0.9, 1)), c2: uniform(new Vector3(1, 0.85, 0.2)) };
  const m = new SpriteNodeMaterial({ transparent: true, depthWrite: false, blending: AdditiveBlending, depthTest: false });
  m.positionNode = u.centre.add(ad.xyz.mul(ae.x.add(ad.w.mul(u.age))));
  const tw = sin(u.age.mul(ae.z.mul(1.5).add(1)).add(ae.z).mul(TAU)).mul(0.5).add(0.5);
  m.scaleNode = ae.y.mul(tw.mul(0.4).add(0.8));
  const pick = ae.w, base = select(pick.lessThan(0.34), u.c0, select(pick.lessThan(0.67), u.c1, u.c2));
  m.colorNode = mix(base, vec3(1, 1, 1), tw.mul(0.4)).mul(B.gain);
  m.opacityNode = u.env.mul(B.alpha).mul(tw.mul(0.5).add(0.5)).mul(particleShape(float(1)));
  const sprite = new Sprite(m);
  sprite.count = 0; sprite.visible = false; sprite.frustumCulled = false; sprite.renderOrder = 3;
  sprite.name = 'spiral-burst';
  return { sprite, material: m, u, n, age: -1, gentle: false };
}

/**
 * @param {Scene} scene
 * @param {object} track loaded track (frameAt, lengthM, sectionAt)
 * @param {{ layout: object, photosafe?: { request(kind: string): boolean }, feel?: object, look?: object }} opts
 * @returns {{ object: Group, gates: Array<{id, s, centre, radiusM}>, update(dt, view), dispose() } | null}
 */
export function createSpiralGates(scene, track, { layout = null, photosafe = null, feel = null, look = SPIRAL_LOOK } = {}) {
  const L = look;
  if (!layout || !Array.isArray(layout.gates) || !layout.gates.length) return null;
  const group = new Group();
  group.name = 'spiral-gates';
  const frames = gateFrames(track, layout.gates, L);
  const gates = [];
  layout.gates.forEach((g, i) => {
    try {
      // sectionAt gives an index into track.sections (integrator fix: every gate read dawn before)
      const si = track.sectionAt ? track.sectionAt(frames[i].s) : null;
      const sec = typeof si === 'number' ? (track.sections || [])[si] : si;
      const params = gateParams(g, LOOM, paletteFor(sec && sec.palette, spectrumMod, L));
      const gate = buildGate(g, frames[i], params, (i + 1) * 7919, L);
      gates.push(gate); group.add(gate.sprite);
    } catch (e) { console.warn('[glassroad/spirals] gate ' + g.id + ' failed: ' + (e && e.message)); }
  });
  const burst = buildBurst(L);
  group.add(burst.sprite);
  if (scene) scene.add(group);

  const frustum = new Frustum(), pv = new Matrix4(), fwd = new Vector3(), cam = [0, 0, 0];
  const getOpt = (k, d) => { try { const v = feel && feel.get(k); return v == null ? d : v; } catch (e) { return d; } };

  function fireBurst(gate, gentle) {
    const granted = photosafe && photosafe.request ? photosafe.request('spiral-burst') : true;
    burst.count = burstCount(Q.name, { granted, gentle }, L);
    burst.age = 0; burst.gentle = gentle;
    const rows = hueRows(loomState(gate.params, (gate.phaseT % gate.loop) / gate.loop).hue);
    const rot = (c) => rows.map((r) => Math.min(1, Math.max(0, r[0] * c[0] + r[1] * c[1] + r[2] * c[2])));
    [burst.u.c0, burst.u.c1, burst.u.c2].forEach((uu, k) => { const c = rot(gate.burstCols[k]); uu.value.set(c[0], c[1], c[2]); });
  }

  /**
   * @param {number} dt seconds
   * @param {{ camera: Camera, karts?: Array<Vector3|number[]|Object3D>, player?: number, gentle?: boolean }} view
   */
  function update(dt, { camera = null, karts = [], player = 0, gentle = false } = {}) {
    if (!camera) return;
    camera.updateMatrixWorld();
    pv.multiplyMatrices(camera.projectionMatrix, camera.matrixWorldInverse);
    frustum.setFromProjectionMatrix(pv);
    cam[0] = camera.position.x; cam[1] = camera.position.y; cam[2] = camera.position.z;
    const ks = karts.map(xyz), me = ks[player] || null;
    const turn = L.turnMul * (gentle ? L.gentleTurnMul : 1);
    for (const gate of gates) {
      const gf = gate.gf, dist = Math.hypot(cam[0] - gf.c[0], cam[1] - gf.c[1], cam[2] - gf.c[2]);
      // the player's crossing (always checked, it is cheap)
      if (me) {
        const a = axialOf(gf, me);
        if (gate.prevAxial !== null && crossed(gate.prevAxial, a, lateralOf(gf, me), gate.g.radiusM)) fireBurst(gate, gentle);
        gate.prevAxial = a;
      }
      const n = budgetFor(Q.name, dist, L);
      gate.sprite.visible = n > 0 && frustum.intersectsSphere(gate.sphere);
      gate.sprite.count = Math.min(gate.n, n);
      if (!gate.sprite.visible || dist > L.activeM) continue;
      // the Loom phase: one seamless loop per loopMs2, slowed by Gentle
      gate.phaseT = (gate.phaseT + dt * turn) % (gate.loop * 1000);
      const st = loomState(gate.params, (gate.phaseT % gate.loop) / gate.loop), u = gate.u;
      u.rot1.value = st.rot1 % TAU; u.rot2.value = st.rot2 % TAU; u.zoom.value = st.zoom; u.wobPhase.value = st.wobPhase;
      const rows = hueRows(st.hue);
      u.h0.value.set(...rows[0]); u.h1.value.set(...rows[1]); u.h2.value.set(...rows[2]);
      u.time.value = (u.time.value + dt) % 1000;
      u.twAmp.value = L.twinkleAmp * (gentle ? L.gentleTwinkleMul : 1);
      u.alpha.value = L.alpha * gate.depthK * (gentle ? L.gentleAlphaMul : 1);
      u.part.value = gentle ? L.gentlePartMul : 1;
      const slots = pickSlots(gf, gate.g, ks, L, player);
      for (let k = 0; k < u.slots.length; k++) {
        const p = k < slots.length ? ks[slots[k]] : null;
        if (p) u.slots[k].value.set(p[0], p[1], p[2], 1); else u.slots[k].value.set(0, -1e5, 0, 0);
      }
    }
    // the sparkle burst rides with the camera, a little ahead of it
    if (burst.age >= 0) {
      burst.age += dt;
      const env = burstEnvelope(burst.age, { gentle: burst.gentle, riseMs: getOpt('gentle.riseMs', 300), fadeMs: getOpt('gentle.fadeMs', 600) }, L);
      if (env <= 0 && burst.age > L.burst.riseS) { burst.age = -1; burst.sprite.visible = false; }
      else {
        camera.getWorldDirection(fwd);
        burst.u.centre.value.copy(camera.position).addScaledVector(fwd, L.burst.aheadM);
        burst.u.age.value = burst.age * (burst.gentle ? 0.5 : 1);
        burst.u.env.value = env;
        burst.sprite.count = burst.count; burst.sprite.visible = burst.count > 0;
      }
    }
  }

  function dispose() {
    for (const g of gates) g.material.dispose();
    burst.material.dispose();
    if (group.parent) group.parent.remove(group);
  }

  return {
    object: group, update, dispose,
    gates: gates.map((g) => ({ id: g.id, s: g.gf.s, centre: g.gf.c.slice(), radiusM: g.g.radiusM, depthM: g.g.depthM || 0 })),
    /** dev: fire the camera burst of gate i now */
    burstNow(i = 0, gentle = false) { if (gates[i]) fireBurst(gates[i], gentle); },
  };
}
