/* render/camera.js - the chase camera (CONTRACT.md section 6). The law lives in cameraMath.js;
 * this only copies the rig into a three.js PerspectiveCamera.
 */
import { PerspectiveCamera } from 'three/webgpu';
import { createCamRig } from './cameraMath.js';
import { LOOK } from './look.js';

export function createChaseCamera(feel) {
  const camera = new PerspectiveCamera(feel.get('fov.baseVDeg'), 16 / 9, LOOK.nearM, LOOK.farM);
  const rig = createCamRig(feel);
  function apply(aspect) {
    camera.position.set(rig.eye[0], rig.eye[1], rig.eye[2]);
    camera.up.set(rig.up[0], rig.up[1], rig.up[2]);
    camera.lookAt(rig.look[0], rig.look[1], rig.look[2]);
    if (camera.fov !== rig.vFov || camera.aspect !== aspect) {
      camera.fov = rig.vFov; camera.aspect = aspect; camera.updateProjectionMatrix();
    }
  }
  return {
    camera,
    rig,
    /** track is unused in Phase A (no occlusion pull-in yet). */
    update(kart, fx, track, dt, aspect) { rig.update(kart, fx, dt, aspect); apply(aspect); },
    snap(kart, track, aspect = camera.aspect) { rig.snap(kart, aspect); apply(aspect); },
  };
}
