/* render/camera.js - the chase camera (CONTRACT.md section 6). The law lives in cameraMath.js;
 * this only copies the rig into a three.js PerspectiveCamera.
 * B1: mode 'menu' is a slow attract orbit around the kart (Gentle slows it to gentle.scrollMul);
 * leaving the menu snaps the chase rig back behind the kart.
 */
import { PerspectiveCamera } from 'three/webgpu';
import { createCamRig, vFovForAspect } from './cameraMath.js';
import { S } from '../sim/state.js';
import { LOOK } from './look.js';

export function createChaseCamera(feel) {
  const camera = new PerspectiveCamera(feel.get('fov.baseVDeg'), 16 / 9, LOOK.nearM, LOOK.farM);
  const rig = createCamRig(feel);
  let lastMode = 'race', orbit = 0;
  function apply(aspect, eye = rig.eye, look = rig.look, up = rig.up, vFov = rig.vFov) {
    camera.position.set(eye[0], eye[1], eye[2]);
    camera.up.set(up[0], up[1], up[2]);
    camera.lookAt(look[0], look[1], look[2]);
    if (camera.fov !== vFov || camera.aspect !== aspect) {
      camera.fov = vFov; camera.aspect = aspect; camera.updateProjectionMatrix();
    }
  }
  const eye = [0, 0, 0], look = [0, 0, 0], UP = [0, 1, 0];
  function menu(kart, dt, aspect) {
    orbit += dt * LOOK.menuOrbitRadps * (feel.get('gentle.on') ? feel.get('gentle.scrollMul') : 1);
    const x = kart[S.posX], y = kart[S.posY], z = kart[S.posZ];
    eye[0] = x + Math.sin(orbit) * LOOK.menuOrbitM; eye[1] = y + LOOK.menuOrbitUpM; eye[2] = z + Math.cos(orbit) * LOOK.menuOrbitM;
    look[0] = x; look[1] = y + LOOK.menuLookUpM; look[2] = z;
    apply(aspect, eye, look, UP, vFovForAspect(feel.get('fov.baseVDeg'), aspect));
  }
  return {
    camera,
    rig,
    /** track is unused (no occlusion pull-in yet). mode: 'race' (chase), 'menu' (attract orbit), 'results' (chase). */
    update(kart, fx, track, dt, aspect, mode = 'race') {
      if (mode === 'menu') { lastMode = mode; menu(kart, dt, aspect); return; }
      if (lastMode === 'menu') { lastMode = mode; rig.snap(kart, aspect); apply(aspect); return; }
      lastMode = mode;
      rig.update(kart, fx, dt, aspect); apply(aspect);
    },
    snap(kart, track, aspect = camera.aspect) { rig.snap(kart, aspect); apply(aspect); },
  };
}
