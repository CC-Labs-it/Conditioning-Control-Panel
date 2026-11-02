import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { readFileSync } from 'node:fs';
const hooks = registerHooks({ resolve(specifier, context, next) {
  const names = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return names[specifier] ? { url: new URL('../vendor/three/build/' + names[specifier], import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { Scene } = await import('three/webgpu');
const { createGarageBackdrop, garageBackdropLayout, GARAGE_BACKDROP } = await import('../render/garageBackdrop.js');
hooks.deregister();

test('showroom contains a real sky graph and three physical scenic layers inside a bounded draw budget', () => {
  const scene = new Scene(), kit = createGarageBackdrop(scene);
  try {
    for (const name of ['garage-painted-sky', 'garage-gloss-checker-floor', 'garage-showroom-island',
      'garage-candy-race-arch', 'garage-distant-track-0', 'garage-distant-track-1',
      'garage-scalloped-cloud-banks', 'garage-near-far-soap-bubbles', 'garage-floating-sparkles'])
      assert.ok(kit.object.getObjectByName(name), name);
    const sky = kit.object.getObjectByName('garage-painted-sky');
    assert.ok(sky.material.colorNode && sky.material.fog === false, 'gradient sky cannot flatten under fog');
    const cloud = kit.object.getObjectByName('garage-scalloped-cloud-banks');
    const bubbles = kit.object.getObjectByName('garage-near-far-soap-bubbles');
    assert.ok(cloud.isInstancedMesh && bubbles.isInstancedMesh);
    assert.ok(bubbles.material.opacityNode && bubbles.material.depthWrite === false);
    assert.ok(kit.inspect().draws <= GARAGE_BACKDROP.maxDraws);
    assert.equal(kit.inspect().clouds, 72); assert.equal(kit.inspect().bubbles, 28);
    assert.ok(kit.layout.bubbles.some(b => b.p[2] < 1) && kit.layout.bubbles.some(b => b.p[2] > 15));
    assert.deepEqual(garageBackdropLayout(), garageBackdropLayout(), 'scene layout stays deterministic');
  } finally { kit.dispose(); }
});

test('bounded scenery animation freezes for pause, reduced motion and Gentle without replacing resources', () => {
  const kit = createGarageBackdrop(new Scene()), meshes = [], resources = [];
  kit.object.traverse(o => { if (o.isMesh) { meshes.push(o); resources.push([o.geometry, o.material]); } });
  const bubble = kit.object.getObjectByName('garage-near-far-soap-bubbles');
  const before = Array.from(bubble.instanceMatrix.array);
  kit.update(.05); assert.notDeepEqual(Array.from(bubble.instanceMatrix.array), before);
  for (const option of [{ paused: true }, { reducedMotion: true }, { gentle: true }]) {
    const held = Array.from(bubble.instanceMatrix.array), time = kit.inspect().time;
    kit.update(50, option); assert.equal(kit.inspect().time, time);
    assert.deepEqual(Array.from(bubble.instanceMatrix.array), held);
  }
  const time = kit.inspect().time; kit.update(999); assert.equal(kit.inspect().time, time + .05);
  meshes.forEach((m, i) => { assert.equal(m.geometry, resources[i][0]); assert.equal(m.material, resources[i][1]); });
  const geometries = new Set(meshes.map(m => m.geometry)), materials = new Set(meshes.map(m => m.material));
  let retired = 0;
  for (const resource of [...geometries, ...materials]) resource.addEventListener('dispose', () => retired++);
  kit.dispose(); kit.dispose(); assert.equal(kit.object.parent, null);
  assert.equal(retired, geometries.size + materials.size, 'shared resources retire exactly once');
});

test('garage builds scenic shaders before wardrobe warmup and preserves existing framing and accepted faces', () => {
  const source = readFileSync(new URL('../render/garage.js', import.meta.url), 'utf8');
  assert.ok(source.indexOf('createGarageBackdrop(scene)') < source.indexOf('await warmGarageWardrobe'));
  assert.match(source, /backdrop\.update\(dt,\{reducedMotion:motionReduced,gentle\}\)/);
  assert.match(source, /backdrop\.dispose\(\)/);
  assert.match(source, /faceUv\.value\.set\(face\.col\*\.25,face\.row\*\.25\)/);
  assert.match(source, /camera\.setViewOffset/);
});
