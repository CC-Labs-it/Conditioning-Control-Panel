// The water probe sees only broad surfaces: the reflection layer goes to objects by world size.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { 'three': 'three.webgpu.min.js', 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { Scene, Group, Mesh, InstancedMesh, Sprite, BoxGeometry, PlaneGeometry, MeshBasicNodeMaterial, SpriteNodeMaterial, Matrix4 } = await import('three/webgpu');
const { REFLECT_LAYER, REFLECT_MIN_M, assignReflectionLayer, reflectedRadius } = await import('../render/reflectionLayer.js');
hooks.deregister();

function scene() {
  const s = new Scene(), m = new MeshBasicNodeMaterial();
  const road = new Mesh(new PlaneGeometry(600, 600), m); road.name = 'road';
  const kart = new Mesh(new BoxGeometry(1.4, 1, 2), m); kart.name = 'kart';
  const scaled = new Mesh(new BoxGeometry(1, 1, 1), m); scaled.name = 'scaled'; scaled.scale.setScalar(12);
  const rows = new InstancedMesh(new BoxGeometry(.4, .4, .4), m, 2); rows.name = 'rows';
  rows.setMatrixAt(0, new Matrix4().makeTranslation(-80, 0, 0)); rows.setMatrixAt(1, new Matrix4().makeTranslation(80, 0, 0));
  const bits = new InstancedMesh(new BoxGeometry(.4, .4, .4), m, 2); bits.name = 'bits';
  const sprite = new Sprite(new SpriteNodeMaterial()); sprite.name = 'sprite'; sprite.scale.setScalar(40);
  const forced = new Mesh(new BoxGeometry(.2, .2, .2), m); forced.name = 'forced'; forced.userData.reflect = true;
  const refused = new Mesh(new PlaneGeometry(900, 900), m); refused.name = 'refused'; refused.userData.reflect = false;
  const catcher = new Mesh(new PlaneGeometry(900, 900), m); catcher.name = 'catcher'; catcher.layers.set(2);
  const water = new Group(); water.name = 'water-volume'; const surface = new Mesh(new PlaneGeometry(500, 500), m); water.add(surface);
  s.add(road, kart, scaled, rows, bits, sprite, forced, refused, catcher, water);
  return { s, road, kart, scaled, rows, bits, sprite, forced, refused, catcher, water, surface };
}

test('big surfaces, scaled props and spread instanced rows join the probe; small things, sprites and catchers stay out', () => {
  const w = scene();
  const { on, off } = assignReflectionLayer(w.s, { exclude: w.water });
  const sees = o => o.layers.isEnabled(REFLECT_LAYER);
  assert.ok(sees(w.road)); assert.ok(sees(w.scaled)); assert.ok(sees(w.rows)); assert.ok(sees(w.forced));
  assert.ok(!sees(w.kart)); assert.ok(!sees(w.bits)); assert.ok(!sees(w.sprite)); assert.ok(!sees(w.refused));
  assert.ok(!sees(w.catcher), 'a layer-2 shadow catcher is never touched'); assert.equal(w.catcher.layers.mask, 1 << 2);
  assert.ok(!sees(w.surface), 'the excluded water volume never reflects itself');
  assert.equal(on, 4); assert.equal(off, 4);
  for (const o of [w.road, w.kart, w.rows, w.forced]) assert.ok(o.layers.isEnabled(0), 'the main camera still sees everything');
  assert.ok(reflectedRadius(w.kart) < REFLECT_MIN_M && reflectedRadius(w.scaled) > REFLECT_MIN_M);
  assert.equal(reflectedRadius(w.sprite), 0);
});

test('a second warm re-assigns: an object that shrank leaves the probe', () => {
  const w = scene();
  assignReflectionLayer(w.s);
  w.scaled.scale.setScalar(.5);
  assignReflectionLayer(w.s);
  assert.ok(!w.scaled.layers.isEnabled(REFLECT_LAYER));
});

test('the probe cameras see only the reflection layer and assign it at warm', () => {
  const src = readFileSync(new URL('../render/waterReflections.js', import.meta.url), 'utf8');
  assert.match(src, /for\(const face of camera\.children\)face\.layers\.set\(REFLECT_LAYER\)/);
  assert.match(src, /assignReflectionLayer\(scene,\{exclude\}\)/);
  assert.ok(src.indexOf('assignReflectionLayer(scene') < src.indexOf('compileAsync'), 'layers before the cube compile');
});
