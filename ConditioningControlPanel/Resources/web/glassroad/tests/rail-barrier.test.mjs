import { test } from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
const hook = registerHooks({ resolve(name, context, next) {
  const file = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' }[name];
  return file ? { url: new URL('../vendor/three/build/' + file, import.meta.url).href, shortCircuit: true } : next(name, context);
} });
const { createRailBarrier } = await import('../render/railBarrier.js');
const { Vector3 } = await import('three/webgpu');
hook.deregister();
const impact = { p: [10, 4, 20], tangent: [1, 0, 0], up: [0, 1, 0], normal: [0, 0, 1], dv: 12 };
function fixture() {
  const listeners = new Map();
  const bus = { on(name, fn) { listeners.set(name, fn); return () => listeners.delete(name); } };
  const fx = createRailBarrier(bus);
  return { fx, listeners, meshes: fx.object.children, fire: () => listeners.get('wall')?.(impact) };
}
test('contact reveals a world-space hex patch, flexes, fades and stays within a fixed pool', () => {
  const { fx, meshes, fire } = fixture();
  fire(); fx.update(.08);
  const mesh = meshes[0], a = mesh.geometry.attributes;
  assert.equal(mesh.position.x, 10); assert.equal(mesh.position.y, 4);
  assert.ok(Math.abs(mesh.position.z - 20.06) < 1e-6);
  assert.ok(a.position.count < 700);
  assert.ok(Math.max(...a.barrierAlpha.array) > .1);
  assert.ok(a.position.array.some((v, i) => i % 3 === 2 && Math.abs(v) > .01));
  const frozen = [...a.position.array];
  fx.update(.3, { paused: true }); assert.deepEqual([...a.position.array], frozen);
  fx.update(.7); assert.equal(mesh.visible, false);
  for (let i = 0; i < 100; i++) fire();
  assert.equal(meshes.length, 2); fx.reset(); assert.ok(meshes.every(m => !m.visible)); fx.dispose();
});
test('Gentle and reduced motion keep the honeycomb still and soft, and disposal retires listeners once', () => {
  for (const mode of ['gentle', 'reducedMotion']) {
    const { fx, meshes, fire, listeners } = fixture();
    fx.update(0, { [mode]: true }); fire(); fx.update(.08, { [mode]: true });
    const attributes = meshes[0].geometry.attributes;
    assert.ok(attributes.position.array.every((v, i) => i % 3 !== 2 || v === 0));
    assert.ok(Math.max(...attributes.barrierAlpha.array) < .2);
    let disposed = 0; meshes[0].material.addEventListener('dispose', () => disposed++);
    fx.dispose(); fx.dispose(); assert.equal(disposed, 1); assert.equal(listeners.size, 0);
  }
});
test('both rail sides keep the patch upright with its flex facing into the road', () => {
  const { fx, meshes } = fixture();
  for (const side of [-1, 1]) {
    const normal = [0, 0, side];
    fx.hit({ ...impact, normal });
    const mesh = meshes[side < 0 ? 0 : 1];
    const up = new Vector3(0, 1, 0).applyQuaternion(mesh.quaternion);
    const inward = new Vector3(0, 0, 1).applyQuaternion(mesh.quaternion);
    assert.ok(up.distanceTo(new Vector3(0, 1, 0)) < 1e-6);
    assert.ok(inward.distanceTo(new Vector3(...normal)) < 1e-6);
  }
  fx.dispose();
});
