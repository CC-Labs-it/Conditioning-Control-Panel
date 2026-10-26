/* render/camera.js - the chase camera (CONTRACT.md section 6). The law lives in cameraMath.js;
 * this only copies the rig into a three.js PerspectiveCamera.
 * B1: mode 'menu' is a slow attract orbit around the kart (Gentle slows it to gentle.scrollMul);
 * leaving the menu snaps the chase rig back behind the kart.
 * Polish 3: the rig's kick layer (cameraMath.js createKick). Landings and boost starts come from state;
 * the player's bumps and wall hits come from the bus when one is given (opts.bus, or attach(bus) later),
 * else from the rig's own velocity-jump detector.
 * Item feel: the player's item uses, landed and blocked hits and hits taken nudge the same springs (attach below).
 */
import { PerspectiveCamera } from 'three/webgpu';
import { createCamRig, vFovForAspect } from './cameraMath.js';
import { S } from '../sim/state.js';
import { LOOK } from './look.js';
import { createCameraViews } from './cameraViews.js';
import { fireKickKey, scaleKick } from '../fx/itemFeelMath.js';

/** OS reduced motion, read live (false without matchMedia). */
function reducedMotion() {
  try { return !!globalThis.matchMedia?.('(prefers-reduced-motion: reduce)')?.matches; } catch (e) { return false; }
}

export function createChaseCamera(feel, { bus = null } = {}) {
  const camera = new PerspectiveCamera(feel.get('fov.baseVDeg'), 16 / 9, LOOK.nearM, LOOK.farM);
  const rig = createCamRig(feel);
  const views = createCameraViews();
  const applyView = (kart, dt, aspect, options) => {
    const pose = views.update(rig, kart, dt, options);
    apply(aspect, pose.eye, pose.look, pose.up, pose.vFov);
  };
  let lastMode = 'race', orbit = 0, last = null;
  const offs = [];
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
  /** Which side of the player kart (last seen state) a world point is: +1 right, -1 left. */
  function sideOf(p) {
    if (!p || !last) return 1;
    const qx = last[S.quatX], qy = last[S.quatY], qz = last[S.quatZ], qw = last[S.quatW];
    // the kart's right (+X) axis from its quaternion
    const rx = 1 - 2 * (qy * qy + qz * qz), rz = 2 * (qx * qz - qw * qy);
    return (p[0] - last[S.posX]) * rx + (p[2] - last[S.posZ]) * rz >= 0 ? 1 : -1;
  }
  /** Wire the player's bumps and wall hits to the kick layer (the velocity detector stands down). */
  function attach(b) {
    if (!b || offs.length) return;
    rig.useVel = false;
    offs.push(b.on('bump', ({ a = -1, b: o = -1, dv = 0, p = null } = {}) => { if (a === 0 || o === 0) rig.hit('bump', dv, sideOf(p)); }));
    offs.push(b.on('wall', ({ dv = 0, side = 1 } = {}) => rig.hit('wall', dv, side)));
    // items from the thrower's seat (feel itemFeel.*KickM): a use nudges by its mode, a landed hit leans in,
    // a blocked one settles back, a hit on you jolts in; Gentle and OS reduced motion keep gentleKickMul of it
    const nudge = (key) => {
      const soft = !!feel.get('gentle.on') || reducedMotion();
      rig.kick.nudge(scaleKick(feel.get(key), soft ? feel.get('itemFeel.gentleKickMul') : 1));
    };
    offs.push(b.on('item-fire', ({ racer = -1, mode } = {}) => { if (racer === 0) nudge(fireKickKey(mode)); }));
    offs.push(b.on('item-hit', ({ victim = -1, by = -1, verdict } = {}) => {
      if (victim === 0 && verdict === 'hit') nudge('itemFeel.victimKickM');
      else if (by === 0 && victim !== 0) nudge(verdict === 'hit' ? 'itemFeel.confirmKickM' : 'itemFeel.blockedKickM');
    }));
  }
  if (bus) attach(bus);
  return {
    camera,
    rig,
    views,
    attach,
    /** track: its magnet stretches put the rig in road mode (the loop). mode: 'race' (chase), 'menu' (attract orbit), 'results' (chase).
     * viewOptions.viewDt: the drawn karts' clock step this frame (renderTime delta); the follow predicts with it. */
    update(kart, fx, track, dt, aspect, mode = 'race', lookInput = null, viewOptions = {}) {
      last = kart;
      if (mode === 'menu') { lastMode = mode; views.reset(); menu(kart, dt, aspect); return; }
      if (lastMode === 'menu') { rig.snap(kart, aspect, null, track); }
      lastMode = mode;
      rig.update(kart, fx, dt, aspect, track, mode === 'race' ? lookInput : null, viewOptions.viewDt ?? dt);
      applyView(kart, dt, aspect, { ...viewOptions, track, active: mode === 'race' && viewOptions.active !== false });
    },
    snap(kart, track, aspect = camera.aspect) { last = kart; views.reset(); rig.snap(kart, aspect, null, track); applyView(kart, 0, aspect, { track }); },
    dispose() { for (const off of offs) off(); offs.length = 0; },
  };
}
