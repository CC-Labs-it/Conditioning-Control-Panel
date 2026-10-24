import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { createGlideCap, GLIDE_ASSET } from '../render/glideCap.js';
const hooks = registerHooks({ resolve(specifier, context, next) {
  return specifier === 'three/webgpu' ? { url: new URL('../vendor/three/build/three.webgpu.min.js', import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { Group, Mesh, BoxGeometry, MeshStandardMaterial } = await import('three/webgpu');
hooks.deregister();
function source() {
  const scene = new Group(), spin = new Group(); spin.name = 'spin'; spin.position.set(0, .225, .155);
  scene.add(new Mesh(new BoxGeometry(), new MeshStandardMaterial()), spin);
  spin.add(new Mesh(new BoxGeometry(), new MeshStandardMaterial()));
  return { scene };
}
function model() { const m = new Group(), mount = new Group(); mount.name = 'head_attach'; m.add(mount); return m; }
test('Accepted beanie attaches at identity and rotor alone deploys, spins and freezes on pause', () => {
  const shared = source(), art = { model: id => id === GLIDE_ASSET ? shared : null }, m = model();
  const a = createGlideCap(art, m), b = createGlideCap(art, model());
  assert.equal(a.cap.parent.name, 'head_attach');
  assert.deepEqual(a.cap.position.toArray(), [0, 0, 0]);
  a.update(true, .2);
  assert.equal(a.cap.visible, true); assert.ok(a.spin.scale.x > .5);
  assert.deepEqual(a.cap.scale.toArray(), [1, 1, 1]);
  const q = a.spin.quaternion.clone(), s = a.spin.scale.clone();
  a.update(true, 0); assert.deepEqual(a.spin.quaternion.toArray(), q.toArray()); assert.deepEqual(a.spin.scale.toArray(), s.toArray());
  assert.equal(b.cap.visible, false); assert.deepEqual(shared.scene.getObjectByName('spin').scale.toArray(), [1, 1, 1]);
  a.update(true, .1); assert.notDeepEqual(a.spin.quaternion.toArray(), q.toArray());
  for (let i = 0; i < 90; i++) a.update(false, 1 / 60);
  assert.equal(a.cap.visible, false); assert.equal(a.spin.scale.x, 0);
  a.update(true, .2); a.reset(); assert.equal(a.cap.visible, false);
  a.dispose(); b.dispose(); assert.equal(m.getObjectByName('head_attach').children.length, 0);
});
test('Attachment refuses a mismatched accepted base and missing mount or rotor', () => {
  const art = { model: () => source(), row: id => id === GLIDE_ASSET ? { baseSha256: 'a' } : { sha256: 'b' } };
  assert.equal(createGlideCap(art, model()), null);
  assert.equal(createGlideCap({ model: () => source() }, new Group()), null);
  assert.equal(createGlideCap({ model: () => ({ scene: new Group() }) }, model()), null);
});
