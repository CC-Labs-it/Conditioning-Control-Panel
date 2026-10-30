// Feel wave item 9 (lane E): the look-point hint leads the LOOK direction only, after compose(). The eye, the
// follow and the rig's own look never move; Gentle and Motion off drop it; a quiet road lands on today's pose.
import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { hintTarget, createHint, rotateLook } from '../render/cameraMath.js';
import { fakeTrack, makeFeel, place } from './sim-fake.mjs';
import { S } from '../sim/state.js';

const hooks = registerHooks({ resolve(name, context, next) {
  return name === 'three/webgpu' ? { url: new URL('../vendor/three/build/three.webgpu.min.js', import.meta.url).href, shortCircuit: true } : next(name, context);
} });
const { createChaseCamera } = await import('../render/camera.js');
const { PerspectiveCamera } = await import('three/webgpu');
hooks.deregister();

const DEG = Math.PI / 180;
const near = (a, b, eps, msg) => assert.ok(Math.abs(a - b) <= eps, `${msg}: ${a} vs ${b}`);
const tight = () => fakeTrack({ nodes: 40, spacing: 10 });            // R 63.7 m, turns right ~22.5 deg per 25 m
const straight = () => fakeTrack({ nodes: 400000, spacing: 10 });     // turns 0.002 deg per 25 m

test('feel defaults: shortcut 4, bend 6, within 25 m, 0.25 s in, 0.4 s out', () => {
  const f = makeFeel();
  assert.deepEqual(['shortcutDeg', 'bendDeg', 'aheadM', 'inS', 'outS'].map((k) => f.get('camera.hint.' + k)), [4, 6, 25, 0.25, 0.4]);
});

test('a right bend leads the look right by its share of bendDeg, capped at bendDeg', () => {
  const track = tight(), kart = place(track, { s: 100, speed: 20 });
  const lead = hintTarget(track, kart, 4, 6, 25);
  near(lead, -6 * DEG * (25 / (track.lengthM / (2 * Math.PI))) / (30 * DEG), 1e-3, 'right bend share');
  assert.ok(lead < 0, 'right = yaw down');
  near(hintTarget(track, kart, 4, 6, 60), -6 * DEG, 1e-9, 'a sharper turn caps at bendDeg');
  near(hintTarget(straight(), kart, 4, 6, 25), 0, 1e-4, 'straight road: no lead');
});

test('a shortcut gate within 25 m pulls the look toward its side, nothing beyond', () => {
  const track = straight(), kart = place(track, { s: 100, speed: 20 });
  track.routes = [{ side: 'R', openS0: 105, openS1: 140 }];
  near(hintTarget(track, kart, 4, 6, 25), -4 * DEG, 1e-4, 'right gate near: full lead right');
  track.routes = [{ side: 'L', openS0: 105, openS1: 140 }];
  near(hintTarget(track, kart, 4, 6, 25), 4 * DEG, 1e-4, 'left gate near: full lead left');
  track.routes = [{ side: 'L', openS0: 90, openS1: 140 }];
  near(hintTarget(track, kart, 4, 6, 25), 4 * DEG, 1e-4, 'alongside the open edge');
  track.routes = [{ side: 'L', openS0: 118, openS1: 140 }];
  const mid = hintTarget(track, kart, 4, 6, 25);
  assert.ok(mid > 0 && mid < 4 * DEG, 'eases in with distance: ' + mid / DEG);
  track.routes = [{ side: 'L', openS0: 126, openS1: 140 }];
  near(hintTarget(track, kart, 4, 6, 25), 0, 1e-4, 'past 25 m');
  track.forkPortals = [{ id: 'f', side: 1, s0: 110, s1: 130 }]; track.forks = [{ id: 'f', fromS: 112 }];
  near(hintTarget(track, kart, 4, 6, 25), -4 * DEG, 1e-3, 'a fork mouth on the right');
  track.forks = [{ id: 'f', fromS: 900 }];
  near(hintTarget(track, kart, 4, 6, 25), 0, 1e-4, 'a fork exit is not a gate');
});

test('no lead driving backwards, on a branch or without a track', () => {
  const track = tight();
  assert.equal(hintTarget(track, place(track, { s: 100, headingDeg: 180 }), 4, 6, 25), 0);
  const kart = place(track, { s: 100 });
  assert.equal(hintTarget(null, kart, 4, 6, 25), 0);
  track.forkAtSeg = () => ({ id: 'f' });
  assert.equal(hintTarget(track, kart, 4, 6, 25), 0);
});

test('the hint tweens in over 0.25 s and out over 0.4 s; off snaps to the clean pose', () => {
  const feel = makeFeel(), track = straight(), kart = place(track, { s: 100 }), h = createHint(feel);
  track.routes = [{ side: 'L', openS0: 105, openS1: 140 }];
  const dt = 1 / 60;
  let t = 0;
  while (t < 0.25 - 1e-9) { h.update(track, kart, dt, 'on'); t += dt; }
  assert.ok(h.value > 0.94 * 4 * DEG && h.value < 4 * DEG, 'in by 0.25 s: ' + h.value / DEG);
  const before = h.value; h.update(track, kart, dt, 'out');
  assert.ok(h.value > 0.85 * before, 'no snap back');
  for (t = dt; t < 0.4 - 1e-9; t += dt) h.update(track, kart, dt, 'out');
  assert.ok(h.value < 0.06 * 4 * DEG, 'out by 0.4 s: ' + h.value / DEG);
  for (let i = 0; i < 120; i++) h.update(track, kart, dt, 'out');
  assert.equal(h.value, 0, 'lands exactly on 0');
  h.update(track, kart, dt, 'on');
  assert.ok(h.value > 0);
  assert.equal(h.update(track, kart, dt, 'off'), 0, 'Gentle / Motion off: none at all');
});

test('rotateLook keeps the eye and the range, turns about up', () => {
  const eye = [1, 2, 3], look = [1, 2, -7], out = [0, 0, 0];
  rotateLook(out, eye, look, [0, 1, 0], 90 * DEG);
  near(out[0], -9, 1e-9, 'positive = left'); near(out[1], 2, 1e-9, 'height kept'); near(out[2], 3, 1e-9, 'z');
  assert.deepEqual(eye, [1, 2, 3]);
});

/** Drive two chase cameras along the same states; returns per-frame [position, quaternion, rig eye, rig look]. */
function run(track, optsA, optsB, feelOver = {}) {
  const feel = makeFeel(feelOver), a = createChaseCamera(feel), b = createChaseCamera(feel), rows = [];
  const dt = 1 / 60;
  for (let n = 0; n < 180; n++) {
    const st = place(track, { s: 60 + n * dt * 20, speed: 20 }); st[S.speedPct] = 0.5;
    if (!n) { a.snap(st, track, 16 / 9); b.snap(st, track, 16 / 9); continue; }
    a.update(st, null, track, dt, 16 / 9, 'race', null, { viewDt: dt, ...optsA });
    b.update(st, null, track, dt, 16 / 9, 'race', null, { viewDt: dt, ...optsB });
    rows.push([a, b].map((c) => ({ pos: c.camera.position.toArray(), q: c.camera.quaternion.toArray(), eye: [...c.rig.eye], look: [...c.rig.look], vFov: c.camera.fov })));
  }
  a.dispose(); b.dispose();
  return rows;
}

test('the eye never moves: a leading hint changes only the drawn aim', () => {
  const track = tight();
  track.routes = [{ side: 'L', openS0: 70, openS1: 200 }];
  let turned = 0;
  for (const [on, off] of run(track, {}, { motion: 'off' })) {
    assert.deepEqual(on.pos, off.pos, 'drawn eye');
    assert.deepEqual(on.eye, off.eye, 'rig eye'); assert.deepEqual(on.look, off.look, 'rig look');
    assert.equal(on.vFov, off.vFov, 'fov');
    if (on.q.some((v, i) => Math.abs(v - off.q[i]) > 1e-6)) turned++;
  }
  assert.ok(turned > 100, 'the hint was live: ' + turned);
});

test('Gentle, the rear view and looking around stand the hint down', () => {
  const track = tight();
  for (const opts of [{ gentle: true }, { rearView: true }, { manual: true }]) {
    const rows = run(track, opts, opts);
    const off = run(track, { ...opts, motion: 'off' }, { ...opts, motion: 'off' });
    rows.forEach(([r], i) => { if (i > 30) assert.deepEqual(r.q, off[i][0].q, JSON.stringify(opts)); });
  }
});

test('with no hint ahead the drawn pose is byte-identical to the old lookAt of the rig pose', () => {
  const track = straight();   // the residual 0.002 deg bend is cut by bendDeg 0 below
  for (const over of [{}, { 'camera.hint.bendDeg': 0 }]) {
    const rows = run(track, {}, { motion: 'off' }, over);
    const ref = new PerspectiveCamera();
    for (const [on, off] of rows) {
      ref.position.fromArray(off.eye); ref.up.set(0, 1, 0); ref.lookAt(off.look[0], off.look[1], off.look[2]);
      assert.deepEqual(off.q, ref.quaternion.toArray(), 'motion off = old pose');
      if (over['camera.hint.bendDeg'] === 0) assert.deepEqual(on.q, off.q, 'defaults with nothing ahead = old pose');
    }
  }
});
