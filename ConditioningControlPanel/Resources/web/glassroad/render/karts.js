/* render/karts.js - the eight karts (B1, wave 2 art): EMI teacup clones (SkeletonUtils, through
 * dressing.buildKart). Racer 0 keeps EMI's own porcelain and no accessory. Rival i wears colourway
 * i - 1 of art/palettes.json `karts`: the cup and saucer tints ride the kart's _CUP_TINT / _SAUCER_TINT
 * masks (ART.md "Tint binding", pushed apart by kartPose.rivalLook so the seven read at speed), a
 * coloured rim glow, and one head accessory parented to head_attach. The pose comes from
 * render/kartPose.js (lean, nose lift, squash, hit wobble, Shatter spin, antenna sway, arms up on a
 * boost), a ghost wash while a racer is ghosted (respawn grace), and a small name tag over rivals
 * within LOOK.tagNearM. Past Q.kartLodM a rival skips its bone pose and tag.
 * Hierarchy per kart: root (state position and rotation) > body (pose) > model.
 */
import {
  Group, Mesh, Sprite, SpriteNodeMaterial, CanvasTexture, SRGBColorSpace, Quaternion, Vector3, Color,
  MeshPhysicalNodeMaterial,
} from 'three/webgpu';
import { attribute, texture, vec3, float, mix, pow, abs, materialColor, materialEmissive, normalView, positionViewDirection } from 'three/tsl';
import { clone as cloneSkinned } from 'three/addons/utils/SkeletonUtils.js';
import { S, RESPAWN } from '../sim/state.js';
import { LOOK } from './look.js';
import { Q } from './quality.js';
import { buildKart } from './dressing.js';
import { createKartMaterial } from './materials/kart.js';
import { createPose, stepPose, tagAlpha, rivalLook, rivalNumbers } from './kartPose.js';

const QX = new Quaternion(), AX = new Vector3(), UPY = new Vector3(0, 1, 0), FZ = new Vector3(0, 0, 1), RX = new Vector3(1, 0, 0), GC = new Color();

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

/**
 * A rival's kart_surface: the loader material copied into a node material the way the renderer's own
 * fromMaterial does, then the masked cup / saucer tint (weight = mask x base alpha, ART.md) and a
 * coloured view rim added to the emissive (the ghost wash still drives .emissive).
 */
function rivalSurface(src, look, R) {
  const m = new MeshPhysicalNodeMaterial();
  for (const key in src) m[key] = src[key];
  const keep = src.map ? texture(src.map).a : float(1);
  const tintBy = (name, mul) => mix(vec3(1, 1, 1), vec3(mul[0], mul[1], mul[2]), attribute(name, 'float').mul(keep));
  m.colorNode = materialColor.rgb.mul(tintBy('_cup_tint', look.cupMul)).mul(tintBy('_saucer_tint', look.saucerMul));
  m.opacityNode = float(1);
  const fres = pow(float(1).sub(abs(normalView.dot(positionViewDirection))), R.rimPow);
  m.emissiveNode = materialEmissive.add(vec3(look.rimLin[0], look.rimLin[1], look.rimLin[2]).mul(fres.mul(R.rimGain)));
  return m;
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

/** greyGeo: the greybox kart geometry for when the art set is missing. */
export function buildKarts(art, count, greyGeo) {
  const R = rivalNumbers(LOOK), list = [];
  for (let i = 0; i < count; i++) {
    const look = rivalLook(i, KART_TABLE, LOOK);
    const root = new Group(), body = new Group();
    const art3d = buildKart(art);
    const model = art3d || new Mesh(greyGeo, createKartMaterial(look ? look.cup : [1, 1, 1]).material);
    const mats = [];
    model.traverse((o) => {
      if (!o.isMesh || !o.material || o.material.name !== 'kart_surface') return;
      const own = o.material.clone();
      o.material = look ? rivalSurface(own, look, R) : own;
      if (o.material.emissive) mats.push(o.material);
    });
    const accessory = art3d && look ? attachAccessory(art, model, look.accessory) : null;
    const bones = {};
    for (const n of ['ant0', 'ant1', 'ant2', 'shoulderL', 'shoulderR']) {
      const b = model.getObjectByName(n);
      if (b) bones[n] = { b, rest: b.quaternion.clone() };
    }
    body.add(model); root.add(body); root.visible = false;
    list.push({
      i, root, body, model, mats, bones, look, accessory, tint: look ? look.cup : [1, 1, 1], tagHex: look ? look.hex : '#fff4fa',
      tag: null, tagName: '', pose: createPose(), mem: {}, ghost: 0,
    });
  }

  function setName(k, name) {
    if (k.i === 0 || !name || name === k.tagName) return;
    if (k.tag) { k.root.remove(k.tag); k.tag.material.map.dispose(); k.tag.material.dispose(); }
    k.tag = tagSprite(name, k.tagHex); k.tagName = name; k.root.add(k.tag);
  }

  const turn = (bone, axis, rad) => { if (bone) bone.b.quaternion.copy(bone.rest).multiply(QX.setFromAxisAngle(AX.copy(axis), rad)); };

  /**
   * One kart per frame. st = interpolated state, fxv = RacerFx or null, distM = distance to the camera,
   * opts: { squash (player fx.squash or null), gentle, topMps, name }.
   */
  function pose(k, st, fxv, dt, distM, { squash = null, gentle = false, topMps = 30, name = '' } = {}) {
    const { root, body, pose: p } = k;
    root.visible = true;
    root.position.set(st[S.posX], st[S.posY], st[S.posZ]);
    root.quaternion.set(st[S.quatX], st[S.quatY], st[S.quatZ], st[S.quatW]);
    stepPose(p, st, dt, LOOK, { topMps, gentle, squash, spinRad: fxv ? fxv.spinRad || 0 : 0 });
    body.quaternion.setFromAxisAngle(UPY, p.yawAdd)
      .multiply(QX.setFromAxisAngle(FZ, p.roll)).multiply(QX.setFromAxisAngle(RX, p.pitch));
    body.scale.set(1 + p.sq / 2, 1 - p.sq, 1 + p.sq / 2);
    const near = k.i === 0 || distM <= Q.kartLodM;
    if (near) {
      // EMI frame: +Z up the antenna, Y forward; the sway rolls about Y, the arms lift about Y (mirrored)
      turn(k.bones.ant0, UPY, -p.ant * 0.5); turn(k.bones.ant1, UPY, -p.ant * 0.3); turn(k.bones.ant2, UPY, -p.ant * 0.2);
      turn(k.bones.shoulderL, UPY, p.arms); turn(k.bones.shoulderR, UPY, -p.arms);
    }
    // ghost: a steady pale wash (no flicker), eased in and out
    const ghosted = (fxv && fxv.ghost) || st[S.respawnPhase] === RESPAWN.GRACE;
    k.ghost += ((ghosted ? 1 : 0) - k.ghost) * (1 - Math.exp(-dt / LOOK.ghostRiseS));
    if (k.mats.length) {
      GC.set(LOOK.ghostEmissive).multiplyScalar(k.ghost * LOOK.ghostGain);
      for (const m of k.mats) m.emissive.copy(GC);
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

  function hide(k) { k.root.visible = false; }

  function dispose() {
    for (const k of list) {
      if (k.tag) { k.tag.material.map.dispose(); k.tag.material.dispose(); }
      for (const m of k.mats) m.dispose();
      if (k.accessory) k.accessory.traverse((o) => { if (o.isMesh && o.material) o.material.dispose(); });
    }
  }
  return { list, pose, hide, dispose };
}
