/* tests/render-camera-loop.test.mjs - polish 6: the chase rig through a vertical 360 loop (road mode) and
 * unchanged on flat and banked road. The kart is driven round an analytic loop (R 15 m, along -Z). */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createFeel } from '../core/feel.js';
import { createState, S } from '../sim/state.js';
import { qfromBasis, qaxis } from '../core/math.js';
import { createCamRig, travelDir, loopTarget, LOOP_CAM } from '../render/cameraMath.js';

const feel = () => createFeel(JSON.parse(readFileSync(new URL('../feel.json', import.meta.url))));
const fx0 = () => ({ shake: [0, 0, 0], fovKick: 0, camPush: 0, orbit: 0 });
const DT = 1 / 60, R = 15, V = 20;
const dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const ang = (a, b) => Math.acos(Math.max(-1, Math.min(1, dot(a, b) / Math.hypot(...a) / Math.hypot(...b)))) * 180 / Math.PI;

/** Kart state at loop angle th (0 = bottom, entering along -Z); seg 1 is the loop (magnet), 0 the straight. */
function loopKart(k, th, inLoop) {
  const f = [0, Math.sin(th), -Math.cos(th)], u = [0, Math.cos(th), Math.sin(th)], r = [1, 0, 0];
  const q = qfromBasis([0, 0, 0, 1], r, u, [-f[0], -f[1], -f[2]]);
  k[S.quatX] = q[0]; k[S.quatY] = q[1]; k[S.quatZ] = q[2]; k[S.quatW] = q[3];
  k[S.posX] = 0; k[S.posY] = R * (1 - Math.cos(th)); k[S.posZ] = -R * Math.sin(th);
  k[S.velX] = 0; k[S.velY] = f[1] * V; k[S.velZ] = f[2] * V;
  k[S.speedPct] = V / 30; k[S.grounded] = 1; k[S.seg] = inLoop ? 1 : 0;
  return { f, u };
}
const track = { nodeFlags: (i) => (i === 1 ? ['rail', 'magnet'] : ['rail']) };

test('travelDir: flat = level forward, a loop keeps its heading all the way round', () => {
  const out = [0, 0, 0];
  assert.deepEqual(travelDir(out, [0, 0, -1], [0, 1, 0]).map((v) => Math.round(v * 1e6) / 1e6), [0, 0, -1]);
  for (let th = 0; th < 2 * Math.PI; th += 0.3) {
    const d = travelDir(out, [0, Math.sin(th), -Math.cos(th)], [0, Math.cos(th), Math.sin(th)]);
    assert.ok(Math.abs(d[2] + 1) < 1e-9 && Math.abs(d[0]) < 1e-9, 'th ' + th);
  }
  assert.equal(loopTarget(1, false), 0);
  assert.equal(loopTarget(0.9, false), 0);                  // a 25 deg bank stays level
  assert.equal(loopTarget(0.9, true), 1);
  assert.equal(loopTarget(0.2, false), 1);
});

test('loop: the camera follows the road up through the loop, up turns smoothly, no roll snap', () => {
  const f = feel(), rig = createCamRig(f), k = createState();
  loopKart(k, 0, false);
  rig.snap(k, 16 / 9, null, track);
  assert.equal(rig.loop, 0);
  let prevUp = rig.up.slice(), maxStep = 0, topUpY = 1, minLoop = 1;
  // 12 m of straight, the loop at V, then 12 m of straight
  const run = (n, at) => {
    for (let i = 0; i < n; i++) {
      const { u } = at(i);
      rig.update(k, fx0(), DT, 16 / 9, track);
      maxStep = Math.max(maxStep, ang(prevUp, rig.up));
      prevUp = rig.up.slice();
      for (const v of [...rig.eye, ...rig.look, ...rig.up]) assert.ok(Number.isFinite(v));
      // the eye stays behind the kart along its own up side, never under the road
      const e = [rig.eye[0] - k[S.posX], rig.eye[1] - k[S.posY], rig.eye[2] - k[S.posZ]];
      if (k[S.seg] === 1) assert.ok(dot(e, u) > -0.5, 'eye above the road plane at ' + i);
    }
  };
  const loopTicks = Math.round(2 * Math.PI * R / V / DT);
  run(20, () => loopKart(k, 0, false));
  run(loopTicks, (i) => {
    const th = 2 * Math.PI * i / loopTicks, r = loopKart(k, th, true);
    if (Math.abs(th - Math.PI) < 0.2) { topUpY = Math.min(topUpY, rig.up[1]); minLoop = Math.min(minLoop, rig.loop); }
    return r;
  });
  run(60, () => loopKart(k, 0, false));
  assert.ok(topUpY < -0.6, 'at the top the camera is upside down with the road, up.y ' + topUpY);
  assert.ok(minLoop > 0.95, 'road mode is full at the top ' + minLoop);
  // no roll snap: up never turns faster than the loop itself (360 deg over the loop) plus easing slack
  const loopDegPerTick = 360 / loopTicks;
  assert.ok(maxStep < loopDegPerTick * 2.2, 'max up step ' + maxStep.toFixed(2) + ' deg/tick vs loop ' + loopDegPerTick.toFixed(2));
  assert.ok(rig.loop < 0.2, 'eases back to level after the loop ' + rig.loop);
  assert.ok(Math.abs(rig.yaw) < 0.05, 'heading held through the loop: yaw ' + rig.yaw);
});

test('loop: at the loop the rig is level until the kart reaches the magnet stretch; banked road stays level', () => {
  const f = feel(), rig = createCamRig(f), k = createState();
  // a 25 deg bank, not magnet: up stays exactly world up
  const q = qaxis([0, 0, 0, 1], [0, 0, -1], 25 * Math.PI / 180);
  k[S.quatX] = q[0]; k[S.quatY] = q[1]; k[S.quatZ] = q[2]; k[S.quatW] = q[3]; k[S.seg] = 0;
  rig.snap(k, 16 / 9, null, track);
  for (let i = 0; i < 30; i++) rig.update(k, fx0(), DT, 16 / 9, track);
  assert.deepEqual(rig.up, [0, 1, 0]);
  assert.equal(rig.loop, 0);
  assert.ok(LOOP_CAM.pitchMaxDeg >= 30);
});

test('loop: effect shake cannot move the road-mode camera, including inverted sections', () => {
  const f = feel(), rig = createCamRig(f), k = createState();
  loopKart(k, Math.PI, true);
  rig.snap(k, 16 / 9, null, track);
  const e0 = rig.eye.slice(), look0 = rig.look.slice();
  rig.snap(k, 16 / 9, { shake: [0, 0.2, 0], fovKick: 0, camPush: 0, orbit: 0 }, track);
  assert.deepEqual(rig.eye, e0);
  assert.deepEqual(rig.look, look0);
});
