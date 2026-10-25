import { Vector3, Quaternion } from 'three/webgpu';

/** Additive offsets on the accepted three-piece rig, restored before its next animation pose. */
export function createPhrasePieces(model) {
  const bindings = [['cup_body', 'cupM'], ['rider_mount', 'riderM'], ['saucer_body', 'saucerM']]
    .map(([name, key]) => ({ bone: model.getObjectByName(name), key }))
    .filter(b => b.bone?.parent).map(b => ({ ...b, pos: b.bone.position.clone() }));
  const up = new Vector3(), parent = new Quaternion(), world = new Quaternion();
  let applied = false;
  function beforePose() {
    if (!applied) return;
    for (const b of bindings) b.bone.position.copy(b.pos);
    applied = false;
  }
  function update(pose, enabled = true) {
    beforePose();
    if (!enabled || bindings.length !== 3 || !pose || !(pose.cupM || pose.riderM || pose.saucerM)) return;
    model.updateMatrixWorld(true); model.getWorldQuaternion(world);
    for (const b of bindings) {
      b.pos.copy(b.bone.position);
      b.bone.parent.getWorldQuaternion(parent);
      up.set(0, pose[b.key] || 0, 0).applyQuaternion(world).applyQuaternion(parent.invert());
      b.bone.position.add(up);
    }
    applied = true;
  }
  return { beforePose, update, get count() { return bindings.length; } };
}
