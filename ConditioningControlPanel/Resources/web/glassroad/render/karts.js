/* render/karts.js - the eight karts (B1, wave 2 art): EMI teacup clones (SkeletonUtils, through
 * dressing.buildKart). Racer 0 keeps EMI's own porcelain and no accessory. Rival i wears colourway
 * i - 1 of art/palettes.json `karts`: the cup and saucer tints ride the kart's _CUP_TINT / _SAUCER_TINT
 * masks (ART.md "Tint binding", pushed apart by kartPose.rivalLook so the seven read at speed), a
 * coloured rim glow, and one head accessory parented to head_attach. Every kart_surface is one shared
 * program with per-material uniforms (materials/kart.js createKartSurface). The pose comes from
 * render/kartPose.js (lean, nose lift, hit wobble, Shatter spin, antenna sway, arms up on a boost), a
 * ghost wash while a racer is ghosted (respawn grace), and a small name tag over rivals within
 * LOOK.tagNearM. Past Q.kartLodM a rival skips its bone pose, face redraws and tag.
 * Polish 2, the reaction layer (kartPose.js stepReact): squash and stretch, a saucer twist, tilts away
 * from hits, EMI's head on a spring (the EMI_root joint), per-effect moods, place faces, idle breath and
 * a finish cheer or sulk; EMI's face (render/faces.js) on every kart's emi_screen. Hop, land, drift,
 * boost, place and item effects are read from state; attach({ bus, particles }) adds bumps, walls, the
 * countdown glance, finishes and the head sprays (stars, sweat).
 * Hierarchy per kart: root (state position and rotation) > body (pose) > model.
 * Polish 3: the screen face draws larger and brighter (faces.js FACE_LOOK, gain feeds the bloom) and a head
 * mark (faces.js MARK_IDS: dizzy swirl, sweat drop, zz, ice, heart, spark, anger) pops over EMI's head on
 * a sprite (one shared atlas canvas), eased in and out by the reaction layer's markK.
 * Polish 4, items as events: an item hit spins, stomps or flips the kart (kartPose.js itemHit), bursts in the
 * kind's colour, sets stars orbiting EMI's head for the stun and glows the rim in the kind's hue (hue drifts,
 * lightness held; Gentle holds it still and halves it). A throw winds up and lunges. Surge leaves flat gold
 * chevrons behind the kart; Echo keeps its aura. Glass and Golden share one shell in items/visuals.js.
 * ART5, the R4 rig: one AnimationMixer per kart clone plays Astra's 26 clips, picked by kartPose.js
 * directorStep from the same edges and bus kinds the reactions hear (art/models/emi-kart-event-bindings.json).
 * The clips own every joint, so the procedural head, arm and antenna writes and the hop, land, bump, boost,
 * cheer and throw body motion stand down (react.rig); the steering bank (not under a lean clip), the polish 4
 * item hit turns, flips and hops, faces, marks, stars, rim and aura stay on top. Bots wear Astra's outfits and
 * ride the Petal / Trinket variants (render/wardrobe.js, one stable look per seat; setLook re-dresses a seat).
 */
import {
  Group, Mesh, Sprite, SpriteNodeMaterial, MeshBasicNodeMaterial, CanvasTexture, SRGBColorSpace, Quaternion, Vector2, Vector3,
  Euler, Color, SphereGeometry, BufferGeometry, Float32BufferAttribute, DoubleSide, AdditiveBlending, AnimationMixer, LoopOnce, LoopRepeat, PropertyBinding,
} from 'three/webgpu';
import {
  texture, uv, uniform, vec2, vec3, float, pow, abs, sin, atan, smoothstep, min, max, normalView, normalWorld, positionWorld, positionViewDirection, positionLocal,
} from 'three/tsl';
import { clone as cloneSkinned } from 'three/addons/utils/SkeletonUtils.js';
import { S, RESPAWN } from '../sim/state.js';
import { phraseBouncePhase } from '../sim/airWords.js';
import { createPhrasePieces } from './phrasePieces.js';
import { LOOK } from './look.js';
import { KART_VIEW } from './viewMath.js';
import { Q } from './quality.js';
import { createKartLods } from './phoneModels.js';
import { buildKart } from './dressing.js';
import { createGlideCap, glideAsset } from './glideCap.js';
import { createSwimPropeller, createPropellerFleet } from './swimPropeller.js';
import { coolantCaustics } from './mainframeMaterials.js';
import { objectUniform, objectState } from './objectUniform.js';
import { createKartMaterial, createKartSurface } from './materials/kart.js';
import {
  createPose, stepPose, tagAlpha, rivalLook, rivalNumbers, createReact, reactNumbers, observeKart, reactTo, stepReact, reactionsFor, phraseReboundPose,
  hitRimRgb, stunStar, kindHex, holdFace, rigTable, rigNumbers, createDirector, directorEvent, directorStep,
} from './kartPose.js';
import { dressKart, lookForSeat } from './wardrobe.js';
import { createEmiLife } from './emiLife.js';
import { createEmiSkin } from './emiSkin.js';
import { skinFromWire } from '../hud/garageState.js';
import { lookForTier } from './wardrobeMath.js';
import { HIT_LOOK, AURA_LOOK, ITEM_TIERS } from './items/look.js';
import { auraStyle } from './items/shieldMath.js';
import { createFaceCanvas, faceCell, FACE_LOOK, MARK_LOOK, markCell, createMarkAtlas } from './faces.js';

const QX = new Quaternion(), QH = new Quaternion(), QI = new Quaternion(), EU = new Euler(), V = new Vector3(), AX = new Vector3();
const UPY = new Vector3(0, 1, 0), FZ = new Vector3(0, 0, 1), RX = new Vector3(1, 0, 0), GC = new Color();
const BUS_EVENTS = ['bump', 'wall', 'countdown', 'go', 'finish', 'item-hit', 'item-fire', 'item-roll', 'race-result', 'racer-event'];
const RIM = [0, 0, 0], STAR = [0, 0, 0, 0];
// polish 6 VIEW: every kart root carries the look scale (render/viewMath.js); sprites read at range keep their size
const KS = KART_VIEW.scale, SPRITE_KEEP = KART_VIEW.keepSprites ? 1 / KS : 1;

// The stun ring, aura and head mark each share ONE material across every kart (a material per kart built
// the same shader graph eight times at load): k, rot, colour and the rest are read per drawn object from
// userData.fx (render/objectUniform.js), and written exactly like the old per-kart uniforms.
/** The stun ring material on the mark atlas's spark cell (k fades it, rot turns the sprites). */
function starMaterial(tex, cell) {
  const u = { k: objectUniform('k', 0), rot: objectUniform('rot', 0) };
  const m = new SpriteNodeMaterial({ transparent: true, depthWrite: false });
  const c = texture(tex, vec2(uv().x.add(cell.col).div(4), float(1 - cell.row).add(uv().y).div(2)));
  m.colorNode = c.rgb; m.opacityNode = c.a.mul(u.k); m.rotationNode = u.rot;
  return m;
}
/** One kart's stun ring: n sprites on the shared star material, one state for all of them. */
function starRing(material, n) {
  const sprites = [];
  for (let j = 0; j < n; j++) { const s = new Sprite(material); s.renderOrder = 5; s.visible = false; s.frustumCulled = false; sprites.push(s); }
  return { sprites, u: objectState(sprites, { k: 0, rot: 0 }), material };
}

/** A self item aura: a fresnel shell with bands climbing round it, additive; k fades it, t runs the bands. */
function auraMaterial() {
  const A = AURA_LOOK, u = { colour: objectUniform('colour', new Color(1, 1, 1)), k: objectUniform('k', 0), t: objectUniform('t', 0),
    bandHz: objectUniform('bandHz', A.bandClimbHz), bandPower: objectUniform('bandPower', 3), bandColour: objectUniform('bandColour', .55), bandAlpha: objectUniform('bandAlpha', .6) };
  const m = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, blending: AdditiveBlending });
  const fres = pow(float(1).sub(abs(normalView.dot(positionViewDirection))), A.fresPow);
  const p = positionLocal;
  const band = sin(p.y.mul(A.bands * Math.PI).sub(u.t.mul(u.bandHz).mul(2 * Math.PI)).add(atan(p.x, p.z).mul(A.bandTwist))).mul(0.5).add(0.5).pow(u.bandPower);
  const ends = smoothstep(-1, -0.55, p.y).mul(smoothstep(1, 0.5, p.y));
  m.colorNode = u.colour.mul(fres.mul(0.8).add(band.mul(u.bandColour)));
  m.opacityNode = min(fres.add(band.mul(u.bandAlpha)), 1).mul(ends).mul(u.k);
  return m;
}
function auraMesh(geo, material) {
  const A = AURA_LOOK, mesh = new Mesh(geo, material);
  mesh.scale.set(A.size[0], A.size[1], A.size[2]); mesh.position.y = A.upM; mesh.renderOrder = 2; mesh.visible = false;
  const u = objectState(mesh, { colour: new Color(1, 1, 1), k: 0, t: 0, bandHz: A.bandClimbHz, bandPower: 3, bandColour: .55, bandAlpha: .6 });
  return { mesh, u, material, emitAcc: 0, style: {} };
}

/** Three solid chevrons point forward along -Z. All twelve triangles lie below the saucer. */
function surgeGeometry() {
  const p = [];
  for (let i = 0; i < 3; i++) for (const side of [-1, 1]) {
    const z = 1.05 + i * .6, w = .8 - i * .12;
    p.push(0,.15,z, side*w,.15,z+.4, side*w,.15,z+.58,
      0,.15,z, side*w,.15,z+.58, 0,.15,z+.18);
  }
  return new BufferGeometry().setAttribute('position', new Float32BufferAttribute(p, 3));
}

/** art/palettes.json `karts` (seven colourways); null keeps the LOOK.kartTints fallback. */
const KART_TABLE = await fetch(new URL('../art/palettes.json', import.meta.url))
  .then((r) => (r.ok ? r.json() : null)).then((j) => (j && j.karts) || null).catch(() => null);

/** The R4 rig contract (art/models/emi-kart-event-bindings.json): null keeps the procedural pose. */
const RIG_T = await fetch(new URL('../art/models/emi-kart-event-bindings.json', import.meta.url))
  .then((r) => (r.ok ? r.json() : null)).then((j) => (j && j.actions ? rigTable(j) : null)).catch(() => null);

/**
 * A landing clip's rider separation over time ([[t, m]...]) from its rider_mount track, for landingStart:
 * the distance from the rest position in kart_root space (the joint's local axes are not the model's).
 */
function sepCurve(clip, rest) {
  const name = PropertyBinding.sanitizeNodeName('rider_mount') + '.position';
  const tr = clip && clip.tracks.find((t) => t.name === name);
  if (!tr) return null;
  const out = [];
  for (let k = 0; k < tr.times.length; k++) out.push([tr.times[k], V.set(tr.values[k * 3], tr.values[k * 3 + 1], tr.values[k * 3 + 2]).distanceTo(rest)]);
  return out;
}

/** The mixer and one action per bound clip on a kart clone; null without clips or the contract. */
function makeRig(gltf, model) {
  if (!RIG_T || !gltf || !gltf.animations || !gltf.animations.length) return null;
  const rider = model.getObjectByName('rider_mount');
  if (!rider) return null;
  const rest = rider.position.clone();
  for (const c of ['landing_squash', 'landing_heavy']) {
    if (RIG_T[c] && !RIG_T[c].sep) RIG_T[c].sep = sepCurve(gltf.animations.find((a) => a.name === c), rest);
  }
  const mixer = new AnimationMixer(model), actions = {};
  for (const clip of gltf.animations) {
    const e = RIG_T[clip.name];
    if (!e) continue;
    const a = mixer.clipAction(clip);
    if (e.loop) a.setLoop(LoopRepeat, Infinity);
    else { a.setLoop(LoopOnce, 1); a.clampWhenFinished = true; }
    actions[clip.name] = a;
  }
  return { mixer, actions, cur: null, rider, rest, bank: 1 };
}

/** Start a director change on the mixer: the old clip fades out (frozen for a landing: the captured pose), the new one fades in at `from`. */
function playRig(rig, ch) {
  const next = ch.clip ? rig.actions[ch.clip] || null : null, prev = rig.cur, fade = Math.max(ch.fade, 1e-3);
  if (prev && prev !== next) { if (ch.land) prev.timeScale = 0; prev.fadeOut(fade); }
  if (next) { next.reset(); next.timeScale = 1; next.time = ch.from || 0; next.fadeIn(fade); next.play(); }
  rig.cur = next;
}

function tagSprite(name, hex) {
  const cv = document.createElement('canvas'); cv.width = 256; cv.height = 64;
  const g = cv.getContext('2d');
  g.fillStyle = 'rgba(20, 12, 32, 0.55)';
  g.beginPath(); g.roundRect(4, 8, 248, 48, 24); g.fill();
  g.fillStyle = hex;
  g.beginPath(); g.arc(34, 32, 11, 0, Math.PI * 2); g.fill();
  g.fillStyle = '#fff4fa'; g.font = LOOK.tagFont; g.textBaseline = 'middle';
  g.fillText(name, 56, 33, 186);
  const t = new CanvasTexture(cv); t.colorSpace = SRGBColorSpace;
  const m = new SpriteNodeMaterial({ map: t, transparent: true, depthWrite: false });
  const s = new Sprite(m);
  s.scale.set(LOOK.tagSizeM[0] * SPRITE_KEEP, LOOK.tagSizeM[1] * SPRITE_KEEP, 1);
  s.position.y = LOOK.tagUpM; s.renderOrder = 4; s.visible = false;
  return s;
}

/** The head mark material on the shared mark atlas: cell picks the mark (row 0 on top), k fades and pops it, rot turns it. */
function markMaterial(tex) {
  const u = { cell: objectUniform('cell', new Vector2(0, 0)), k: objectUniform('k', 0), rot: objectUniform('rot', 0) };
  const m = new SpriteNodeMaterial({ transparent: true, depthWrite: false });
  // canvas texture with flipY: v = 1 is the canvas top, so row r spans v (1 - r - 1 .. 1 - r) / 2
  const c = texture(tex, vec2(u.cell.x.add(uv().x).div(4), u.cell.y.oneMinus().add(uv().y).div(2)));
  m.colorNode = c.rgb;
  m.opacityNode = c.a.mul(u.k);
  m.rotationNode = u.rot;
  return m;
}
function markSprite(material) {
  const s = new Sprite(material);
  s.renderOrder = 5; s.visible = false; s.frustumCulled = false;
  return { sprite: s, u: objectState(s, { cell: new Vector2(0, 0), k: 0, rot: 0 }), id: null };
}

/** The accessory scene for id, parented to the kart's head_attach at identity (ART.md "Attachment binding"). */
function attachAccessory(art, model, id) {
  const g = id && art && art.model ? art.model(id) : null, at = model.getObjectByName('head_attach');
  if (!g || !at) return null;
  const acc = cloneSkinned(g.scene);
  acc.position.set(0, 0, 0); acc.quaternion.identity(); acc.scale.set(1, 1, 1);
  acc.traverse((o) => { if (o.isMesh) { o.frustumCulled = false; if (o.material) o.material = o.material.clone(); } });
  at.add(acc);
  return acc;
}

/**
 * EMI's screen on the mesh dressing gave the emi_screen slot: the `emi-faces` atlas cell when that art
 * is loaded, else the procedural face canvas. glTF uvs put v = 0 at the top, so the canvas is not flipped.
 */
function faceScreen(art, model, fg) {
  let mesh = null;
  model.traverse((o) => { if (o.isMesh && o.material && (o.material.name === 'emi_screen' || (o.material.map && o.material.map.isCanvasTexture))) mesh = o; });
  if (!mesh) return null;
  const old = mesh.material, atlas = art && art.tex ? art.tex('emi-faces') : null;
  let screen;
  if (atlas) {
    const cell = uniform(new Vector2(0, 0)), m = new MeshBasicNodeMaterial();
    m.colorNode = texture(atlas, uv().mul(0.25).add(cell)).rgb.mul(FACE_LOOK.gain);
    screen = { material: m, set(id) { const c = faceCell(id); cell.value.set(c.col * 0.25, c.row * 0.25); }, dispose() { m.dispose(); } };
  } else {
    const fc = createFaceCanvas(document, { bg: LOOK.faceColour, fg });
    const t = new CanvasTexture(fc.canvas); t.colorSpace = SRGBColorSpace; t.flipY = false;
    const m = new MeshBasicNodeMaterial({ map: t });
    m.color.setScalar(FACE_LOOK.gain);   // brighter than paper white: the face glows into the bloom
    screen = { material: m, set(id, blink, time) { if (fc.set(id, blink, time)) t.needsUpdate = true; }, dispose() { t.dispose(); m.dispose(); } };
  }
  screen.set('neutral', false, 0);
  mesh.material = screen.material;
  mesh.material.name = 'emi_screen';
  if (old) { if (old.map) old.map.dispose(); old.dispose(); }
  return screen;
}

/** EMI_root (her whole bust, accessory and screen) with the frame change from the kart's model space. */
function headRig(model) {
  const b = model.getObjectByName('EMI_root');
  if (!b || !b.parent) return null;
  model.updateMatrixWorld(true);
  const P = b.parent.getWorldQuaternion(new Quaternion());   // the model is still its own root here
  return { b, rest: b.quaternion.clone(), pos: b.position.clone(), P, Pi: P.clone().invert() };
}

/** Head offsets are in the kart frame (yaw about +Y, pitch about +X, roll about +Z; bob up +Y). */
function poseHead(h, o) {
  QH.setFromEuler(EU.set(o.hPitch, o.hYaw, o.hRoll, 'YXZ'));
  h.b.quaternion.copy(h.Pi).multiply(QH).multiply(h.P).multiply(h.rest);
  h.b.position.copy(h.pos).add(V.set(0, o.hBob, 0).applyQuaternion(h.Pi));
}

/** The seed for the bot looks: opts.seed, else ?seed= in the page url, else 0. */
const urlSeed = () => { try { return new URLSearchParams(globalThis.location.search).get('seed') || 0; } catch { return 0; } };

/**
 * greyGeo: the greybox kart geometry for when the art set is missing. opts: { seed (the race seed: bot looks),
 * looks (per seat a wanted { outfit, vehicle }, e.g. a remote player's; undefined = the seat's own pick) }.
 */
export function buildKarts(art, count, greyGeo, { seed = urlSeed(), looks = [] } = {}) {
  const R = rivalNumbers(LOOK), RR = reactNumbers(LOOK), RN = rigNumbers(LOOK), list = [];
  const kartGltf = art && art.model ? art.model('emi-teacup-kart') : null;
  const lods = createKartLods(art?.model?.('emi-teacup-kart-lod1'));
  const auraGeo = new SphereGeometry(1, 32, 20);
  const surgeGeo = surgeGeometry();
  // perf: every kart's marine drive draws through one fleet (one instanced draw per part, not 12 per kart)
  const propellers = createPropellerFleet(count);
  // one head mark atlas for every kart (none without a document, e.g. under node)
  let markTex = null, markMat = null, starMat = null;
  if (typeof document !== 'undefined') {
    markTex = new CanvasTexture(createMarkAtlas(document)); markTex.colorSpace = SRGBColorSpace;
    markMat = markMaterial(markTex); starMat = starMaterial(markTex, markCell('spark'));
  }
  const auraMat = auraMaterial();
  for (let i = 0; i < count; i++) {
    const look = rivalLook(i, KART_TABLE, LOOK);
    const root = new Group(), body = new Group();
    root.scale.setScalar(KS);
    const art3d = buildKart(art);
    const model = art3d || new Mesh(greyGeo, createKartMaterial(look ? look.cup : [1, 1, 1]).material);
    const lod = lods.bind(model, i);
    const mats = [], rims = [];
    if (!art3d) mats.push(model.material);
    model.traverse((o) => {
      if (!o.isMesh || !o.material || o.material.name !== 'kart_surface') return;
      const surf = createKartSurface(o.material, look, R);
      o.material = surf.material;
      mats.push(o.material); rims.push(surf.uniforms.rim);
    });
    // ART5: the seat's outfit and vehicle (render/wardrobe.js); an outfit brings its own headpiece, so the
    // old head accessory only stays on a rival with no outfit
    const want = lookForTier(lookForSeat(i, seed, looks[i]), Q.name);
    const face = art3d ? faceScreen(art, model, look ? look.hex : LOOK.emiColour) : null;
    const skin = art3d ? createEmiSkin(model,{cloneMaterials:false}) : null;
    const dress = art3d ? dressKart(art, model, want, { tint: look ? look.cup : null, tintMix: LOOK.vehicleTintMix }) : null;
    const accessory = art3d && look && !(dress && dress.outfit) ? attachAccessory(art, model, look.accessory) : null;
    const glideCap = art3d ? createGlideCap(art, model) : null;
    const head = art3d ? headRig(model) : null;
    const rig = art3d ? makeRig(kartGltf, model) : null;
    const bones = {};
    for (const n of ['ant0', 'ant1', 'ant2', 'shoulderL', 'shoulderR']) {
      const b = model.getObjectByName(n);
      if (b) bones[n] = { b, rest: b.quaternion.clone() };
    }
    body.add(model); root.add(body); root.visible = false;
    const propeller = createSwimPropeller(body, art?.model?.(dress?.vehicle || 'emi-teacup-kart')?.scene, propellers);
    root.userData.swimPropeller = propeller.motion;
    const caustics={time:uniform(0),amount:uniform(0)};
    for(const material of mats) material.colorNode=material.colorNode.add(
      coolantCaustics(positionWorld.xz,caustics.time).mul(vec3(.08,.28,.36)).mul(max(normalWorld.y,.18)).mul(caustics.amount));
    const mark = markMat ? markSprite(markMat) : null;
    if (mark) root.add(mark.sprite);
    const stars = starMat ? starRing(starMat, RR.stunStars) : null;
    if (stars) for (const sp of stars.sprites) root.add(sp);
    const aura = auraMesh(auraGeo, auraMat);
    root.add(aura.mesh);
    const surgeMaterial = new MeshBasicNodeMaterial({ color: '#d7a64f', transparent: true, opacity: 0, depthWrite: false, side: DoubleSide });
    const surgeMesh = new Mesh(surgeGeo, surgeMaterial); surgeMesh.name = 'surge-chevrons'; surgeMesh.visible = false; root.add(surgeMesh);
    const surge = { mesh: surgeMesh, material: surgeMaterial, alpha: 0, time: 0 };
    list.push({
      i, root, body, model, lod, mats, bones, look, accessory, face, head, tint: look ? look.cup : [1, 1, 1], tagHex: look ? look.hex : '#fff4fa',
      dress, skin, rig, glideCap, propeller, caustics, phrasePieces: art3d ? createPhrasePieces(model) : null, phrasePose: {}, underwater:0, life: art3d ? createEmiLife(model, {seed:i}) : null, lifeSignals:{}, lifeAir:0, dir: createDirector(),
      tag: null, tagName: '', pose: createPose(), react: Object.assign(createReact(i), { rig: !!rig }), pend: [], mem: {}, ghost: 0, mark,
      rims, rimBase: look ? look.rimLin.slice() : [0, 0, 0], rimSet: false, stars, aura, surge,
    });
  }

  let particles = null;
  const offs = [];
  let binding = 0, disposed = false;
  const capSource = a => a?.model?.(glideAsset(a?.theme));
  let glideSource = capSource(art);
  const queue = (i, kind, a) => {
    if (i === -1) for (const k of list) k.pend.push([kind, { ...a }]);
    else if (list[i]) list[i].pend.push([kind, a]);
  };
  /** Wire the bus reactions and the head sprays (scene.js calls this once after createParticles). */
  function attach({ bus = null, particles: pt = null } = {}) {
    detach();
    const mine = binding;
    particles = pt;
    if (bus) for (const name of BUS_EVENTS) offs.push(bus.on(name, (p) => { for (const [i, kind, a] of reactionsFor(name, p)) queue(i, kind, a); }));
    // dev (?bot=1 only): the bus on the page so screenshot scripts can play an item hit or a fire
    if (bus && globalThis.location && /[?&]bot=1(?:&|$)/.test(globalThis.location.search)) { globalThis.__glassroadBus = bus; globalThis.__glassroadKarts = list; globalThis.__glassroadSetLook = setLook; }
    return () => { if (binding === mine) detach(); };
  }
  function detach() { binding++; for (const off of offs.splice(0)) off(); particles = null; }
  function rebind(next = {}) {
    art = next.art === undefined ? art : next.art;
    seed = next.seed === undefined ? seed : next.seed;
    const source = capSource(art);
    if (source !== glideSource) {
      for (const k of list) { k.glideCap?.dispose(); k.glideCap = createGlideCap(art, k.model); }
      glideSource = source;
    }
    resetReactions();
  }

  function setName(k, name) {
    if (k.i === 0 || !name || name === k.tagName) return;
    if (k.tag) { k.root.remove(k.tag); k.tag.material.map.dispose(); k.tag.material.dispose(); }
    k.tag = tagSprite(name, k.tagHex); k.tagName = name; k.root.add(k.tag);
  }

  const turn = (bone, axis, rad) => { if (bone) bone.b.quaternion.copy(bone.rest).multiply(QX.setFromAxisAngle(AX.copy(axis), rad)); };

  /** Bus args that need the kart: a bump point into kart-local x / z, a finish place from the standings. */
  function prep(k, kind, a, fxv) {
    if (kind === 'bump') {
      if (a.p) {
        V.set(a.p[0] - k.root.position.x, a.p[1] - k.root.position.y, a.p[2] - k.root.position.z).applyQuaternion(QI.copy(k.root.quaternion).invert());
        a.lx = V.x; a.lz = V.z;
      } else { a.lx = k.i & 1 ? 1 : -1; a.lz = 0; }
    } else if (kind === 'finish' && !a.place) {
      a.place = fxv ? fxv.place : 0;
      a.count = list.reduce((n, x) => n + (x.root.visible ? 1 : 0), 0);
    }
  }

  function spray(k, kind, n, gentle, item) {
    const few = gentle ? 0.6 : 1;
    if (kind === 'impact') {
      // the item lands: a wide burst in the kind's colour with a few pale sparkles, never a white core (the
      // player's own is half as many and smaller: it bursts right under the camera)
      const c = V.set(0, 0.7 * KS, 0).applyQuaternion(k.root.quaternion).add(k.root.position), H = HIT_LOOK, hex = kindHex(item), me = k.i === 0;
      particles.burst([c.x, c.y, c.z], Math.max(1, Math.round(n * few * (me ? H.burstPlayerMul : 1))), { colours: [hex, hex, '#fff4fa'],
        speed: H.burstSpeedMps * (gentle ? 0.7 : 1), sizeM: H.burstSizeM * (me ? H.burstPlayerMul : 1), lifeS: H.burstLifeS, sparkle: 0.6, petal: 0.2, up: H.burstUp, g: 0.4 });
      return;
    }
    const up = V.set(0, RR.sprayUpM * KS, 0).applyQuaternion(k.root.quaternion).add(k.root.position), p = [up.x, up.y, up.z];
    const stars = kind === 'stars', me = k.i === 0;
    // the player's own head sprays burst right under the camera: half as many (render/lensMath.js)
    particles.burst(p, Math.max(1, Math.round(n * few * (me ? 0.5 : 1))), stars
      ? { family: 'golden', colours: RR.starColours, speed: RR.starSpeedMps, sizeM: RR.starSizeM, lifeS: 0.9, sparkle: 1, up: 1.2, g: 0.15 }
      : { family: 'melt', colours: RR.sweatColours, speed: RR.sweatSpeedMps, sizeM: RR.sweatSizeM, lifeS: 0.55, sparkle: 0, up: 1.8, g: 1.2 });
  }

  /**
   * One kart per frame. st = interpolated state, fxv = RacerFx or null, distM = distance to the camera,
   * opts: { squash (the player's fx.squash; the reaction layer now owns squash), gentle, topMps, name }.
   */
  function pose(k, st, fxv, dt, distM, { squash = null, gentle = false, topMps = 30, name = '', glide = false, underwater = 0, paused = false, face = null, rollAdd = 0 } = {}) {
    k.lod?.update(distM);
    k.underwater=underwater;
    if(!paused){k.caustics.time.value+=dt*(gentle?.5:1);k.caustics.amount.value+=(underwater*(gentle?.5:1)-k.caustics.amount.value)*Math.min(1,dt/.35);}
    const { root, body, pose: p, react: r } = k;
    k.phrasePieces?.beforePose();
    k.life?.beforePose();
    root.visible = true;
    k.propeller.update(st[S.respawnPhase] === RESPAWN.NONE ? underwater : 0, dt, {gentle,paused});
    if (k.glideCap) k.glideCap.update(glide && st[S.airborne] > 0 && st[S.respawnPhase] === RESPAWN.NONE, dt, gentle);
    if (k.accessory) k.accessory.visible = !(k.dress && k.dress.outfit) && !(k.glideCap && k.glideCap.cap.visible);
    root.position.set(st[S.posX], st[S.posY], st[S.posZ]);
    root.quaternion.set(st[S.quatX], st[S.quatY], st[S.quatZ], st[S.quatW]);
    const rig = k.rig, spinOwned = rig && k.dir.cur === 'spin_out';
    stepPose(p, st, dt, LOOK, { topMps, gentle, squash: rig ? 0 : squash, spinRad: fxv && !spinOwned ? fxv.spinRad || 0 : 0 });
    const sep = rig ? rig.rider.position.distanceTo(rig.rest) : 0;
    for (const [kind, a] of observeKart(r, st, fxv)) { reactTo(r, kind, a, RR, gentle); if (rig) directorEvent(k.dir, kind, a, RIG_T, RN, sep); }
    for (const [kind, a] of k.pend) { prep(k, kind, a, fxv); reactTo(r, kind, a, RR, gentle); if (rig) directorEvent(k.dir, kind, a, RIG_T, RN, sep); }
    k.pend.length = 0;
    const o = stepReact(r, st, dt, RR, { gentle, lean: p.lean, fxv });
    if (rig) {
      // the clips own every joint; the body keeps the steering bank (not while a lean clip leans), the item
      // hit turns, flips and hops (polish 4) and the hit squash, layered over the rig
      const ch = directorStep(k.dir, st, dt, RIG_T, RN, gentle);
      if (ch) { playRig(rig, ch); if (ch.face) holdFace(r, ch.face, RIG_T[ch.clip].dur); }
      const leanClip = k.dir.cur === 'lean_left' || k.dir.cur === 'lean_right';
      rig.bank += ((leanClip ? 0 : 1) - rig.bank) * (1 - Math.exp(-dt / 0.15));
      rig.mixer.update(dt);
    }
    body.position.set(0, o.lift, 0);
    body.quaternion.setFromAxisAngle(UPY, p.yawAdd + o.yaw)
      .multiply(QX.setFromAxisAngle(FZ, p.roll * o.stiff * (rig ? rig.bank : 1) + o.roll + rollAdd)).multiply(QX.setFromAxisAngle(RX, (rig ? 0 : p.pitch) + o.pitch));
    const motionOff = typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
    const bounce = phraseReboundPose(motionOff ? null : phraseBouncePhase(st), gentle, k.phrasePose);
    // The brief body spring reads on every vehicle; only the accepted cup stack separates.
    const phraseSq = bounce.squash;
    const bodySq = phraseSq ? Math.max(-.25, Math.min(.35, o.sq + phraseSq)) : o.sq;
    body.scale.set(1 + bodySq / 2, 1 - bodySq, 1 + bodySq / 2);
    const near = k.i === 0 || distM <= Q.kartLodM;
    if (near && rig) { if (k.face) k.face.set(face || o.face, o.blink, r.t); }
    else if (near) {
      // EMI frame: +Z up the antenna, Y forward; the sway rolls about Y, the arms lift about Y (mirrored)
      const arms = p.arms + (o.arms - p.arms) * o.armsK, ant = p.ant * o.stiff;
      turn(k.bones.ant0, UPY, -ant * 0.5); turn(k.bones.ant1, UPY, -ant * 0.3); turn(k.bones.ant2, UPY, -ant * 0.2);
      turn(k.bones.shoulderL, UPY, arms); turn(k.bones.shoulderR, UPY, -arms);
      if (k.head) poseHead(k.head, o);
      if (k.face) k.face.set(face || o.face, o.blink, r.t);
    }
    const signals=k.lifeSignals;
    signals.time=r.t; signals.speed=Math.min(1,Math.max(0,st[S.speedPct])); signals.steer=st[S.steerSlew];
    signals.boost=st[S.boostKind]>0?1:0; signals.air=st[S.airborne]; signals.land=k.lifeAir&&!signals.air?1:0;
    signals.hit=o.stunK||0; signals.gentle=gentle; signals.motionOff=typeof matchMedia==='function'&&matchMedia('(prefers-reduced-motion: reduce)').matches;
    signals.near=near; signals.clip=k.dir.cur; signals.outfit=k.dress?.outfit||''; k.lifeAir=signals.air;
    k.life?.update(dt,signals); k.dress?.life?.update(dt,signals);
    k.phrasePieces?.update(bounce, !k.dress?.vehicle && !motionOff);
    if (r.sprays.length) {
      if (particles && near) for (const [kind, n, item] of r.sprays) spray(k, kind, n, gentle, item);
      r.sprays.length = 0;
    }
    // ghost: a steady pale wash (no flicker), eased in and out
    const ghosted = (fxv && fxv.ghost) || st[S.respawnPhase] === RESPAWN.GRACE;
    k.ghost += ((ghosted ? 1 : 0) - k.ghost) * (1 - Math.exp(-dt / LOOK.ghostRiseS));
    if (k.mats.length) {
      GC.set(LOOK.ghostEmissive).multiplyScalar(k.ghost * LOOK.ghostGain);
      for (const m of k.mats) if (m.emissive) m.emissive.copy(GC);
    }
    setName(k, name);
    if (k.tag) {
      // a kart right beside the player needs no tag (it filled the view on the grid): fade it in over tagHideNearM
      const [h0, h1] = LOOK.tagHideNearM || [0, 0], hn = h1 > h0 ? Math.min(1, Math.max(0, (distM - h0) / (h1 - h0))) : 1;
      const a = near ? tagAlpha(distM, LOOK) * hn * hn * (3 - 2 * hn) : 0;
      k.tag.visible = a > 0.01;
      k.tag.material.opacity = a;
    }
    if (k.mark) showMark(k, o, near, gentle);
    showHit(k, o, near, gentle);
    showAura(k, o, near, gentle, paused ? 0 : dt);
  }

  /** The stun stars orbit EMI's head; the rim glows the kind's hue (lightness held) while stunned. */
  function showHit(k, o, near, gentle) {
    const t = k.react.t;
    if (k.stars) {
      const on = near && o.stunK > 0.01;
      for (let j = 0; j < k.stars.sprites.length; j++) {
        const sp = k.stars.sprites[j];
        sp.visible = on;
        if (!on) continue;
        stunStar(j, k.stars.sprites.length, t, o.stunK, gentle, RR, STAR);
        sp.position.set(STAR[0], STAR[1] + o.lift, STAR[2]);
        sp.scale.setScalar(STAR[3]);
      }
      if (on) { k.stars.u.k.value = o.stunK; k.stars.u.rot.value = -t * 2.5 * (gentle ? 0.5 : 1); }
    }
    if (!k.rims.length) return;
    if (o.rimK > 0) {
      hitRimRgb(o.hitKind, t, gentle, o.rimK, k.rimBase, RR, RIM);
      for (const u of k.rims) u.value.set(RIM[0], RIM[1], RIM[2]);
      k.rimSet = true;
    } else if (k.rimSet) {
      for (const u of k.rims) u.value.set(k.rimBase[0], k.rimBase[1], k.rimBase[2]);
      k.rimSet = false;
    }
  }

  /** A self item's aura: the shell fades with auraK; motes rise off it (fewer on lower tiers and under Gentle). */
  function showAura(k, o, near, gentle, dt) {
    const s = k.surge, target = o.aura === 'surge' ? o.auraK : 0;
    s.alpha += Math.max(-dt / .3, Math.min(dt / .3, target - s.alpha));
    const still = gentle || (typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches);
    if (!still) s.time += dt;
    s.mesh.position.z = still ? 0 : Math.sin(s.time * 7) * .08;
    s.mesh.visible = s.alpha > .005; s.material.opacity = s.alpha * .85;
    if (o.aura === 'surge') { k.aura.mesh.visible = false; k.aura.emitAcc = 0; return; }
    const a = k.aura, A = AURA_LOOK, style = auraStyle(o.aura, k.i === 0, gentle, a.style), on = style.visible && o.auraK > 0.01;
    a.mesh.visible = on;
    if (!on) { a.emitAcc = 0; return; }
    const slow = gentle ? A.gentle : 1;
    a.u.colour.value.set(A.colours[o.aura] || '#ffffff');
    a.u.k.value = o.auraK * style.alpha * slow;
    a.u.bandHz.value = style.bandHz; a.u.bandPower.value = style.bandPower;
    a.u.bandColour.value = style.bandColour; a.u.bandAlpha.value = style.bandAlpha;
    a.u.t.value = k.react.t * slow;
    if (!particles || !near) return;
    const tier = ITEM_TIERS[Q.name] || ITEM_TIERS.high, share = tier.motes / ITEM_TIERS.high.motes;
    a.emitAcc += dt * A.motesPerS * share * slow * o.auraK * (k.i === 0 ? 0.5 : 1);   // the player's own: half, under the camera
    const n = Math.floor(a.emitAcc);
    if (n < 1) return;
    a.emitAcc -= n;
    const c = V.set(0, A.upM * 0.6 * KS, 0).applyQuaternion(k.root.quaternion).add(k.root.position);
    particles.burst([c.x, c.y, c.z], n, { colours: [A.colours[o.aura] || '#ffffff', '#ffffff'], speed: A.moteSpeedMps * 0.6, sizeM: A.moteSizeM,
      lifeS: A.moteLifeS, sparkle: 0.7, up: A.moteSpeedMps, g: -0.4 });
  }

  /** The head mark: pops in (scale from popIn to 1 with markK), bobs, the swirl turns; above the name tag when one shows. */
  function showMark(k, o, near, gentle) {
    const mk = k.mark, M = MARK_LOOK, c = near && o.mark && o.markK > 0.01 ? markCell(o.mark) : null;
    mk.sprite.visible = !!c;
    if (!c) return;
    if (mk.id !== o.mark) { mk.id = o.mark; mk.u.cell.value.set(c.col, c.row); }
    const t = k.react.t, slow = gentle ? 0.5 : 1, kk = o.markK;
    const side = M.sided.includes(o.mark) ? M.sideM : 0, up = M.upM + (k.tag && k.tag.visible ? LOOK.tagSizeM[1] * SPRITE_KEEP + 0.2 : 0);
    mk.sprite.position.set(side, up + o.lift + Math.sin(t * 6.283 * M.bobHz * slow) * M.bobM * slow, 0);
    mk.sprite.scale.setScalar(M.sizeM * SPRITE_KEEP * (M.popIn + (1 - M.popIn) * kk));
    mk.u.k.value = kk;
    mk.u.rot.value = o.mark === 'swirl' ? -t * M.spinRadps * slow : Math.sin(t * 6.283 * M.wobbleHz * slow) * M.wobbleRad * slow;
  }

  function hide(k) { k.root.visible = false; k.pend.length = 0; }
  /** A fresh reaction state on every kart (Again, Restart, Menu): no cheer, sulk or daze carries over. */
  function resetReactions() {
    for (const k of list) {
      k.lod?.reset();
      k.phrasePieces?.beforePose();
      k.react = Object.assign(createReact(k.i), { rig: !!k.rig }); k.pend.length = 0; k.dir = createDirector();
      if (k.glideCap) k.glideCap.reset();
      k.propeller.reset();
      k.surge.alpha = k.surge.time = k.surge.material.opacity = 0; k.surge.mesh.visible = false;
      k.underwater=0;k.caustics.amount.value=k.caustics.time.value=0;
      if (k.rig) { k.rig.mixer.stopAllAction(); k.rig.cur = null; }
    }
  }

  function dispose() {
    if (disposed) return; disposed = true;
    lods.dispose();
    detach();
    for (const k of list) {
      k.phrasePieces?.beforePose();
      if (k.tag) { k.tag.material.map.dispose(); k.tag.material.dispose(); }
      for (const m of k.mats) m.dispose();
      if (k.face) k.face.dispose();
      k.surge.material.dispose();
      if (k.accessory) k.accessory.traverse((o) => { if (o.isMesh && o.material) o.material.dispose(); });
      if (k.life) k.life.dispose();
      if (k.glideCap) k.glideCap.dispose();
      k.propeller.dispose();
      if (k.dress) k.dress.dispose();
      if (k.skin) k.skin.dispose();
      if (k.rig) { k.rig.mixer.stopAllAction(); k.rig.mixer.uncacheRoot(k.model); }
    }
    if (markTex) markTex.dispose();
    if (markMat) markMat.dispose();
    if (starMat) starMat.dispose();
    auraMat.dispose();
    auraGeo.dispose();
    surgeGeo.dispose();
    propellers.dispose();
    if (!kartGltf) greyGeo?.dispose();
  }
  /** Re-dress seat i (multiplayer: a remote player's { outfit, vehicle } ids; an unknown id gets the default look). */
  function setLook(i, want) {
    const k = list[i];
    if (!k || !k.dress) return null;
    k.phrasePieces?.beforePose();
    const picked=lookForSeat(i,seed,want);
    if ((k.dress.outfit || null) === (picked.outfit || null) && (k.dress.vehicle || null) === (picked.vehicle || null)) {
      if(k.skin)k.skin.set(skinFromWire(want?.skin));
      return lookOf(k);
    }
    k.dress.dispose();
    k.dress = dressKart(art, k.model, i===0&&want?picked:lookForTier(picked,Q.name), { tint: k.look ? k.look.cup : null, tintMix: LOOK.vehicleTintMix });
    k.propeller.fit(art?.model?.(k.dress?.vehicle || 'emi-teacup-kart')?.scene);
    if(k.skin)k.skin.set(skinFromWire(want?.skin));
    if (k.accessory) k.accessory.visible = !k.dress.outfit;
    return lookOf(k);
  }
  const lookOf = (k) => ({ outfit: k.dress ? k.dress.outfit : null, vehicle: k.dress ? k.dress.vehicle : null });

  return { list, propellers, pose, hide, attach, rebind, resetReactions, setLook, looks: () => list.map(lookOf), dispose };
}
