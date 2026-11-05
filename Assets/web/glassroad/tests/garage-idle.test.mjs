import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { garageIdleAt } from '../render/garageIdleMath.js';

const hooks = registerHooks({ resolve(s, c, next) {
  const path = s === 'three' || s === 'three/webgpu' ? 'build/three.webgpu.min.js' : s.startsWith('three/addons/') ? s.slice(6) : null;
  return path ? { url: new URL('../vendor/three/' + path, import.meta.url).href, shortCircuit: true } : next(s, c);
} });
const { GLTFLoader } = await import('three/addons/loaders/GLTFLoader.js');
const { MeshoptDecoder } = await import('three/addons/libs/meshopt_decoder.module.js');
const { Vector3, Quaternion } = await import('three/webgpu');
const { createGarageIdle } = await import('../render/garageIdle.js');
hooks.deregister();
const source = readFileSync(new URL('../art/models/emi-teacup-kart.glb', import.meta.url));
const length = source.readUInt32LE(12), json = JSON.parse(source.subarray(20, 20 + length));
json.materials = json.materials.map(m => ({ name: m.name }));
const bytes = Buffer.from(JSON.stringify(json)), padded = (bytes.length + 3) & ~3;
const binary = source.subarray(20 + length), glb = Buffer.alloc(20 + padded + binary.length, 0x20);
source.copy(glb, 0, 0, 20); glb.writeUInt32LE(glb.length, 8); glb.writeUInt32LE(padded, 12); bytes.copy(glb, 20); binary.copy(glb, 20 + padded);
const asset = await new GLTFLoader().setMeshoptDecoder(MeshoptDecoder).parseAsync(glb.buffer, '');

test('garage alternates three complete performances with rest at every join', () => {
  assert.equal(garageIdleAt(2).name, 'wave'); assert.equal(garageIdleAt(10).name, 'curious'); assert.equal(garageIdleAt(18).name, 'hop');
  assert.equal(garageIdleAt(2).face, 'happy'); assert.equal(garageIdleAt(10).face, 'neutral');
  for (const t of [0, 8, 16, 24, 48]) for (const [k, v] of Object.entries(garageIdleAt(t))) if (typeof v === 'number' && k !== 'waveTime') assert.ok(v === 0, k);
  for (const t of [2, 10, 18]) {
    const a = garageIdleAt(t), b = garageIdleAt(t, { gentle: true }), off = garageIdleAt(t, { motionOff: true });
    for (const k of ['wave', 'yaw', 'roll', 'lift', 'squash', 'antenna']) { assert.equal(b[k], a[k] * .5); assert.ok(off[k] === 0); }
  }
});

test('little hop has one apex and continues descending into its landing', () => {
  for (const hz of [20, 30, 60, 120]) {
    let last = Infinity;
    for (let t = 18.0; t < 18.32; t += 1 / hz) { const h = garageIdleAt(t).lift; assert.ok(h < last); last = h; }
    assert.equal(garageIdleAt(18.32).lift, 0);
  }
});

test('real R4 wave and curious look preserve sole anchors; hop lifts both together; reset has no drift', () => {
  const model = asset.scene, idle = createGarageIdle(model, asset.animations);
  model.updateMatrixWorld(true);
  const feet = [['footL', [.12848665, .31999999, -.18965095]], ['footR', [-.0568836, .31999999, -.09388392]]].map(([n, xyz]) => {
    const bone = model.getObjectByName(n), p = new Vector3(...xyz);
    return { bone, p, local: bone.worldToLocal(p.clone()), q: bone.getWorldQuaternion(new Quaternion()) };
  });
  const nodes = []; model.traverse(b => nodes.push({ b, p: b.position.clone(), q: b.quaternion.clone(), s: b.scale.clone() }));
  let maxResidual = 0;
  for (let i = 0; i < 24 * 60; i++) {
    idle.beforePose(); const m = idle.update(1 / 60); model.updateMatrixWorld(true);
    for (const f of feet) {
      const expected = f.p.clone(); expected.y += m.offsets.lift;
      maxResidual = Math.max(maxResidual, f.bone.localToWorld(f.local.clone()).distanceTo(expected));
    }
  }
  assert.ok(maxResidual < .002, 'sole contact residual under 2 mm: ' + maxResidual);
  idle.update(0, { blocked: true });
  assert.equal(idle.metrics.time, 0);
  for (const { b, p, q, s } of nodes) { assert.deepEqual(b.position.toArray(), p.toArray()); assert.deepEqual(b.quaternion.toArray(), q.toArray()); assert.deepEqual(b.scale.toArray(), s.toArray()); }
  idle.update(1, { motionOff: true }); assert.equal(idle.metrics.name, 'rest');
});

test('whole garage performance agrees at 30 and 120 fps and can restart after an interruption', () => {
  const run = hz => { const idle = createGarageIdle(asset.scene, asset.animations); for (let i = 0; i < hz * 10; i++) { idle.beforePose(); idle.update(1 / hz); }
    const out = { ...idle.metrics.offsets }; idle.reset(); return out; };
  const a = run(30), b = run(120);
  for (const k of Object.keys(a)) if (typeof a[k] === 'number') assert.ok(Math.abs(a[k] - b[k]) < 1e-9, k);
});
