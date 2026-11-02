// Speed wave 2026-10-28 (lane B): the view widens a little at speed and the screen edges blur a little; both grow
// with speed and boost, ease in (~0.35 s) and out (~0.6 s), honour Motion less (half) / off (none) and Gentle (half),
// and never touch the rig's pose or vFov (the camera copy wears the widen; FxState carries the settled share).
import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { SPEED_FEEL_DEFAULTS, speedFeelNumbers, speedShare, speedFeelMul, speedFov, createSpeedSettle, createCamRig } from '../render/cameraMath.js';
import { createBus } from '../core/events.js';
import { createFx } from '../fx/reactions.js';
import { createGentle } from '../fx/gentle.js';
import { createPhotosafe } from '../fx/photosafe.js';
import { fakeTrack, makeFeel, place } from './sim-fake.mjs';
import { S } from '../sim/state.js';

const hooks = registerHooks({ resolve(name, context, next) {
  return name === 'three/webgpu' ? { url: new URL('../vendor/three/build/three.webgpu.min.js', import.meta.url).href, shortCircuit: true } : next(name, context);
} });
const { createChaseCamera } = await import('../render/camera.js');
hooks.deregister();

const L = SPEED_FEEL_DEFAULTS;
const close = (a, b, eps = 1e-6, msg = '') => assert.ok(Math.abs(a - b) <= eps, `${msg} ${a} vs ${b}`);

test('speedShare: 0 at cruise, eased to 1 at top speed, up to boostDeg/topDeg at maxPct', () => {
  assert.equal(speedShare(0), 0); assert.equal(speedShare(L.fromPct), 0); assert.equal(speedShare(0.5), 0);
  close(speedShare((L.fromPct + 1) / 2), 0.5, 1e-9, 'halfway eases to one half');
  assert.ok(speedShare(0.8) > 0 && speedShare(0.8) < speedShare(0.9) && speedShare(0.9) < 1, 'monotone between cruise and top');
  assert.equal(speedShare(1), 1);
  close(speedShare(1.3, 1.3), L.boostDeg / L.topDeg, 1e-9, 'maxPct reaches the boost cap');
  close(speedShare(1.15, 1.3), 1 + (L.boostDeg / L.topDeg - 1) / 2, 1e-9, 'half the boost band is half the extra');
  assert.equal(speedShare(2, 1.3), L.boostDeg / L.topDeg, 'never past the cap');
  assert.equal(speedShare(1.3, 1), 1, 'no boost band without a maxPct past 1');
  assert.equal(speedShare(NaN), 0); assert.equal(speedShare(undefined), 0);
});

test('speedFov: +7 deg at top, +10 under boost, half under less or Gentle, none under off', () => {
  assert.equal(speedFov(0), 0);
  assert.equal(speedFov(1), 7); assert.equal(speedFov(L.boostDeg / L.topDeg), 10); assert.equal(speedFov(5), 10, 'capped');
  assert.equal(speedFov(1, 'less'), 3.5); assert.equal(speedFov(1, 'full', true), 3.5); assert.equal(speedFov(1, 'less', true), 3.5);
  assert.equal(speedFov(1, 'off'), 0); assert.equal(speedFov(1, 'off', true), 0);
  assert.equal(speedFov(-1), 0); assert.equal(speedFov(NaN), 0);
  assert.deepEqual([speedFeelMul('full'), speedFeelMul('less'), speedFeelMul('off'), speedFeelMul('full', true), speedFeelMul('??')], [1, 0.5, 0, 0.5, 1]);
  const over = speedFeelNumbers({ speedFeel: { topDeg: 4, boostDeg: 6 } });
  assert.equal(speedFov(1, 'full', false, over), 4); assert.equal(speedFov(1.5, 'full', false, over), 6);
  assert.equal(speedFeelNumbers(null).topDeg, 7);
});

test('settle: critically damped, ~95% of a step in riseS up and releaseS down, no overshoot, frame rate independent', () => {
  for (const dt of [1 / 60, 1 / 20, 1 / 144]) {
    const s = createSpeedSettle();
    let t = 0, at35 = 0, peak = 0;
    while (t < 1.5) { s.update(1, dt); t += dt; peak = Math.max(peak, s.value); if (t <= L.riseS + 1e-9) at35 = s.value; }
    assert.ok(at35 > 0.9 && at35 < 0.995, `rise reaches ~95% at riseS (${at35}) at dt ${dt}`);
    assert.ok(peak <= 1 + 1e-9, 'no overshoot');
    close(s.value, 1, 1e-3, 'settles on the target');
    let at6 = 1; t = 0;
    while (t < 1.5) { s.update(0, dt); t += dt; if (t <= L.releaseS + 1e-9) at6 = s.value; }
    assert.ok(at6 < 0.1 && at6 > 0.005, `release leaves ~5% at releaseS (${at6}) at dt ${dt}`);
    assert.ok(s.value >= 0, 'never below rest');
    assert.equal(s.update(0.5, 0), s.value, 'dt 0 holds');
    s.reset(); assert.equal(s.value, 0);
  }
});

test('FxState.speedFeel / speedBlur follow the kart speed through the settle with the motion share', () => {
  const feel = makeFeel(), track = fakeTrack(), bus = createBus();
  const fx = createFx(bus, feel, { gentle: createGentle(feel, { search: '' }), photosafe: createPhotosafe(feel) });
  try {
    const run = (pct, seconds, dt = 1 / 60) => { const k = place(track, { s: 50, speed: 30 }); k[S.speedPct] = pct; for (let t = 0; t < seconds; t += dt) fx.update(dt, k); return fx.state; };
    assert.equal(run(0.5, 1).speedFeel, 0, 'cruise: nothing'); assert.equal(fx.state.speedBlur, 0);
    run(1, 0.1); assert.ok(fx.state.speedFeel > 0.1 && fx.state.speedFeel < 0.9, 'top speed: eases in');
    run(1, 1); close(fx.state.speedFeel, 1, 1e-3, 'top speed: settles at 1'); close(fx.state.speedBlur, L.blurMax, 1e-3);
    run(1.3, 1); close(fx.state.speedFeel, L.boostDeg / L.topDeg, 1e-3, 'boost: past 1'); close(fx.state.speedBlur, L.blurMax, 1e-3, 'blur caps at 1');
    run(0.5, 0.2); assert.ok(fx.state.speedFeel > 0.2 && fx.state.speedFeel < L.boostDeg / L.topDeg, 'release eases out');
    run(0.5, 1.5); close(fx.state.speedFeel, 0, 1e-3, 'back to rest');
    fx.setMotion('less'); run(1, 1.5); close(fx.state.speedFeel, 0.5, 1e-3, 'less: half'); close(fx.state.speedBlur, 0.5, 1e-3);
    fx.setMotion('off'); run(1, 1.5); assert.equal(fx.state.speedFeel, 0, 'off: none'); assert.equal(fx.state.speedBlur, 0);
    fx.setMotion('full'); run(1, 1.5); close(fx.state.speedFeel, 1, 1e-3);
    fx.reset(); assert.equal(fx.state.speedFeel, 0); assert.equal(fx.state.speedBlur, 0);
  } finally { fx.dispose(); }
});

test('Gentle halves the speed feel', () => {
  const feel = makeFeel({ 'gentle.on': true }), track = fakeTrack(), bus = createBus();
  const fx = createFx(bus, feel, { gentle: createGentle(feel, { search: '' }), photosafe: createPhotosafe(feel) });
  try {
    const k = place(track, { s: 50, speed: 30 }); k[S.speedPct] = 1;
    for (let t = 0; t < 1.5; t += 1 / 60) fx.update(1 / 60, k);
    close(fx.state.speedFeel, 0.5, 1e-3, 'Gentle: half'); close(fx.state.speedBlur, 0.5, 1e-3);
  } finally { fx.dispose(); }
});

test('the widen lands on the drawn camera only: rig.vFov, eye and look stay the clean pose', () => {
  const feel = makeFeel(), track = fakeTrack(), kart = place(track, { s: 50, speed: 30 });
  kart[S.speedPct] = 1;
  const chase = createChaseCamera(feel), quiet = createCamRig(feel);
  quiet.useVel = chase.rig.useVel = false;
  chase.snap(kart, track, 16 / 9); quiet.snap(kart, 16 / 9, null, track);
  const base = chase.camera.fov;
  close(base, chase.rig.vFov, 1e-9, 'at rest the copy is the rig');
  for (const [feelShare, motion, deg] of [[1, 'full', 7], [L.boostDeg / L.topDeg, 'full', 10], [0.5, 'full', 3.5], [1, 'off', 0], [0, 'full', 0]]) {
    chase.update(kart, { speedFeel: feelShare, traumaOut: 0 }, track, 1 / 60, 16 / 9, 'race', null, { motion });
    quiet.update(kart, null, 1 / 60, 16 / 9, track);
    close(chase.camera.fov, chase.rig.vFov + deg, 1e-9, `drawn fov widens by ${deg} at share ${feelShare} / ${motion}`);
    assert.equal(chase.rig.vFov, quiet.vFov, 'the rig never widens');
    assert.deepEqual(chase.rig.eye, quiet.eye); assert.deepEqual(chase.rig.look, quiet.look);
  }
  chase.update(kart, null, track, 1 / 60, 16 / 9, 'race', null, {});
  close(chase.camera.fov, chase.rig.vFov, 1e-9, 'no fx state: no widen');
  chase.dispose();
});
