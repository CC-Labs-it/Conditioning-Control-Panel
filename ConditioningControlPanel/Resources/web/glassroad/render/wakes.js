/* render/wakes.js - boost ribbons and ground rings for every kart.
 *
 * Ribbons: one flat glowing strip per kart, laid behind the saucer on the road plane while a boost
 * runs (colour by boost kind: the drift tier colour, gold for pads and B1 grants, white for a launch),
 * tapered and faded along its length, easing out over LOOK.trailFadeTcS after the boost. Every kart
 * shares one geometry: one draw call. Points are laid every trailStepM; Q.trailPoints per kart.
 * Rings: a small pool of flat glow rings that grow and fade (landings, pickup pops); with the wave 2
 * particle atlas each is a flat card drawing its soft ring cell, else a RingGeometry glow.
 */
import { Mesh, BufferGeometry, BufferAttribute, DynamicDrawUsage, MeshBasicNodeMaterial, AdditiveBlending, DoubleSide,
  RingGeometry, PlaneGeometry, Color, Vector3, Quaternion } from 'three/webgpu';
import { attribute, smoothstep, abs, uniform, vec3, float } from 'three/tsl';
import { S, BOOST } from '../sim/state.js';
import { LOOK } from './look.js';
import { Q } from './quality.js';
import { createGlowMaterial } from './materials/glow.js';
import { currentArt } from './art.js';
import { atlasOf, atlasCoverage } from './particles.js';
import { FXART_LOOK, cellIndex } from './atlas.js';

/** A flat ring card: the atlas ring cell, its ring at radius 1 of the mesh scale. */
function atlasRing(at, hex, A) {
  const half = 1 / A.ringCellR, c = new Color(hex), u = { opacity: uniform(0) };
  const m = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, blending: AdditiveBlending, side: DoubleSide });
  m.colorNode = vec3(c.r, c.g, c.b).mul(u.opacity);
  m.opacityNode = atlasCoverage(at, float(cellIndex('ring', at.cells)));
  return { geometry: new PlaneGeometry(half * 2, half * 2).rotateX(-Math.PI / 2), material: m, uniforms: u };
}

export function createWakes(feel, count = 8, art = currentArt()) {
  const at = atlasOf(art), A = LOOK.fxArt || FXART_LOOK;
  const N = Q.trailPoints, V = count * N * 2;
  const pos = new BufferAttribute(new Float32Array(V * 3), 3).setUsage(DynamicDrawUsage);
  const col = new BufferAttribute(new Float32Array(V * 4), 4).setUsage(DynamicDrawUsage);
  const side = new Float32Array(V), idx = [];
  for (let k = 0; k < count; k++) for (let i = 0; i < N; i++) {
    const v = (k * N + i) * 2;
    side[v] = -1; side[v + 1] = 1;
    if (i < N - 1) idx.push(v, v + 1, v + 2, v + 2, v + 1, v + 3);
  }
  const g = new BufferGeometry();
  g.setAttribute('position', pos); g.setAttribute('aCol', col); g.setAttribute('aSide', new BufferAttribute(side, 1));
  g.setIndex(idx);
  const m = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, blending: AdditiveBlending, side: DoubleSide });
  const c = attribute('aCol', 'vec4');
  m.colorNode = c.rgb;
  m.opacityNode = c.a.mul(smoothstep(0.35, 1, abs(attribute('aSide', 'float'))).oneMinus());
  const mesh = new Mesh(g, m);
  mesh.frustumCulled = false; mesh.renderOrder = 1;

  const trails = Array.from({ length: count }, () => ({ pts: new Float32Array(N * 6), n: 0, a: 0, rgb: [1, 1, 1] }));
  const tmp = new Vector3(), right = new Vector3(), cc = new Color();

  function colourOf(bk) {
    const colours = feel.get('juice.sparkColours');
    return bk >= BOOST.DRIFT1 && bk <= BOOST.DRIFT3 ? colours[bk - 1] : bk === BOOST.LAUNCH ? LOOK.launchTrail : LOOK.padTrail;
  }

  /** Per kart k and frame. obj = its Object3D (matrixWorld current), st = interpolated state. */
  function kart(k, obj, st, dt, visible = true) {
    const t = trails[k], bk = st[S.boostKind], on = visible && bk !== BOOST.NONE && st[S.respawnPhase] === 0;
    tmp.set(0, LOOK.trailUpM, LOOK.saucerR * 0.9).applyMatrix4(obj.matrixWorld);
    right.set(1, 0, 0).applyQuaternion(obj.quaternion);
    const p = t.pts, last = t.n ? (t.n - 1) * 6 : 0;
    const jump = t.n && Math.hypot(p[last] - tmp.x, p[last + 1] - tmp.y, p[last + 2] - tmp.z) > 20;
    if ((on && t.a < 0.05) || jump || !visible) t.n = 0;             // a new boost grows from the kart
    if (on) { cc.set(colourOf(bk)); t.rgb[0] = cc.r; t.rgb[1] = cc.g; t.rgb[2] = cc.b; }
    t.a += ((on ? 1 : 0) - t.a) * (1 - Math.exp(-dt / (on ? 0.05 : LOOK.trailFadeTcS)));
    const far = !t.n || Math.hypot(p[last] - tmp.x, p[last + 1] - tmp.y, p[last + 2] - tmp.z) >= LOOK.trailStepM;
    if (far) {
      if (t.n === N) { p.copyWithin(0, 6); t.n--; }
      t.n++;
    }
    const h = (t.n - 1) * 6;   // the head point always rides the kart
    p[h] = tmp.x; p[h + 1] = tmp.y; p[h + 2] = tmp.z; p[h + 3] = right.x; p[h + 4] = right.y; p[h + 5] = right.z;
    write(k);
  }

  function write(k) {
    const t = trails[k], P = pos.array, C = col.array, W = LOOK.trailWidthM / 2;
    for (let i = 0; i < N; i++) {
      const j = Math.min(i, Math.max(t.n - 1, 0)) * 6, age = t.n > 1 ? 1 - Math.min(i, t.n - 1) / (t.n - 1) : 1;
      const w = W * (1 - 0.7 * age), a = t.n > 1 && i < t.n ? t.a * LOOK.trailAlpha * (1 - age) ** 1.5 : 0;
      for (let s = 0; s < 2; s++) {
        const v = (k * N + i) * 2 + s, sg = s ? 1 : -1;
        P[v * 3] = t.pts[j] + t.pts[j + 3] * w * sg; P[v * 3 + 1] = t.pts[j + 1] + t.pts[j + 4] * w * sg; P[v * 3 + 2] = t.pts[j + 2] + t.pts[j + 5] * w * sg;
        C[v * 4] = t.rgb[0]; C[v * 4 + 1] = t.rgb[1]; C[v * 4 + 2] = t.rgb[2]; C[v * 4 + 3] = a;
      }
    }
  }

  // ground rings
  const rings = [], rq = new Quaternion();
  for (let i = 0; i < LOOK.ringPool; i++) {
    const mat = at ? atlasRing(at, LOOK.ringColour, A) : createGlowMaterial(LOOK.ringColour);
    const r = new Mesh(at ? mat.geometry : new RingGeometry(1 - LOOK.ringWidth, 1, 48).rotateX(-Math.PI / 2), mat.material);
    r.visible = false; r.renderOrder = 1;
    rings.push({ mesh: r, u: mat.uniforms, t: 0, life: 1, size: 1, gain: 1, rise: 8 });
  }
  let ringHead = 0;
  /** A ring at world p, lying in the plane of quaternion q (the kart's), growing to sizeM. */
  function ring(p, q, sizeM, { lifeS = LOOK.ringLifeS, gain = LOOK.ringGain, gentle = false } = {}) {
    const r = rings[ringHead]; ringHead = (ringHead + 1) % rings.length;
    r.mesh.position.set(p[0], p[1] + 0.04, p[2]);
    if (q) r.mesh.quaternion.copy(q); else r.mesh.quaternion.copy(rq.identity());
    // Gentle: twice as long, half as bright, and the IN takes a third of the life (about 300 ms)
    r.t = 0; r.life = lifeS * (gentle ? 2 : 1); r.size = sizeM; r.gain = gain * (gentle ? 0.5 : 1); r.rise = gentle ? 3 : 8; r.mesh.visible = true;
  }

  function update(dt) {
    pos.needsUpdate = true; col.needsUpdate = true;
    for (const r of rings) {
      if (!r.mesh.visible) continue;
      r.t += dt;
      const k = r.t / r.life;
      if (k >= 1) { r.mesh.visible = false; continue; }
      r.mesh.scale.setScalar(LOOK.saucerR + (r.size - LOOK.saucerR) * (1 - (1 - k) ** 3));
      r.u.opacity.value = r.gain * Math.min(1, k * r.rise) * (1 - k) ** 2;
    }
  }

  const objects = [mesh, ...rings.map((r) => r.mesh)];
  return { objects, kart, ring, update, dispose() { g.dispose(); m.dispose(); for (const r of rings) { r.mesh.geometry.dispose(); r.mesh.material.dispose(); } } };
}
