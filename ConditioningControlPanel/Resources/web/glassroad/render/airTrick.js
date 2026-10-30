import { Vector3, Quaternion, PropertyBinding } from 'three/webgpu';

/**
 * EMI's air trick on the R4 rig (render/airTrickMath.js has the timeline): an additive lift (world up) and a
 * turn about the kart's up axis on rider_mount, a settle wobble, and one shoulder eased toward the cheer clip's
 * highest ("arms high") frame. Applied after the mixer, restored before its next pose (like phrasePieces.js).
 */
/** The cheer clip's highest frame for one shoulder: the key farthest from its rest turn ("arms high", about 0.6 s). */
function armsHigh(track, rest, dur) {
  const it = track.createInterpolant(), q = new Quaternion(), best = new Quaternion();
  let most = -1;
  for (let t = 0; t <= dur + 1e-6; t += 1 / 30) {
    const v = it.evaluate(t); q.set(v[0], v[1], v[2], v[3]).normalize();
    const a = q.angleTo(rest);
    if (a > most) { most = a; best.copy(q); }
  }
  return best;
}

export function createAirTrickPieces(model, clips = []) {
  const rider = model.getObjectByName('rider_mount');
  if (!rider || !rider.parent) return null;
  const cheer = clips.find((c) => c.name === 'cheer');
  const arms = {};
  for (const [key, name, sign] of [[1, 'shoulderR', -1], [-1, 'shoulderL', 1]]) {
    const bone = model.getObjectByName(name);
    if (!bone) continue;
    const tr = cheer && cheer.tracks.find((t) => t.name === PropertyBinding.sanitizeNodeName(name) + '.quaternion');
    let high;
    if (tr) high = armsHigh(tr, bone.quaternion, cheer.duration);
    // no clip: the procedural arms-up turn (render/karts.js turns the shoulders about their local Y)
    else high = bone.quaternion.clone().multiply(new Quaternion().setFromAxisAngle(new Vector3(0, 1, 0), sign * 1.5));
    arms[key] = { bone, high, quat: new Quaternion() };
  }
  const pos = new Vector3(), quat = new Quaternion(), world = new Quaternion(), parentInv = new Quaternion();
  const up = new Vector3(), lift = new Vector3(), fwd = new Vector3(), right = new Vector3(), q = new Quaternion();
  let applied = false, appliedArm = null;

  function beforePose() {
    if (!applied) return;
    rider.position.copy(pos); rider.quaternion.copy(quat);
    if (appliedArm) appliedArm.bone.quaternion.copy(appliedArm.quat);
    applied = false; appliedArm = null;
  }
  /** o = stepAirTrick output; enabled false only restores. */
  function update(o, enabled = true) {
    beforePose();
    if (!enabled || !o || !o.active) return;
    pos.copy(rider.position); quat.copy(rider.quaternion);
    model.updateMatrixWorld(true); model.getWorldQuaternion(world);
    rider.parent.getWorldQuaternion(parentInv).invert();
    // EMI pops straight up in the world (a kart diving off a kicker is pitched nose down, its own up points
    // ahead); she turns about her own up. The kart's up, back (+Z) and right (+X) in rider_mount's parent frame:
    lift.set(0, 1, 0).applyQuaternion(parentInv).normalize();
    up.set(0, 1, 0).applyQuaternion(world).applyQuaternion(parentInv).normalize();
    fwd.set(0, 0, 1).applyQuaternion(world).applyQuaternion(parentInv).normalize();
    right.set(1, 0, 0).applyQuaternion(world).applyQuaternion(parentInv).normalize();
    rider.position.addScaledVector(o.lift > 0 ? lift : up, o.lift);
    if (o.spin) rider.quaternion.premultiply(q.setFromAxisAngle(up, o.spin));
    if (o.wobRoll) rider.quaternion.premultiply(q.setFromAxisAngle(fwd, o.wobRoll));
    if (o.wobPitch) rider.quaternion.premultiply(q.setFromAxisAngle(right, o.wobPitch));
    const arm = o.armK > 0 ? arms[o.arm] || arms[-o.arm] : null;
    if (arm) { arm.quat.copy(arm.bone.quaternion); arm.bone.quaternion.slerp(arm.high, o.armK); }
    applied = true; appliedArm = arm;
    rider.updateMatrixWorld(true);
  }
  return { beforePose, update, hasArms: !!(arms[1] || arms[-1]) };
}
