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
 */
import {
  Group, Mesh, Sprite, SpriteNodeMaterial, MeshBasicNodeMaterial, CanvasTexture, SRGBColorSpace, Quaternion, Vector2, Vector3,
  Euler, Color,
} from 'three/webgpu';
import { texture, uv, uniform } from 'three/tsl';
import { clone as cloneSkinned } from 'three/addons/utils/SkeletonUtils.js';
import { S, RESPAWN } from '../sim/state.js';
import { LOOK } from './look.js';
import { Q } from './quality.js';
import { buildKart } from './dressing.js';
import { createKartMaterial, createKartSurface } from './materials/kart.js';
import {
  createPose, stepPose, tagAlpha, rivalLook, rivalNumbers, createReact, reactNumbers, observeKart, reactTo, stepReact, reactionsFor,
} from './kartPose.js';
import { createFaceCanvas, faceCell } from './faces.js';

const QX = new Quaternion(), QH = new Quaternion(), QI = new Quaternion(), EU = new Euler(), V = new Vector3(), AX = new Vector3();
const UPY = new Vector3(0, 1, 0), FZ = new Vector3(0, 0, 1), RX = new Vector3(1, 0, 0), GC = new Color();
const BUS_EVENTS = ['bump', 'wall', 'countdown', 'go', 'finish', 'item-hit', 'race-result', 'racer-event'];

/** art/palettes.json `karts` (seven colourways); null keeps the LOOK.kartTints fallback. */
const KART_TABLE = await fetch(new URL('../art/palettes.json', import.meta.url))
  .then((r) => (r.ok ? r.json() : null)).then((j) => (j && j.karts) || null).catch(() => null);

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
  s.scale.set(LOOK.tagSizeM[0], LOOK.tagSizeM[1], 1);
  s.position.y = LOOK.tagUpM; s.renderOrder = 4; s.visible = false;
  return s;
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
    m.colorNode = texture(atlas, uv().mul(0.25).add(cell)).rgb;
    screen = { material: m, set(id) { const c = faceCell(id); cell.value.set(c.col * 0.25, c.row * 0.25); }, dispose() { m.dispose(); } };
  } else {
    const fc = createFaceCanvas(document, { bg: LOOK.faceColour, fg });
    const t = new CanvasTexture(fc.canvas); t.colorSpace = SRGBColorSpace; t.flipY = false;
    const m = new MeshBasicNodeMaterial({ map: t });
    screen = { material: m, set(id, blink, time) { if (fc.set(id, blink, time)) t.needsUpdate = true; }, dispose() { t.dispose(); m.dispose(); } };
  }
  screen.set('neutral', false, 0);
  mesh.material = screen.material;
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

/** greyGeo: the greybox kart geometry for when the art set is missing. */
export function buildKarts(art, count, greyGeo) {
  const R = rivalNumbers(LOOK), RR = reactNumbers(LOOK), list = [];
  for (let i = 0; i < count; i++) {
    const look = rivalLook(i, KART_TABLE, LOOK);
    const root = new Group(), body = new Group();
    const art3d = buildKart(art);
    const model = art3d || new Mesh(greyGeo, createKartMaterial(look ? look.cup : [1, 1, 1]).material);
    const mats = [];
    model.traverse((o) => {
      if (!o.isMesh || !o.material || o.material.name !== 'kart_surface') return;
      o.material = createKartSurface(o.material, look, R).material;
      mats.push(o.material);
    });
    const accessory = art3d && look ? attachAccessory(art, model, look.accessory) : null;
    const face = art3d ? faceScreen(art, model, look ? look.hex : LOOK.emiColour) : null;
    const head = art3d ? headRig(model) : null;
    const bones = {};
    for (const n of ['ant0', 'ant1', 'ant2', 'shoulderL', 'shoulderR']) {
      const b = model.getObjectByName(n);
      if (b) bones[n] = { b, rest: b.quaternion.clone() };
    }
    body.add(model); root.add(body); root.visible = false;
    list.push({
      i, root, body, model, mats, bones, look, accessory, face, head, tint: look ? look.cup : [1, 1, 1], tagHex: look ? look.hex : '#fff4fa',
      tag: null, tagName: '', pose: createPose(), react: createReact(i), pend: [], mem: {}, ghost: 0,
    });
  }

  let particles = null;
  const offs = [];
  const queue = (i, kind, a) => {
    if (i === -1) for (const k of list) k.pend.push([kind, { ...a }]);
    else if (list[i]) list[i].pend.push([kind, a]);
  };
  /** Wire the bus reactions and the head sprays (scene.js calls this once after createParticles). */
  function attach({ bus = null, particles: pt = null } = {}) {
    if (pt) particles = pt;
    if (bus) for (const name of BUS_EVENTS) offs.push(bus.on(name, (p) => { for (const [i, kind, a] of reactionsFor(name, p)) queue(i, kind, a); }));
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

  function spray(k, kind, n, gentle) {
    const up = V.set(0, RR.sprayUpM, 0).applyQuaternion(k.root.quaternion).add(k.root.position), p = [up.x, up.y, up.z];
    const few = gentle ? 0.6 : 1, stars = kind === 'stars';
    particles.burst(p, Math.max(1, Math.round(n * few)), stars
      ? { family: 'golden', colours: RR.starColours, speed: RR.starSpeedMps, sizeM: RR.starSizeM, lifeS: 0.9, sparkle: 1, up: 1.2, g: 0.15 }
      : { family: 'melt', colours: RR.sweatColours, speed: RR.sweatSpeedMps, sizeM: RR.sweatSizeM, lifeS: 0.55, sparkle: 0, up: 1.8, g: 1.2 });
  }

  /**
   * One kart per frame. st = interpolated state, fxv = RacerFx or null, distM = distance to the camera,
   * opts: { squash (the player's fx.squash; the reaction layer now owns squash), gentle, topMps, name }.
   */
  function pose(k, st, fxv, dt, distM, { squash = null, gentle = false, topMps = 30, name = '' } = {}) {
    const { root, body, pose: p, react: r } = k;
    root.visible = true;
    root.position.set(st[S.posX], st[S.posY], st[S.posZ]);
    root.quaternion.set(st[S.quatX], st[S.quatY], st[S.quatZ], st[S.quatW]);
    stepPose(p, st, dt, LOOK, { topMps, gentle, squash, spinRad: fxv ? fxv.spinRad || 0 : 0 });
    for (const [kind, a] of observeKart(r, st, fxv)) reactTo(r, kind, a, RR, gentle);
    for (const [kind, a] of k.pend) { prep(k, kind, a, fxv); reactTo(r, kind, a, RR, gentle); }
    k.pend.length = 0;
    const o = stepReact(r, st, dt, RR, { gentle, lean: p.lean });
    body.position.set(0, o.lift, 0);
    body.quaternion.setFromAxisAngle(UPY, p.yawAdd + o.yaw)
      .multiply(QX.setFromAxisAngle(FZ, p.roll * o.stiff + o.roll)).multiply(QX.setFromAxisAngle(RX, p.pitch + o.pitch));
    body.scale.set(1 + o.sq / 2, 1 - o.sq, 1 + o.sq / 2);
    const near = k.i === 0 || distM <= Q.kartLodM;
    if (near) {
      // EMI frame: +Z up the antenna, Y forward; the sway rolls about Y, the arms lift about Y (mirrored)
      const arms = p.arms + (o.arms - p.arms) * o.armsK, ant = p.ant * o.stiff;
      turn(k.bones.ant0, UPY, -ant * 0.5); turn(k.bones.ant1, UPY, -ant * 0.3); turn(k.bones.ant2, UPY, -ant * 0.2);
      turn(k.bones.shoulderL, UPY, arms); turn(k.bones.shoulderR, UPY, -arms);
      if (k.head) poseHead(k.head, o);
      if (k.face) k.face.set(o.face, o.blink, r.t);
    }
    if (r.sprays.length) {
      if (particles && near) for (const [kind, n] of r.sprays) spray(k, kind, n, gentle);
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
  }

  function hide(k) { k.root.visible = false; k.pend.length = 0; }
  /** A fresh reaction state on every kart (Again, Restart, Menu): no cheer, sulk or daze carries over. */
  function resetReactions() { for (const k of list) { k.react = createReact(k.i); k.pend.length = 0; } }

  function dispose() {
    for (const off of offs) off();
    for (const k of list) {
      if (k.tag) { k.tag.material.map.dispose(); k.tag.material.dispose(); }
      for (const m of k.mats) m.dispose();
      if (k.face) k.face.dispose();
      if (k.accessory) k.accessory.traverse((o) => { if (o.isMesh && o.material) o.material.dispose(); });
    }
  }
  return { list, pose, hide, attach, resetReactions, dispose };
}
