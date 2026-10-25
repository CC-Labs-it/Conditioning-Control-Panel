import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
import { getBoudoirLayout } from '../track/boudoir-layout.js';
import { getBoudoirSetpieces, mirrorGatePhase, releaseBoudoirMirror, resetBoudoirMirrors } from '../track/boudoir-setpieces.js';
const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { buildBoudoirMirrorRoom, mirrorRoomNear } = await import('../render/boudoirMirrorRoom.js');
const { Scene, PerspectiveCamera, WebGLCoordinateSystem, Vector3, Raycaster, InstancedMesh, BoxGeometry, MeshBasicNodeMaterial } = await import('three/webgpu'); hooks.deregister();
const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/boudoir.json', import.meta.url), 'utf8')));
const hall = getBoudoirLayout(track).landmarks.find(r => r.id === 'drawer-hall');
function setup() {
  const room = buildBoudoirMirrorRoom(track), scene = new Scene(); scene.add(room.group); scene.updateMatrixWorld(true);
  const f = track.frameAt(hall.s0 + 30), camera = new PerspectiveCamera(50, 16 / 9, .1, 800);
  camera.position.set(f.p[0] + f.n[0] * 5, f.p[1] + f.n[1] * 5, f.p[2] + f.n[2] * 5);
  camera.lookAt(new Vector3(...f.p).addScaledVector(new Vector3(...f.t), 80)); camera.updateMatrixWorld();
  return { room, scene, camera };
}

test('three non-bouncing reflection nodes serve every warmable wall, ceiling and gate pane', () => {
  const { room } = setup();
  assert.equal(room.reflections.length, 3); assert.equal(room.diagnostics.shutters, 9); assert.ok(room.diagnostics.mirrors >= 20);
  const reflected = [], materials = new Set();
  room.group.traverse(o => { if (o.isMesh && o.material.name?.startsWith('mirror-live-scene:')) { reflected.push(o); materials.add(o.material); } });
  assert.ok(reflected.reduce((n, m) => n + (m.count || 1), 0) > 50); assert.equal(materials.size, 2);
  assert.ok(room.diagnostics.baseDraws <= 40, 'static frames and moving shutters share batches');
  for (const r of room.reflections) assert.equal(r.reflector.bounces, false);
  room.dispose();
});

test('physical shutters share collision poses, preserve a clear lane, and freeze on pause', () => {
  const { room, camera } = setup(), rows = getBoudoirSetpieces(track).mirrorGates;
  for (const t of [0, .8, 6.8, 8.5, 9.7, 16.8]) {
    room.update(.1, { camera, raceTimeS: t });
    for (const row of rows) assert.ok(Math.abs(room.group.getObjectByName('mirror-shutter:' + row.id).position.y - 3 - mirrorGatePhase(row, t).liftM) < 1e-8);
    for (let station = 0; station < 3; station++) assert.ok(rows.filter(r => r.station === station).some(r => mirrorGatePhase(r, t).liftM >= 8));
  }
  const before = rows.map(row => room.group.getObjectByName('mirror-shutter:' + row.id).position.y);
  room.update(1, { camera, raceTimeS: 25, paused: true }, true);
  assert.deepEqual(rows.map(row => room.group.getObjectByName('mirror-shutter:' + row.id).position.y), before); room.dispose();
});

test('closed shutter glass faces the driver ahead of its thick backing', () => {
  const { room } = setup(), row = getBoudoirSetpieces(track).mirrorGates.find(r => r.station === 0 && r.lane === 1);
  const origin = room.group.localToWorld(new Vector3(row.x, 3, -(row.s - hall.s0) + 3));
  const direction = new Vector3(0, 0, -1).applyQuaternion(room.group.quaternion);
  const hits = new Raycaster(origin, direction, 0, 5).intersectObject(room.group, true);
  assert.ok(hits.length); assert.ok(hits[0].object.material.name.startsWith('mirror-prismatic-shutter:'), 'glass must not be hidden by a box face');
  room.dispose();
});
test('impact escape lifts the real shutter meshes and expires without changing other stations', () => {
  const { room, camera } = setup(), rows = getBoudoirSetpieces(track).mirrorGates;
  releaseBoudoirMirror(track, rows[1], 3);
  room.update(.1, { camera, raceTimeS: 3.4 });
  for (const row of rows) {
    const lift = room.group.getObjectByName('mirror-shutter:' + row.id).position.y - 3;
    assert.ok(Math.abs(lift - (row.station === 0 ? 8.4 : mirrorGatePhase(row, 3.4).liftM)) < 1e-8);
  }
  resetBoudoirMirrors(track); room.dispose();
});

test('native captures exclude every reflective face and restore renderer state with bounded resolution and frequency', () => {
  const { room, scene, camera } = setup();
  let target = { id: 'main-target' }, mrt = { id: 'main-mrt' }, draws = 0;
  const originalTarget = target, originalMrt = mrt;
  const renderer = { autoClear: false, coordinateSystem: WebGLCoordinateSystem,
    getDrawingBufferSize: out => out.set(3840, 2160), getRenderTarget: () => target, getMRT: () => mrt,
    setRenderTarget: value => { target = value; }, setMRT: value => { mrt = value; }, clear() {},
    render(capturedScene, reflectedCamera) {
      draws++; assert.notEqual(target, originalTarget); assert.equal(mrt, null);
      assert.equal(capturedScene, scene);
      if (room.diagnostics.frontStation !== null) {
        const reflected = room.group.worldToLocal(reflectedCamera.position.clone());
        const original = room.group.worldToLocal(camera.position.clone()), plane = room.group.getObjectByName('mirror-plane:0');
        assert.ok(Math.abs(reflected.z - (2 * plane.position.z - original.z)) < .00001, 'front reflection mirrors the real camera across the shutter plane');
        assert.ok(Math.abs(reflected.x - original.x) < .00001);
      }
      room.group.traverse(o => { if (/^mirror-(live-scene|prismatic-shutter|backplate)/.test(o.material?.name || '')) assert.equal(o.material.visible, false); });
    } };
  const capture = () => room.reflections.forEach((node, i) => node.reflector.updateBefore({ scene, camera, renderer,
    material: room.group.children.find(m => m.material?.name === (i === 2 ? 'mirror-prismatic-shutter:0' : 'mirror-live-scene:' + (i ? 1 : -1))).material }));
  capture(); assert.equal(draws, 0, 'general boot warm cannot make real reflection captures');
  room.update(.1, { camera, raceTimeS: 1 });
  renderer._isPreCompiling = true; capture(); assert.equal(draws, 0, 'pipeline compilation cannot recurse into scene captures');
  renderer._isPreCompiling = false; capture(); assert.equal(draws, 1);
  assert.equal(target, originalTarget); assert.equal(mrt, originalMrt); assert.equal(renderer.autoClear, false);
  capture(); assert.equal(draws, 1, 'same frame never recaptures');
  room.update(.1, { camera, raceTimeS: 1.1, paused: true }); capture(); assert.equal(draws, 1);
  for (const node of room.reflections) for (const rt of node.reflector.renderTargets.values()) {
    assert.ok(rt.width <= 512 && rt.height <= 512); assert.ok(rt.width > 0 && rt.height > 0);
  }
  room.update(.1, { camera: { position: new Vector3(10000, 10000, 10000) }, raceTimeS: 2 }); capture(); assert.equal(draws, 1);
  room.update(.1, { camera, raceTimeS: 2.1 }); capture(); assert.equal(draws, 2);
  camera.position.copy(room.group.localToWorld(new Vector3(0, 5, -180))); camera.updateMatrixWorld(true);
  room.update(.1, { camera, raceTimeS: 2.2 }); capture(); assert.equal(draws, 4, 'two side captures resume after passing every station');
  room.dispose(); room.dispose(); assert.equal(scene.children.length, 0);
});

test('near test includes the long hall ends without enabling distant rooms', () => {
  for (const z of [0, -100, -200]) assert.equal(mirrorRoomNear(new Vector3(0, 4, z), 200), true);
  for (const p of [new Vector3(130, 4, -100), new Vector3(0, 4, 120), new Vector3(0, 4, -330)]) assert.equal(mirrorRoomNear(p, 200), false);
});

test('runtime warm captures use the same camera and restore paused capture policy and renderer state', async () => {
  const { room, scene, camera } = setup(), quaternion = camera.quaternion.clone();
  camera.position.copy(room.group.localToWorld(new Vector3(0, 5, -70))); camera.updateMatrixWorld(true);
  const frontZ = room.group.getObjectByName('mirror-plane:0').position.z;
  const geometry = new BoxGeometry(), material = new MeshBasicNodeMaterial();
  const pooled = new InstancedMesh(geometry, material, 1); pooled.count = 0; pooled.visible = false; material.visible = false; scene.add(pooled);
  const mainTarget = {}, mainMrt = {}; let target = mainTarget, mrt = mainMrt, draws = 0, compiles = 0;
  const raw = { autoClear: false, coordinateSystem: WebGLCoordinateSystem,
    getDrawingBufferSize: out => out.set(1440, 900), getRenderTarget: () => target, getMRT: () => mrt,
    setRenderTarget: value => { target = value; }, setMRT: value => { mrt = value; }, clear() {}, render() {
      draws++;
      assert.equal(pooled.visible, draws <= 3); assert.equal(material.visible, draws <= 3);
      assert.equal(pooled.count, draws <= 3 ? 1 : 0); assert.equal(pooled.frustumCulled, draws > 3);
    } };
  const capture = () => room.reflections.forEach((node, i) => node.reflector.updateBefore({ scene, camera, renderer: raw,
    material: room.group.children.find(m => m.material?.name === (i === 2 ? 'mirror-prismatic-shutter:0' : 'mirror-live-scene:' + (i ? 1 : -1))).material }));
  const renderer = { three: raw, post(s, c) {
    assert.equal(s, scene); assert.equal(c, camera);
    return { async compileAsync() { compiles++; raw._isPreCompiling = true;
      try { capture(); } finally { raw._isPreCompiling = false; }
    }, render: capture };
  } };
  room.update(.1, { camera, raceTimeS: 7, paused: true });
  capture(); assert.equal(draws, 0);
  await room.warmCapture(renderer, scene, camera);
  assert.equal(compiles, 3); assert.equal(draws, 9);
  assert.equal(room.group.getObjectByName('mirror-plane:0').position.z, frontZ);
  assert.ok(camera.quaternion.angleTo(quaternion) < 1e-7);
  assert.equal(target, mainTarget); assert.equal(mrt, mainMrt); assert.equal(raw.autoClear, false);
  assert.equal(pooled.visible, false); assert.equal(material.visible, false); assert.equal(pooled.count, 0); assert.equal(pooled.frustumCulled, true);
  capture(); assert.equal(draws, 9, 'warm capture restores the paused runtime policy');
  renderer.post = () => ({ async compileAsync() { throw Error('compile failure'); } });
  await assert.rejects(room.warmCapture(renderer, scene, camera), /compile failure/);
  assert.ok(camera.quaternion.angleTo(quaternion) < 1e-7);
  assert.equal(target, mainTarget); assert.equal(mrt, mainMrt); assert.equal(raw.autoClear, false);
  assert.equal(pooled.visible, false); assert.equal(material.visible, false); assert.equal(pooled.count, 0); assert.equal(pooled.frustumCulled, true);
  capture(); assert.equal(draws, 9); room.dispose(); pooled.dispose(); geometry.dispose(); material.dispose();
});
