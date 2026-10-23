/* render/dressing.js - Astra's wave 1 models in the world (art/ART.md): the EMI teacup kart, the
 * countdown spiral rings, the finish arch and the floating prop scatter. Every builder returns null
 * when its asset is missing, and scene.js keeps the greybox stand-in.
 *
 * Shader-driven faces: any material named spiral_mask / portal_glass / emi_screen is swapped for our
 * own node material (the spiral mask coloured and turned by the palette, a drawn EMI face, the finish
 * arch's finish_membrane as a faint additive spiral), so the
 * flash limiter and Gentle Visuals own every moving or bright surface. Transmission glass is dropped
 * for a plain translucent tint (a transmission pass per ring is not worth it on phones).
 *
 * Scatter: a fresh layout per seed (render/colossiMath.js placeScatter). A prop is rejected when it
 * would sit in the tube over the road (lateral gap < its bound radius + scatterMarginM while within
 * scatterAirM above the surface, so jump arcs stay clear), overlap another prop or touch a colossus.
 * Every road stretch is checked, so a prop beside one stretch also clears one passing near it.
 */
import {
  Group, InstancedMesh, Object3D, Matrix4, Quaternion, Vector3, MeshBasicNodeMaterial, CanvasTexture,
  SRGBColorSpace, AdditiveBlending, DoubleSide, Color, LOD, InstancedBufferAttribute, BackSide, CylinderGeometry, PlaneGeometry, BufferAttribute,
} from 'three/webgpu';
import { uniform, uv, vec2, vec3, texture, cos, sin, float, smoothstep, length, positionView, instancedBufferAttribute, positionLocal, normalLocal,
  cameraPosition, clamp, mix, attribute, normalWorld, positionWorld, normalize, abs, dot, pow, max } from 'three/tsl';
import { clone as cloneSkinned } from 'three/addons/utils/SkeletonUtils.js';
import { LOOK } from './look.js';
import { Q } from './quality.js';
import { placeScatter, sampleTrack, worldLook, scatterCount, scatterBasisInto, placeHeroes, heroBasis, heroBasisInto, heroCount, heroCasters, V3 } from './colossiMath.js';
import { createSoftMaterial } from './materials/colossus.js';

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

/**
 * The finish membrane (wave 2 arch): the turning spiral filling the opening, faint and additive, kept
 * round on the 14.4 x 10 m plane (aspect), softened at its edge and gone near the camera, so driving
 * through it never washes the view.
 */
function membraneFace(art, col, aspect) {
  const u = { turn: uniform(0), lit: uniform(0) };
  const mask = art.tex('sky-spiral-open-mask');
  const c = uv().sub(0.5).mul(vec2(aspect, 1)), ct = cos(u.turn), st = sin(u.turn);
  const r = vec2(c.x.mul(ct).sub(c.y.mul(st)), c.x.mul(st).add(c.y.mul(ct))).add(0.5);
  const v = mask ? texture(mask, r).r : float(0.35);
  const [n0, n1] = LOOK.artMembraneNearM ?? [5, 18];
  const near = smoothstep(n0, n1, positionView.z.negate());
  const vig = smoothstep(0.28, 0.62, length(c)).oneMinus();
  const m = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, side: DoubleSide, blending: AdditiveBlending });
  m.colorNode = col.mul(v.mul(0.85).add(0.15));
  m.opacityNode = u.lit.mul(v).mul(near).mul(vig);
  return { material: m, uniforms: u };
}

/** The membrane plane's width / height from its geometry (1.44 for the wave 2 arch). */
function membraneAspect(root) {
  let a = 0;
  root.traverse((o) => {
    if (a || !o.isMesh || !o.material || o.material.name !== 'finish_membrane') return;
    o.geometry.computeBoundingBox();
    const b = o.geometry.boundingBox, w = b.max.x - b.min.x, h = b.max.y - b.min.y;
    if (w > 0 && h > 0) a = w / h;
  });
  return a || 1.44;
}

/**
 * The finish arch over s = 0, scaled to clear the road: finish-portal-lod0 near, lod1 past
 * LOOK.artFinishLod1M (a three LOD, switched by camera distance at render), the crown spiral and the
 * membrane spiral you drive through. update(dt, rate, gentle): Gentle halves the membrane.
 */
export function buildFinish(art, track, pal) {
  const g0 = art && art.model('finish-portal-lod0'), g1 = art && art.model('finish-portal-lod1');
  const row = art && (art.row('finish-portal-lod0') || art.row('finish-portal-lod1'));
  if (!g0 && !g1) return null;
  const f = track.frameAt(0);
  const face = spiralFace(art, pal.col(0), { additive: true });
  face.uniforms.lit.value = LOOK.artFinishFace;
  const glass = glassTint('#fff4fa', 0.05);
  const membrane = membraneFace(art, pal.col(1), membraneAspect((g0 || g1).scene));
  const lod = new LOD();
  [g0, g1].forEach((g, i) => {
    if (!g) return;
    const arch = g.scene.clone(true);
    swapMaterials(arch, { spiral_mask: face.material, portal_glass: glass, finish_membrane: membrane.material });
    arch.traverse((o) => { if (o.isMesh && o.material === membrane.material) o.renderOrder = 2; });
    lod.addLevel(arch, (i === 0 || !g0) ? 0 : (LOOK.artFinishLod1M ?? 90), 0.1);
  });
  // the rigid opening (clearanceWidthM) must clear the road plus a margin; older rows only carry sizeM
  const span = row ? row.clearanceWidthM || (row.sizeM ? row.sizeM[0] : 16) : 16;
  lod.scale.setScalar(Math.max(1, (f.w + 2 * LOOK.artFinishClearM) / span));
  lod.position.set(f.p[0], f.p[1], f.p[2]);
  frameQuat(f, lod.quaternion);
  const want = LOOK.artMembraneLit ?? 0.22, soft = LOOK.artMembraneGentle ?? 0.5;
  membrane.uniforms.lit.value = want;
  return {
    object: lod,
    update(dt, rate, gentle = false) {
      face.uniforms.turn.value = (face.uniforms.turn.value + dt * rate) % (Math.PI * 2);
      const mu = membrane.uniforms;
      mu.turn.value = (mu.turn.value + dt * rate * (LOOK.artMembraneTurnMul ?? 0.6)) % (Math.PI * 2);
      // eased, so a Gentle toggle mid-race never steps the brightness
      mu.lit.value += (want * (gentle ? soft : 1) - mu.lit.value) * (1 - Math.exp(-dt / 0.4));
    },
  };
}

/** The slot cabinet's reel screen: three lit reels (spiral, heart, seven) on a deep violet glass. */
function reelTexture() {
  const cv = document.createElement('canvas'); cv.width = cv.height = 256;
  const g = cv.getContext('2d');
  const bg = g.createLinearGradient(0, 0, 0, 256);
  bg.addColorStop(0, '#3a0f5c'); bg.addColorStop(1, '#14052a');
  g.fillStyle = bg; g.fillRect(0, 0, 256, 256);
  const cols = ['#ff3fb0', '#19e3ff', '#ffd23a'];
  for (let i = 0; i < 3; i++) {
    const x = 14 + i * 80;
    const rg = g.createLinearGradient(0, 40, 0, 216);
    rg.addColorStop(0, '#d8c8f0'); rg.addColorStop(0.5, '#fff8fc'); rg.addColorStop(1, '#d8c8f0');
    g.fillStyle = rg; g.fillRect(x, 40, 68, 176);
    const cx = x + 34, cy = 128;
    g.strokeStyle = g.fillStyle = cols[i]; g.lineWidth = 7; g.lineCap = 'round';
    if (i === 0) {
      g.beginPath();
      for (let a = 0; a < Math.PI * 5; a += 0.1) { const r = 2 + a * 1.6; g.lineTo(cx + Math.cos(a) * r, cy + Math.sin(a) * r); }
      g.stroke();
    } else if (i === 1) {
      g.beginPath(); g.moveTo(cx, cy + 22);
      g.bezierCurveTo(cx - 40, cy - 6, cx - 18, cy - 34, cx, cy - 14);
      g.bezierCurveTo(cx + 18, cy - 34, cx + 40, cy - 6, cx, cy + 22); g.fill();
    } else {
      g.font = 'bold 64px sans-serif'; g.textAlign = 'center'; g.textBaseline = 'middle'; g.fillText('7', cx, cy + 2);
    }
  }
  // the pay line
  g.fillStyle = '#ff3fb0'; g.fillRect(6, 126, 244, 4);
  const t = new CanvasTexture(cv); t.colorSpace = SRGBColorSpace;
  return t;
}

/** The ink outline: the same mesh drawn back faces only, pushed out along its normals by a width that grows
 * with distance (about the same pixels near and far). Instancing runs first, so positionLocal is in the
 * world (the hero group sits at the origin). */
function outlineMaterial(W) {
  const m = new MeshBasicNodeMaterial({ side: BackSide });
  const d = cameraPosition.sub(positionLocal).length();
  const w = clamp(d.mul(W.heroOutlineK), W.heroOutlineM[0], W.heroOutlineM[1]);
  m.positionNode = positionLocal.add(normalLocal.normalize().mul(w));
  const c = new Color(W.heroOutline);
  m.colorNode = vec3(c.r, c.g, c.b);
  return m;
}

/**
 * The hero set (polish 4 COLOSSAL, owner "way bigger, about 4 or 5 times that, colossal"): the recognisable
 * props as colossi 30 to 55 m tall, 15 to 90 m off the road (colossiMath.js placeHeroes), each on a glass
 * plinth with a lit top (a leaning one rises bare out of the void below the road), an ink outline that keeps
 * its pixel width at any distance, a rim in a contrasting hue and a soft backlight disc, so the silhouette
 * reads against the busy sky. Chess pieces wear the chess game's pink and white silicone (or cyan glass, or
 * its violet side), slot cabinets show lit reels and EMI's face, cards and watches keep their own paint,
 * discs turn the Loom mask. Upright pieces turn slowly; facing pieces swing a little toward the oncoming
 * camera. The nearest few whose shadow reaches the road (heroCasters) get their own instanced meshes that
 * cast the lamp's shadow (render/shadows.js casts them on the high tier); nothing else here casts. Nothing
 * has a time term in its shading, so nothing flickers; Gentle (spinMul) slows or holds the turning.
 * Returns { object, placed, bodies, casterBodies, update(dt, spinMul) } or null.
 */
export function buildHeroes(art, track, seed, { pal = null, avoid = [], samples = null } = {}) {
  if (!art || !pal) return null;
  // dev A/B: ?heroes=0 leaves the hero set out (frame cost checks)
  try { if (new URLSearchParams(globalThis.location ? location.search : '').get('heroes') === '0') return null; } catch { /* no location */ }
  const W = worldLook();
  const kinds = (W.heroKinds || []).filter((k) => art.model(k.id) && art.row(k.id));
  if (!kinds.length) return null;
  const sectionOf = (s) => { try { const sec = track.sections[track.sectionAt(s)]; return sec ? sec.palette : null; } catch { return null; } };
  const S = samples || sampleTrack(track, W.stepM);
  const placed = placeHeroes(track, kinds, (id) => art.row(id), seed, { want: heroCount(Q.name, W), avoid, W, samples: S, sectionOf });
  if (!placed.length) return null;
  // the nearest few whose shadow reaches the road cast the lamp's shadow (render/shadows.js casts them on the
  // high tier only, the scatter path); they get their own instanced meshes so the rest stay out of the map
  const cast = new Set(heroCasters(placed, LOOK.lightDir, S, W));
  placed.forEach((x, i) => { x.cast = cast.has(i); });

  const group = new Group(), parts = [], faces = [], C = new Color();
  group.name = 'hero-props';
  // dev and shot tours read the layout here (id, s, side, gap, up, height)
  group.userData.heroes = placed.map((x) => ({ id: x.k.id, s: x.s, side: x.side, gap: x.gap, up: x.up, h: x.h, lean: x.lean, cast: x.cast }));
  const ink = outlineMaterial(W);
  let reels = null;
  const attr3 = (list, get) => {
    const a = new Float32Array(list.length * 3);
    list.forEach((x, i) => { C.set(get(x)); a.set([C.r, C.g, C.b], i * 3); });
    return new InstancedBufferAttribute(a, 3);
  };
  for (const key of [...new Set(placed.map((x) => x.k.id + (x.cast ? '|cast' : '')))]) {
    const mine = placed.filter((x) => x.k.id + (x.cast ? '|cast' : '') === key), id = mine[0].k.id, look = mine[0].k.look, casts = mine[0].cast;
    const tintA = attr3(mine, (x) => x.look.body), rimA = attr3(mine, (x) => x.look.rim);
    const chromeA = new InstancedBufferAttribute(new Float32Array(mine.map((x) => x.look.chrome || 0)), 1);
    const tint = instancedBufferAttribute(tintA, 'vec3'), rim = instancedBufferAttribute(rimA, 'vec3');
    const chrome = look === 'chess' ? instancedBufferAttribute(chromeA, 'float') : null;
    const root = art.model(id).scene;
    root.updateMatrixWorld(true);
    let big = null, bigN = -1;
    root.traverse((m) => {
      if (!m.isMesh || Array.isArray(m.material)) return;
      const name = m.material && m.material.name;
      let mat;
      if (look === 'slot' && name === 'screen') {
        reels = reels || reelTexture();
        mat = new MeshBasicNodeMaterial({ map: reels });
      } else if (look === 'slot' && name === 'emi_screen') mat = emiFace();
      else if (look === 'slot' && name === 'slot_lamps') {
        mat = new MeshBasicNodeMaterial(); mat.colorNode = rim.mul(0.6).add(vec3(0.6, 0.35, 0.5));
      } else if (look === 'disc' && name === 'spiral_mask') {
        const f = spiralFace(art, tint.mul(1.3));
        f.uniforms.lit.value = 1; faces.push(f); mat = f.material;
      } else {
        mat = createSoftMaterial(m.material, pal, { tint, rim, chrome, tintMix: look === 'paint' ? 0.3 : look === 'chess' ? 1 : null,
          rimGain: W.heroRimGain, rimPow: W.heroRimPow, haze: 0.5, fill: W.heroFill, ink: look === 'card' }).material;
      }
      const im = new InstancedMesh(m.geometry, mat, mine.length);
      im.frustumCulled = false; im.userData.cast = casts;
      group.add(im); parts.push({ im, local: m.matrixWorld.clone(), mine });
      const n = m.geometry.attributes.position.count;
      if (n > bigN && !(mat.transparent)) { bigN = n; big = im; }
    });
    if (big) {
      const o = new InstancedMesh(big.geometry, ink, mine.length);
      o.instanceMatrix = big.instanceMatrix;    // the same matrices, written once per frame
      o.frustumCulled = false; o.userData.noCast = true;
      group.add(o);
    }
  }

  // plinths: a low glass drum, its top edge lit in the hero's rim hue (py: 0 at the top, -1 at the base)
  const pg = new CylinderGeometry(1, 1.1, 1, 40, 1).translate(0, -0.5, 0);
  const py = new Float32Array(pg.attributes.position.count);
  for (let i = 0; i < py.length; i++) py[i] = pg.attributes.position.getY(i);
  pg.setAttribute('py', new BufferAttribute(py, 1));
  const rimAll = instancedBufferAttribute(attr3(placed, (x) => x.look.rim), 'vec3');
  const pm = new MeshBasicNodeMaterial();
  const yy = attribute('py', 'float');
  const N = normalize(normalWorld), V = normalize(cameraPosition.sub(positionWorld));
  const fres = pow(max(float(1).sub(abs(dot(N, V))), 0), 2);
  const glass = vec3(0.16, 0.07, 0.3).add(rimAll.mul(0.12)).add(rimAll.mul(fres).mul(0.5));
  const band = smoothstep(-0.22, -0.08, yy);
  pm.colorNode = mix(glass, rimAll.mul(1.25), band);
  const plinths = new InstancedMesh(pg, pm, placed.length);
  plinths.frustumCulled = false;
  group.add(plinths);

  // backlight: a soft disc in the rim hue behind each piece, square to the oncoming camera (static)
  const hm = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, blending: AdditiveBlending, side: DoubleSide });
  const r = length(uv().sub(0.5)).mul(2);
  const halo = smoothstep(1, 0.45, r).mul(smoothstep(0.05, 0.6, r).mul(0.6).add(0.4));
  hm.colorNode = rimAll.mul(halo).mul(W.heroHalo);
  hm.opacityNode = float(1);
  const halos = new InstancedMesh(new PlaneGeometry(2, 2), hm, placed.length);
  halos.frustumCulled = false; halos.renderOrder = -0.5; halos.userData.noCast = true;
  group.add(halos);

  const E = new Object3D(), BX = new Vector3(), BY = new Vector3(), BZ = new Vector3();
  function basisMatrix(b, p, sx, sy, sz) {
    M4.makeBasis(BX.set(b.X[0], b.X[1], b.X[2]), BY.set(b.Y[0], b.Y[1], b.Y[2]), BZ.set(b.Z[0], b.Z[1], b.Z[2]));
    E.quaternion.setFromRotationMatrix(M4);
    E.position.set(p[0], p[1], p[2]); E.scale.set(sx, sy, sz); E.updateMatrix();
    return E.matrix;
  }
  placed.forEach((x, i) => {
    const b0 = heroBasis(x, 0, W), Y0 = x.Y0 || x.Y;
    const PX = V3.norm(V3.cross(Y0, x.F)), PZ = V3.cross(PX, Y0);
    const pk = x.bare ? 0 : 1;   // a leaning hero rises bare out of the void below the road
    plinths.setMatrixAt(i, basisMatrix({ X: PX, Y: Y0, Z: PZ }, x.p, x.plinth.r * pk, x.plinth.hM * pk, x.plinth.r * pk));
    // the disc sits behind the piece (away from the camera), centred a little above its middle
    const back = (x.rLat || x.plinth.r) + 2, c = [0, 1, 2].map((j) => x.p[j] + x.Y[j] * x.h * 0.55 - x.F[j] * back);
    const hr = x.h * W.heroHaloR;
    halos.setMatrixAt(i, basisMatrix({ X: b0.X, Y: b0.Y, Z: x.F }, c, hr, hr, 1));
  });
  plinths.instanceMatrix.needsUpdate = true; halos.instanceMatrix.needsUpdate = true;

  // per frame: one kept basis, plain loops (polish 6 PERF: no arrays or closures per frame)
  const HB = { X: [0, 0, 0], Y: [0, 0, 0], Z: [0, 0, 0] };
  function write() {
    for (const x of placed) x.m = (x.m || new Matrix4()).copy(basisMatrix(heroBasisInto(x, x.ang, W, HB), x.p, x.scale, x.scale, x.scale));
    for (const { im, local, mine } of parts) {
      for (let i = 0; i < mine.length; i++) im.setMatrixAt(i, M4.multiplyMatrices(mine[i].m, local));
      im.instanceMatrix.needsUpdate = true;
    }
  }
  write();
  // only the few casters (above) go into the shadow pass; outlines, plinths, halos and the rest never cast
  group.traverse((o) => { o.userData.noCast = !o.userData.cast; });
  return {
    object: group,
    placed,
    bodies: placed.map((x) => x.body),
    /** the casters' piece capsules ({ B, top, r }), for the phone tier's baked road shadows */
    casterBodies: placed.filter((x) => x.cast).map((x) => ({ id: x.k.id, B: x.p, top: V3.add(x.p, V3.mul(x.Y, x.h)), r: x.rLat })),
    /** spinMul: the Gentle scroll share (0 holds them still). */
    update(dt, spinMul = 1) {
      if (!spinMul || !dt) return;
      for (const x of placed) x.ang += x.spin * dt * spinMul;
      for (const f of faces) f.uniforms.turn.value = (f.uniforms.turn.value + dt * spinMul * 0.6) % (Math.PI * 2);
      write();
    },
  };
}

/**
 * Seeded scatter of floating props (polish 3: a dense corridor the chase camera sees, colossiMath.js
 * placeScatter). Count per tier from LOOK.world / WORLD_DEFAULTS scatterByTier (the tier picked at boot;
 * a later governor step leaves the layout alone). Each piece wears its own saturated spectrum colour
 * (an instanced tint) with a bright rim. Cards, watches and discs face the oncoming camera and swing
 * about it; chess pieces and cabinets lean their top toward it and turn slowly; bubbles tumble.
 * Returns { object, count, placed, update(dt, spinMul) } or null with no props.
 */
export function buildScatter(art, track, seed, { pal = null, avoid = [], samples = null } = {}) {
  if (!art) return null;
  const W = worldLook();
  const kinds = (W.scatterKinds || LOOK.artScatterKinds).filter((k) => art.model(k.id) && art.row(k.id));
  if (!kinds.length) return null;
  const want = scatterCount(Q.name, W);
  // polish 4: the hero set first (eye level beside the road); the far scatter keeps clear of it
  let heroes = null;
  try { heroes = buildHeroes(art, track, seed, { pal, avoid, samples }); } catch (e) { console.warn('[glassroad/world] hero props failed: ' + (e && e.message)); }
  if (heroes) avoid = [...avoid, ...heroes.bodies];
  const sectionOf = (s) => { try { const sec = track.sections[track.sectionAt(s)]; return sec ? sec.palette : null; } catch { return null; } };
  const placed = placeScatter(track, kinds, (id) => art.row(id), seed,
    { want, avoid, W, samples: samples || sampleTrack(track, W.stepM), sectionOf });
  // huge things turn slowly
  for (const x of placed) x.spin *= W.scatterSpinMul;
  if (!placed.length) return null;

  // one InstancedMesh per mesh part of each kind; instance matrix = placement * part matrix; one tint
  // buffer per kind, read per instance by every part's material
  const group = new Group(), parts = [], C = new Color();
  if (heroes) group.add(heroes.object);
  for (const k of kinds) {
    const mine = placed.filter((x) => x.k === k);
    if (!mine.length) continue;
    const tints = new Float32Array(mine.length * 3);
    mine.forEach((x, i) => { C.set(x.colour); tints.set([C.r, C.g, C.b], i * 3); });
    const tintAttr = new InstancedBufferAttribute(tints, 3);
    const root = art.model(k.id).scene;
    root.updateMatrixWorld(true);
    root.traverse((m) => {
      if (!m.isMesh) return;
      let mat = m.material;
      if (pal && mat && !Array.isArray(mat)) mat = createSoftMaterial(mat, pal, { tint: instancedBufferAttribute(tintAttr, 'vec3') }).material;
      const im = new InstancedMesh(m.geometry, mat, mine.length);
      im.frustumCulled = false;
      group.add(im); parts.push({ im, local: m.matrixWorld.clone(), mine });
    });
  }
  const E = new Object3D(), BX = new Vector3(), BY = new Vector3(), BZ = new Vector3();
  const SB = { X: [0, 0, 0], Y: [0, 0, 0], Z: [0, 0, 0] };   // kept basis: place() runs per frame
  function place(x) {
    const b = scatterBasisInto(x, x.ang, W, SB);
    if (b) {
      M4.makeBasis(BX.set(b.X[0], b.X[1], b.X[2]), BY.set(b.Y[0], b.Y[1], b.Y[2]), BZ.set(b.Z[0], b.Z[1], b.Z[2]));
      E.quaternion.setFromRotationMatrix(M4);
    } else E.rotation.set(x.rot[0], x.rot[1], x.rot[2]);
    E.position.set(x.p[0], x.p[1], x.p[2]);
    E.scale.setScalar(x.scale);
    E.updateMatrix();
    x.m = (x.m || new Matrix4()).copy(E.matrix);
  }
  function write() {
    for (const x of placed) place(x);
    for (const { im, local, mine } of parts) {
      for (let i = 0; i < mine.length; i++) im.setMatrixAt(i, M4.multiplyMatrices(mine[i].m, local));
      im.instanceMatrix.needsUpdate = true;
    }
  }
  write();
  return {
    object: group,
    count: placed.length,
    placed,
    heroes,
    /** spinMul: the Gentle scroll share (0 holds them still). */
    update(dt, spinMul) {
      if (!spinMul) return;
      if (heroes) heroes.update(dt, spinMul);
      for (const x of placed) {
        x.ang += x.spin * dt * spinMul;
        if (x.pose === 'tumble') { x.rot[1] += x.spin * dt * spinMul; x.rot[0] += x.spin * 0.3 * dt * spinMul; }
      }
      write();
    },
  };
}
