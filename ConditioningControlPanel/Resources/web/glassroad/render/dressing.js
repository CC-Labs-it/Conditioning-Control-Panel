/* render/dressing.js - Astra's wave 1 models in the world (art/ART.md): the EMI teacup kart, the
 * countdown spiral rings, the finish arch and the floating prop scatter. Every builder returns null
 * when its asset is missing, and scene.js keeps the greybox stand-in.
 *
 * Shader-driven faces: any material named spiral_mask / portal_glass / emi_screen is swapped for our
 * own node material (the spiral mask coloured and turned by the palette, a drawn EMI face), so the
 * flash limiter and Gentle Visuals own every moving or bright surface. Transmission glass is dropped
 * for a plain translucent tint (a transmission pass per ring is not worth it on phones).
 *
 * Scatter: a fresh layout per seed. A prop is rejected when it would sit in the tube over the road
 * (lateral gap < its bound radius + scatterMarginM while within scatterAirM above the surface, so
 * jump arcs stay clear) or overlap another prop. Placements use track.query, so a prop beside one
 * stretch is also checked against every other stretch that passes near it.
 */
import {
  Group, InstancedMesh, Object3D, Matrix4, Quaternion, Vector3, MeshBasicNodeMaterial, CanvasTexture,
  SRGBColorSpace, AdditiveBlending, DoubleSide, Color,
} from 'three/webgpu';
import { uniform, uv, vec2, texture, cos, sin, float } from 'three/tsl';
import { clone as cloneSkinned } from 'three/addons/utils/SkeletonUtils.js';
import { LOOK } from './look.js';

const M4 = new Matrix4(), V = new Vector3(), SC = new Vector3(), ZB = new Vector3();

/** A frame's basis as a rotation: x = b (right), y = n (up), z = -t (the model's -Z faces along t). */
function frameQuat(f, out = new Quaternion()) {
  M4.makeBasis(V.set(f.b[0], f.b[1], f.b[2]), SC.set(f.n[0], f.n[1], f.n[2]), ZB.set(-f.t[0], -f.t[1], -f.t[2]));
  return out.setFromRotationMatrix(M4);
}

/** The spiral mask, turned by `turn` about the uv centre, coloured `col`, faded by `lit`. */
function spiralFace(art, col, { additive = false } = {}) {
  const u = { turn: uniform(0), lit: uniform(0) };
  const mask = art.tex('sky-spiral-open-mask');
  const c = uv().sub(0.5), ct = cos(u.turn), st = sin(u.turn);
  const r = vec2(c.x.mul(ct).sub(c.y.mul(st)), c.x.mul(st).add(c.y.mul(ct))).add(0.5);
  const m = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, side: DoubleSide });
  if (additive) m.blending = AdditiveBlending;
  const v = mask ? texture(mask, r).r : float(0.5);
  m.colorNode = col.mul(v.mul(0.8).add(0.2));
  m.opacityNode = u.lit.mul(v.mul(0.6).add(0.4));
  return { material: m, uniforms: u };
}

function glassTint(hex, alpha) {
  const m = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, side: DoubleSide });
  const c = new Color(hex);
  m.color = c; m.opacity = alpha;
  return m;
}

/** EMI's screen face: drawn on a canvas so later waves can swap expressions without new art. */
function emiFace() {
  const cv = document.createElement('canvas'); cv.width = cv.height = 128;
  const g = cv.getContext('2d');
  g.fillStyle = LOOK.faceColour; g.fillRect(0, 0, 128, 128);
  g.fillStyle = LOOK.emiColour;
  for (const x of [40, 88]) { g.beginPath(); g.ellipse(x, 56, 9, 13, 0, 0, Math.PI * 2); g.fill(); }
  g.strokeStyle = LOOK.emiColour; g.lineWidth = 6; g.lineCap = 'round';
  g.beginPath(); g.arc(64, 78, 14, 0.15 * Math.PI, 0.85 * Math.PI); g.stroke();
  const t = new CanvasTexture(cv); t.colorSpace = SRGBColorSpace;
  return new MeshBasicNodeMaterial({ map: t });
}

/** Swap named materials on every mesh under root. */
function swapMaterials(root, byName) {
  root.traverse((o) => {
    if (!o.isMesh) return;
    const name = o.material && o.material.name;
    if (byName[name]) o.material = byName[name];
  });
}

/** The EMI teacup kart (skinned, so cloned with SkeletonUtils). Origin = saucer base, forward -Z. */
export function buildKart(art) {
  const g = art && art.model('emi-teacup-kart');
  if (!g) return null;
  const root = cloneSkinned(g.scene);
  swapMaterials(root, { emi_screen: emiFace() });
  root.traverse((o) => { if (o.isMesh) o.frustumCulled = false; });
  return root;
}

/** Countdown rings standing on the road ahead of the grid; lit one per countdown pulse, opened on go. */
export function buildCountdown(art, track, pal) {
  const g = art && art.model('countdown-ring');
  if (!g) return null;
  const group = new Group(), faces = [];
  const spawn = track.spawnPose(0);
  LOOK.artRingAheadM.forEach((ahead) => {
    const f = track.frameAt((spawn.s + ahead) % track.lengthM), ring = g.scene.clone(true);
    const face = spiralFace(art, pal.col(1));
    swapMaterials(ring, { spiral_mask: face.material, portal_glass: glassTint('#fff4fa', 0.08) });
    ring.position.set(f.p[0], f.p[1], f.p[2]);
    frameQuat(f, ring.quaternion);
    ring.scale.setScalar(LOOK.artRingScale);
    group.add(ring); faces.push(face);
  });
  let lit = 0, openT = -1;
  return {
    object: group,
    /** n = pulses left (3, 2, 1): light one more ring per pulse. */
    pulse(n, pulses) { lit = Math.min(faces.length, pulses - n + 1); openT = -1; },
    go() { lit = faces.length; openT = 0; },
    update(dt, rate, gentle) {
      const rise = 1 - Math.exp(-dt / LOOK.artRingRiseS);
      if (openT >= 0) openT += dt;
      const open = openT < 0 ? 0 : Math.min(1, openT / LOOK.artRingOpenS);
      faces.forEach((f, i) => {
        const want = (i < lit ? 1 : 0) * (1 - open) * (gentle ? LOOK.artGentleFace : 1);
        f.uniforms.lit.value += (want - f.uniforms.lit.value) * rise;
        f.uniforms.turn.value = (f.uniforms.turn.value + dt * rate) % (Math.PI * 2);
      });
      group.visible = openT < 0 || openT < LOOK.artRingHideS;
    },
  };
}

/** The finish arch over s = 0, scaled to clear the road, with a faint turning spiral you drive through. */
export function buildFinish(art, track, pal) {
  const g = art && art.model('finish-portal-lod0'), row = art && art.row('finish-portal-lod0');
  if (!g) return null;
  const f = track.frameAt(0), arch = g.scene.clone(true);
  const face = spiralFace(art, pal.col(0), { additive: true });
  face.uniforms.lit.value = LOOK.artFinishFace;
  swapMaterials(arch, { spiral_mask: face.material, portal_glass: glassTint('#fff4fa', 0.05) });
  const span = row && row.sizeM ? row.sizeM[0] : 16;
  arch.scale.setScalar(Math.max(1, (f.w + 2 * LOOK.artFinishClearM) / span));
  arch.position.set(f.p[0], f.p[1], f.p[2]);
  frameQuat(f, arch.quaternion);
  return {
    object: arch,
    update(dt, rate) { face.uniforms.turn.value = (face.uniforms.turn.value + dt * rate) % (Math.PI * 2); },
  };
}

/** Seeded scatter of floating props. Returns { object, update(dt, spinMul) } or null with no props. */
export function buildScatter(art, track, seed) {
  if (!art) return null;
  let st = (seed >>> 0) % 2147483646 + 1;
  const rnd = (a, b) => { st = (st * 16807) % 2147483647; return a + (st / 2147483647) * (b - a); };
  const kinds = LOOK.artScatterKinds.filter((k) => art.model(k.id) && art.row(k.id));
  if (!kinds.length) return null;
  const total = kinds.reduce((a, k) => a + k.w, 0);
  const pick = () => { let x = rnd(0, total); for (const k of kinds) { if ((x -= k.w) <= 0) return k; } return kinds[0]; };

  const placed = [], L = track.lengthM;
  const tries = LOOK.artScatter * 8;
  for (let i = 0; i < tries && placed.length < LOOK.artScatter; i++) {
    const k = pick(), scale = rnd(k.scale[0], k.scale[1]), R = art.row(k.id).boundRadiusM * scale;
    const f = track.frameAt(rnd(0, L)), side = rnd(0, 1) < 0.5 ? -1 : 1;
    const off = f.w / 2 + R + rnd(LOOK.artScatterSideM[0], LOOK.artScatterSideM[1]), up = rnd(LOOK.artScatterUpM[0], LOOK.artScatterUpM[1]);
    const p = [0, 1, 2].map((j) => f.p[j] + f.b[j] * side * off + f.n[j] * up);
    const qq = track.query(p[0], p[1], p[2]);
    const w = track.frameAt(qq.s).w, gap = Math.abs(qq.lateral) - w / 2;
    const inTube = gap < R + LOOK.artScatterMarginM && qq.height > -R - LOOK.artScatterMarginM && qq.height < R + LOOK.artScatterAirM;
    if (inTube) continue;
    if (placed.some((x) => Math.hypot(x.p[0] - p[0], x.p[1] - p[1], x.p[2] - p[2]) < x.R + R)) continue;
    placed.push({ k, p, R, scale, rot: [rnd(0, 6.28), rnd(0, 6.28), rnd(0, 6.28)], spin: rnd(-1, 1) * k.spin });
  }
  if (!placed.length) return null;

  // one InstancedMesh per mesh part of each kind; instance matrix = placement * part matrix
  const group = new Group(), parts = [];
  for (const k of kinds) {
    const mine = placed.filter((x) => x.k === k);
    if (!mine.length) continue;
    const root = art.model(k.id).scene;
    root.updateMatrixWorld(true);
    root.traverse((m) => {
      if (!m.isMesh) return;
      const im = new InstancedMesh(m.geometry, m.material, mine.length);
      im.frustumCulled = false;
      group.add(im); parts.push({ im, local: m.matrixWorld.clone(), mine });
    });
  }
  const E = new Object3D();
  function write() {
    for (const { im, local, mine } of parts) {
      mine.forEach((x, i) => {
        E.position.set(x.p[0], x.p[1], x.p[2]);
        E.rotation.set(x.rot[0], x.rot[1], x.rot[2]);
        E.scale.setScalar(x.scale);
        E.updateMatrix();
        im.setMatrixAt(i, M4.multiplyMatrices(E.matrix, local));
      });
      im.instanceMatrix.needsUpdate = true;
    }
  }
  write();
  return {
    object: group,
    count: placed.length,
    update(dt, spinMul) {
      if (!spinMul) return;
      for (const x of placed) { x.rot[1] += x.spin * dt * spinMul; x.rot[0] += x.spin * 0.3 * dt * spinMul; }
      write();
    },
  };
}
