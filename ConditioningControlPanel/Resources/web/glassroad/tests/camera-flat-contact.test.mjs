import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { step } from '../sim/kart.js';
import { S } from '../sim/state.js';
import { createCamRig, createKick } from '../render/cameraMath.js';
import { registerHooks } from 'node:module';
import { createBus } from '../core/events.js';
import { createFx } from '../fx/reactions.js';
import { createGentle } from '../fx/gentle.js';
import { createPhotosafe } from '../fx/photosafe.js';
import { makeFeel, place, acts, recorder, DT } from './sim-fake.mjs';

const hooks = registerHooks({ resolve(name, context, next) {
  return name === 'three/webgpu' ? { url: new URL('../vendor/three/build/three.webgpu.min.js', import.meta.url).href, shortCircuit: true } : next(name, context);
} });
const { createChaseCamera } = await import('../render/camera.js');
hooks.deregister();

test('Mainframe flat opening keeps camera height through repeated glancing contacts', () => {
  const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/mainframe.json', import.meta.url), 'utf8')));
  const feel = makeFeel(), st = place(track, { s: 12, speed: 28 }), rig = createCamRig(feel), rec = recorder();
  rig.useVel = false; rig.snap(st, 16 / 9, null, track);
  let maxCameraStep = 0, maxSide = 0, lastY = rig.eye[1], contacts = 0;
  for (let tick = 0; st[S.s] < 150; tick++) {
    step(st, acts({ throttle: 1 }), track, feel, DT, rec);
    if (tick % 18 === 0) { rig.hit('bump', [2.2, 1.6, 1.51][contacts++ % 3], 1); }
    rig.update(st, null, DT, 16 / 9, track);
    const support = track.query(st[S.posX], st[S.posY], st[S.posZ], st[S.seg]);
    assert.ok(support.onRoad && Math.abs(support.height) < 0.001);
    assert.equal(st[S.airborne], 0);
    maxCameraStep = Math.max(maxCameraStep, Math.abs(rig.eye[1] - lastY)); lastY = rig.eye[1];
    maxSide = Math.max(maxSide, Math.abs(rig.kick.out.x));
  }
  assert.ok(contacts > 10 && maxSide > 0.01 && maxSide < 0.05, 'light contacts stay subtle: ' + maxSide);
  assert.ok(maxCameraStep < 0.001, 'no false floor-step camera lift: ' + maxCameraStep);
});

test('even hard planar collisions stay sideways while real landing dips remain', () => {
  for (const speed of [5.9, 6, 6.1, 7.5, 9]) {
    const kick = createKick(); kick.hit('wall', speed, 1);
    let side = 0;
    for (let t = 0; t < 1; t += DT) { const out = kick.update(DT); assert.equal(out.y, 0); side = Math.max(side, Math.abs(out.x)); }
    assert.ok(side > 0, 'the impact still moves away from contact');
  }
  const landing = createKick(); landing.hit('land', 2);
  assert.ok(landing.update(DT).y < -0.1, 'real landing still dips');
});

test('hard racer and wall events through the actual FX bus never create floor steps at 60 or 20 fps', () => {
  const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/mainframe.json', import.meta.url), 'utf8')));
  // The lap seam has a small inherited vertical tangent. Use the physical flat boot district.
  const boot = track.sections.find(section => section.id === 'boot-hall');
  const flatStart = boot.fromS + (boot.toS - boot.fromS) / 4;
  for (const fps of [60, 20]) for (const gentle of [false, true]) {
    const feel = makeFeel({ 'gentle.on': gentle }), bus = createBus(), cam = createChaseCamera(feel, { bus });
    const clean = createChaseCamera(feel, { bus: createBus() });
    const fx = createFx(bus, feel, { gentle: createGentle(feel, { search: '' }), photosafe: createPhotosafe(feel) });
    const st = place(track, { s: flatStart, speed: 28 }), rec = recorder(), dt = 1 / fps;
    cam.snap(st, track, 16 / 9); clean.snap(st, track, 16 / 9); const eyeY = cam.camera.position.y;
    let maxStep = 0, maxSide = 0, thud = 0, lastY = eyeY;
    for (let frame = 0; frame < fps * 3; frame++) {
      for (let sub = 0; sub < 120 / fps; sub++) step(st, acts({ throttle: 1 }), track, feel, 1 / 120, rec);
      if (frame % Math.round(fps * .3) === 0) {
        bus.emit('bump', { a: 0, b: 3, dv: 14, p: [st[S.posX], 0, st[S.posZ] + 1] });
        bus.emit('wall', { dv: 13, side: frame % 2 ? -1 : 1 });
      }
      fx.update(dt, st); cam.update(st, fx.state, track, dt, 16 / 9);
      clean.update(st, null, track, dt, 16 / 9);
      assert.ok(Math.abs(track.frameAt(st[S.s]).t[1]) < 1e-8, 'fixture stays on genuinely flat support');
      assert.equal(st[S.posY], 0); assert.equal(st[S.airborne], 0);
      assert.ok(Math.abs(cam.camera.position.y - eyeY) < 1e-7, 'flat support keeps a constant camera height');
      assert.ok(Math.abs(cam.camera.position.y - clean.camera.position.y) < 1e-7, 'actual FX never add height to the clean road pose');
      assert.equal(cam.camera.fov, clean.camera.fov, 'actual FX never add camera zoom');
      assert.deepEqual(cam.rig.focus, clean.rig.focus, 'actual FX never feed back into road follow');
      maxStep = Math.max(maxStep, Math.abs(cam.camera.position.y - lastY)); lastY = cam.camera.position.y;
      maxSide = Math.max(maxSide, Math.abs(cam.rig.kick.out.x)); thud = Math.max(thud, fx.state.rumble);
    }
    assert.ok(maxStep < 1e-7 && maxSide > .1 && maxSide <= .8, 'lateral response remains bounded');
    if (!gentle) assert.ok(thud > 0, 'contact still has a thud');
    cam.dispose(); clean.dispose(); fx.dispose();
  }
});
