import { Quaternion, Vector3, Euler, PropertyBinding } from 'three/webgpu';
import { garageIdleAt } from './garageIdleMath.js';

/** Garage-only acting, layered after secondary life and removed before the next pose. */
export function createGarageIdle(model, clips = []) {
  const names = ['EMI_root', 'shoulderL', 'shoulderR', 'ant0', 'ant1', 'ant2', 'footL', 'footR'];
  const rows = names.map(n => model.getObjectByName(n)).filter(Boolean).map(bone => ({ bone,
    p: bone.position.clone(), q: bone.quaternion.clone(), s: bone.scale.clone() }));
  const root = model.getObjectByName('EMI_root');
  const feet = ['footL', 'footR'].map(n => model.getObjectByName(n)).filter(Boolean).map(bone => ({ bone, p: new Vector3(), q: new Quaternion(), s: new Vector3() }));
  const waveClip = clips.find(c => c.name === 'wave');
  const tracks = (waveClip?.tracks || []).flatMap(track => {
    const parsed = PropertyBinding.parseTrackName(track.name), bone = model.getObjectByName(parsed.nodeName);
    if (!bone || !names.includes(bone.name) || !['position', 'quaternion'].includes(parsed.propertyName)) return [];
    const sample = track.createInterpolant(), first = Array.from(sample.evaluate(0));
    return [{ bone, property: parsed.propertyName, sample, first }];
  });
  const frame = new Quaternion(), parent = new Quaternion(), inv = new Quaternion(), q = new Quaternion(), delta = new Quaternion();
  const e = new Euler(), point = new Vector3(), lift = new Vector3(), parentScale = new Vector3();
  let time = 0, applied = false;
  const metrics = { time: 0, name: 'rest', face: 'happy', offsets: null };
  function beforePose() {
    if (!applied) return;
    for (const r of rows) { r.bone.position.copy(r.p); r.bone.quaternion.copy(r.q); r.bone.scale.copy(r.s); }
    applied = false;
  }
  function turn(bone, pitch, yaw, roll) {
    if (!bone) return;
    bone.parent.getWorldQuaternion(parent);
    q.setFromEuler(e.set(pitch, yaw, roll, 'YXZ'));
    delta.copy(parent).invert().multiply(frame).multiply(q).multiply(inv.copy(frame).invert()).multiply(parent);
    bone.quaternion.premultiply(delta).normalize(); bone.updateMatrixWorld(true);
  }
  function reset() { beforePose(); time = 0; metrics.time = 0; metrics.name = 'rest'; metrics.face = 'happy'; metrics.offsets = null; }
  function update(dt, { blocked = false, gentle = false, motionOff = false } = {}) {
    beforePose();
    if (blocked || motionOff || !root) { reset(); return metrics; }
    time += Math.max(0, Math.min(.1, dt || 0));
    const o = garageIdleAt(time, { gentle });
    metrics.time = time; metrics.name = o.name; metrics.face = o.face; metrics.offsets = o;
    for (const r of rows) { r.p.copy(r.bone.position); r.q.copy(r.bone.quaternion); r.s.copy(r.bone.scale); }
    model.updateMatrixWorld(true); model.getWorldQuaternion(frame);
    for (const f of feet) { f.bone.getWorldPosition(f.p); f.bone.getWorldQuaternion(f.q); f.bone.getWorldScale(f.s); }
    // Use the authored greeting, retimed as one unhurried wave. Outfits inherit the same joints.
    if (o.wave > 0) for (const t of tracks) {
      const v = t.sample.evaluate(o.waveTime * waveClip.duration);
      if (t.property === 'quaternion') {
        q.fromArray(t.first).invert().multiply(delta.fromArray(v));
        delta.identity().slerp(q, o.wave); t.bone.quaternion.multiply(delta).normalize();
      } else {
        point.fromArray(v).sub(lift.fromArray(t.first)); t.bone.position.addScaledVector(point, o.wave);
      }
    }
    turn(root, o.pitch, o.yaw, o.roll);
    turn(model.getObjectByName('ant0'), o.antenna, 0, 0);
    // R4 is locally Z-up. Scaling the case gives the hop a small load and landing compression.
    root.scale.z *= 1 - o.squash; root.scale.x *= 1 + o.squash * .5; root.scale.y *= 1 + o.squash * .5;
    model.updateMatrixWorld(true);
    root.getWorldPosition(point); point.y += o.lift; root.parent.worldToLocal(point); root.position.copy(point);
    model.updateMatrixWorld(true);
    // Soles stay at their captured contacts throughout both grounded gestures and the hop load/landing.
    // In flight those same anchors travel straight up, preserving shoe orientation and spacing.
    for (const f of feet) {
      point.copy(f.p); point.y += o.lift; f.bone.parent.worldToLocal(point); f.bone.position.copy(point);
      f.bone.parent.getWorldQuaternion(parent); f.bone.quaternion.copy(parent.invert()).multiply(f.q);
      f.bone.parent.getWorldScale(parentScale); f.bone.scale.copy(f.s).divide(parentScale);
      f.bone.updateMatrixWorld(true);
    }
    applied = true;
    return metrics;
  }
  return { beforePose, update, reset, dispose: reset, metrics };
}
