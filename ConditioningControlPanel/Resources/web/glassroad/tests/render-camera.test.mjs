import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createFeel } from '../core/feel.js';
import { createState, S } from '../sim/state.js';
import { qaxis } from '../core/math.js';
import { createCamRig, vFovForAspect, yawOf } from '../render/cameraMath.js';

const feel = () => createFeel(JSON.parse(readFileSync(new URL('../feel.json', import.meta.url))));
const fx0 = () => ({ shake: [0, 0, 0], fovKick: 0, camPush: 0, orbit: 0 });
function kartAt(yaw = 0) {
  const k = createState();
  const q = qaxis([0, 0, 0, 1], [0, 1, 0], yaw);
  k[S.quatX] = q[0]; k[S.quatY] = q[1]; k[S.quatZ] = q[2]; k[S.quatW] = q[3];
  return k;
}
const close = (a, b, eps = 1e-6) => assert.ok(Math.abs(a - b) < eps, `${a} != ${b}`);

test('render: viewport law keeps the base fov at 16:9 and clamps a tall phone', () => {
  close(vFovForAspect(60, 16 / 9), 60);
  assert.ok(vFovForAspect(60, 4 / 3) > 60);
  assert.ok(vFovForAspect(60, 21 / 9) < 60);
  close(vFovForAspect(60, 390 / 844), 102);
});

test('render: snap puts the eye eyeBack behind and eyeUp above, looking lookAhead ahead', () => {
  const f = feel(), rig = createCamRig(f);
  rig.snap(kartAt(0), 16 / 9);
  close(rig.eye[2], f.get('camera.eyeBackM'));
  close(rig.eye[1], f.get('camera.eyeUpM'));
  close(rig.look[2], -f.get('camera.lookAheadM'));
  close(rig.look[1], f.get('camera.lookUpM'));
  close(rig.vFov, f.get('fov.baseVDeg'));
});

test('render: yaw follow never beats the near cap while the gap is under the far gap', () => {
  const f = feel(), rig = createCamRig(f), k = kartAt(0), dt = 1 / 60;
  rig.snap(k, 16 / 9);
  const turned = kartAt(-60 * Math.PI / 180); // 60 deg right, under the 70 deg far gap
  rig.update(turned, fx0(), dt, 16 / 9);
  const capRad = f.get('camera.yawCapDegps') * Math.PI / 180 * dt;
  assert.ok(Math.abs(rig.yaw) <= capRad + 1e-9);
  assert.ok(rig.yaw < 0, 'turns right');
});

test('render: a gap past the far gap uses the far cap', () => {
  const f = feel(), rig = createCamRig(f), dt = 1 / 60;
  rig.snap(kartAt(0), 16 / 9);
  rig.update(kartAt(-120 * Math.PI / 180), fx0(), dt, 16 / 9);
  const near = f.get('camera.yawCapDegps') * Math.PI / 180 * dt;
  const far = f.get('camera.yawCapFarDegps') * Math.PI / 180 * dt;
  assert.ok(Math.abs(rig.yaw) > near && Math.abs(rig.yaw) <= far + 1e-9);
});

test('render: yaw is frozen while stalled', () => {
  const f = feel(), rig = createCamRig(f);
  rig.snap(kartAt(0), 16 / 9);
  const k = kartAt(-1); k[S.stalled] = 0.5;
  for (let i = 0; i < 30; i++) rig.update(k, fx0(), 1 / 60, 16 / 9);
  close(rig.yaw, 0);
});

test('render: drift right biases the camera right, more on the outside line', () => {
  const f = feel(), dt = 1 / 60;
  const run = (lateral) => {
    const rig = createCamRig(f), k = kartAt(0);
    rig.snap(k, 16 / 9);
    k[S.driftDir] = 1; k[S.lateral] = lateral;
    for (let i = 0; i < 120; i++) rig.update(k, fx0(), dt, 16 / 9);
    return rig.bias;
  };
  const inside = run(2), outside = run(-2);
  close(inside, -f.get('camera.driftBiasDeg') * Math.PI / 180, 1e-3);
  close(outside, -f.get('camera.driftBiasOutDeg') * Math.PI / 180, 1e-3);
});

test('render: fov kick adds through the law and a big jump snaps the rig', () => {
  const f = feel(), rig = createCamRig(f), k = kartAt(0);
  rig.snap(k, 16 / 9);
  rig.update(k, { ...fx0(), fovKick: 8 }, 1 / 60, 16 / 9);
  close(rig.vFov, f.get('fov.baseVDeg') + 8);
  k[S.posX] = 500;
  rig.update(k, fx0(), 1 / 60, 16 / 9);
  close(rig.focus[0], 500);
});

test('render: yawOf reads -Z as zero and +X as minus a quarter turn', () => {
  close(yawOf([0, 0, -1]), 0);
  close(yawOf([1, 0, 0]), -Math.PI / 2);
});
