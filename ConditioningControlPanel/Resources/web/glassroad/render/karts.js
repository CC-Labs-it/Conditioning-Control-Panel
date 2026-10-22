/* render/karts.js - the eight karts (B1): EMI teacup clones (SkeletonUtils, through dressing.buildKart),
 * one tint per racer on the kart_surface colour (racer 0 stays porcelain white), the pose from
 * render/kartPose.js (lean, nose lift, squash, hit wobble, Shatter spin, antenna sway, arms up on a
 * boost), a ghost wash while a racer is ghosted (respawn grace), and a small name tag over rivals
 * within LOOK.tagNearM. Past Q.kartLodM a rival skips its bone pose and tag.
 * Hierarchy per kart: root (state position and rotation) > body (pose) > model.
 */
import { Group, Mesh, Sprite, SpriteNodeMaterial, CanvasTexture, SRGBColorSpace, Color, Quaternion, Vector3 } from 'three/webgpu';
import { S, RESPAWN } from '../sim/state.js';
import { LOOK } from './look.js';
import { Q } from './quality.js';
import { buildKart } from './dressing.js';
import { createKartMaterial } from './materials/kart.js';
import { createPose, stepPose, tintRgb, tagAlpha } from './kartPose.js';

const QX = new Quaternion(), AX = new Vector3(), UPY = new Vector3(0, 1, 0), FZ = new Vector3(0, 0, 1), RX = new Vector3(1, 0, 0), GC = new Color();

function tagSprite(name, tint) {
  const cv = document.createElement('canvas'); cv.width = 256; cv.height = 64;
  const g = cv.getContext('2d'), c = new Color().setRGB(tint[0], tint[1], tint[2]);
  g.fillStyle = 'rgba(20, 12, 32, 0.55)';
  g.beginPath(); g.roundRect(4, 8, 248, 48, 24); g.fill();
  g.fillStyle = '#' + c.getHexString();
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

/** greyGeo: the greybox kart geometry for when the art set is missing. */
export function buildKarts(art, count, greyGeo) {
  const list = [];
  for (let i = 0; i < count; i++) {
    const tint = tintRgb(i, LOOK);
    const root = new Group(), body = new Group();
    const model = buildKart(art) || new Mesh(greyGeo, createKartMaterial(tint).material);
    const mats = [];
    model.traverse((o) => {
      if (!o.isMesh || !o.material || o.material.name !== 'kart_surface') return;
      o.material = o.material.clone();
      o.material.color.setRGB(tint[0], tint[1], tint[2]);
      if (o.material.emissive) mats.push(o.material);
    });
    const bones = {};
    for (const n of ['ant0', 'ant1', 'ant2', 'shoulderL', 'shoulderR']) {
      const b = model.getObjectByName(n);
      if (b) bones[n] = { b, rest: b.quaternion.clone() };
    }
    body.add(model); root.add(body); root.visible = false;
    list.push({ i, root, body, model, mats, bones, tint, tag: null, tagName: '', pose: createPose(), mem: {}, ghost: 0 });
  }

  function setName(k, name) {
    if (k.i === 0 || !name || name === k.tagName) return;
    if (k.tag) { k.root.remove(k.tag); k.tag.material.map.dispose(); k.tag.material.dispose(); }
    k.tag = tagSprite(name, k.tint); k.tagName = name; k.root.add(k.tag);
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
    }
  }
  return { list, pose, hide, dispose };
}
