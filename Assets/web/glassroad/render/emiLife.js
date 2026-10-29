import { Quaternion, Euler } from 'three/webgpu';
import { createLifeState, stepLife, resetLife } from './emiLifeMath.js';

/** Call beforePose(), advance the AnimationMixer, then update(dt, signals), once per visual frame. */
export function createEmiLife(model, { seed = 0 } = {}) {
  const state = createLifeState(seed), names = ['EMI_root', 'ant0', 'ant1', 'ant2', 'ballpiv', 'shoulderL', 'shoulderR', 'footL', 'footR'];
  const bindings = names.map(name => model.getObjectByName(name)).filter(Boolean).map(bone => ({ bone, pos: bone.position.clone(), quat: bone.quaternion.clone() }));
  const byName = Object.fromEntries(bindings.map(b => [b.bone.name, b.bone]));
  const anchors = ['shoulderL', 'shoulderR', 'footL', 'footR'].filter(n => byName[n]).map(name => ({ bone: byName[name], pos: byName[name].position.clone(), quat: byName[name].quaternion.clone() }));
  const q = new Quaternion(), parentQ = new Quaternion(), frameQ = new Quaternion(), localQ = new Quaternion(), euler = new Euler();
  const modelFrame = new Quaternion(), cupFrame = new Quaternion();
  const cup = model.getObjectByName('cup_body') || model;
  model.updateMatrixWorld(true);
  // R4 joint rest axes differ from model XYZ. Remove the cup's rest frame before using its plane.
  model.getWorldQuaternion(modelFrame); cup.getWorldQuaternion(cupFrame);
  const cupRestInverse = modelFrame.clone().invert().multiply(cupFrame).invert();
  const metrics = { frames: 0, activeBones: bindings.length, maxAngle: 0, phase: 'rest', authority: 0, contactLocked: true, offsets: null };
  let applied = false, disposed = false;

  function beforePose() {
    if (!applied) return;
    for (const b of bindings) { b.bone.position.copy(b.pos); b.bone.quaternion.copy(b.quat); }
    applied = false;
  }
  function rotate(name, pitch, yaw, roll, frame) {
    const b = byName[name]; if (!b || (!pitch && !yaw && !roll)) return;
    // Bone local XYZ is not model XYZ. Conjugate a cup/model-space rotation through its current parent.
    b.parent.getWorldQuaternion(parentQ);
    q.setFromEuler(euler.set(pitch, yaw, roll, 'YXZ'));
    localQ.copy(parentQ).invert().multiply(frame).multiply(q).multiply(frameQ.copy(frame).invert()).multiply(parentQ);
    b.quaternion.premultiply(localQ).normalize(); b.updateMatrixWorld(true);
  }
  function update(dt, signals = {}) {
    if (disposed) return metrics;
    // Defensive removal also makes accidental repeated update calls non-accumulating.
    beforePose();
    const offsets = stepLife(state, dt, signals); metrics.frames++; metrics.phase = state.phase; metrics.authority = state.authority; metrics.offsets = offsets;
    metrics.maxAngle = 0;
    for (const key in offsets) metrics.maxAngle = Math.max(metrics.maxAngle, Math.abs(offsets[key]));
    if (signals.motionOff || signals.near === false) return metrics;
    for (const b of bindings) { b.pos.copy(b.bone.position); b.quat.copy(b.bone.quaternion); }
    model.updateMatrixWorld(true);
    // Preserve the mixer's actual palm/sole world poses while the case breathes and attends.
    for (const a of anchors) { a.bone.getWorldPosition(a.pos); a.bone.getWorldQuaternion(a.quat); }
    model.getWorldQuaternion(modelFrame);
    rotate('EMI_root', offsets.bodyPitch, offsets.bodyYaw, offsets.bodyRoll, modelFrame);
    for (const a of anchors) {
      a.bone.position.copy(a.pos); a.bone.parent.worldToLocal(a.bone.position);
      a.bone.parent.getWorldQuaternion(parentQ);
      a.bone.quaternion.copy(parentQ.invert()).multiply(a.quat); a.bone.updateMatrixWorld(true);
    }
    cup.getWorldQuaternion(cupFrame);
    cupFrame.multiply(cupRestInverse);
    rotate('shoulderL', 0, offsets.shoulder, 0, cupFrame);
    rotate('shoulderR', 0, -offsets.shoulder, 0, cupFrame);
    rotate('footL', 0, offsets.foot, 0, cupFrame);
    rotate('footR', 0, -offsets.foot, 0, cupFrame);
    rotate('ant0', offsets.antPitch * .48, 0, offsets.antYaw * .48, modelFrame);
    rotate('ant1', offsets.tipPitch * .34, 0, offsets.tipYaw * .34, modelFrame);
    rotate('ant2', offsets.tipPitch * .20, 0, offsets.tipYaw * .20, modelFrame);
    rotate('ballpiv', offsets.tipPitch * -.06, 0, offsets.tipYaw * -.06, modelFrame);
    // head_attach remains untouched and inherits the head motion, keeping every outfit attached.
    applied = true;
    return metrics;
  }
  function dispose() { beforePose(); resetLife(state); disposed = true; metrics.offsets = null; metrics.phase = 'rest'; }
  return { beforePose, update, dispose, metrics };
}
