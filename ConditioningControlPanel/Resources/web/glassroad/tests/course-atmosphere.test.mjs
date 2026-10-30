import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
const hook = registerHooks({ resolve(name, context, next) {
  const file = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' }[name];
  return file ? { url: new URL('../vendor/three/build/' + file, import.meta.url).href, shortCircuit: true } : next(name, context);
} });
const { createCourseAtmosphere } = await import('../render/courseAtmosphere.js');
const { Group, Vector3 } = await import('three/webgpu');
hook.deregister();

test('course switches retire every owned atmosphere resource and leave Threshold to its existing system', () => {
  const scene = new Group(), playerPos = new Vector3(100, 20, -50);
  assert.equal(createCourseAtmosphere({ id: 'threshold' }), null);
  assert.equal(createCourseAtmosphere({ id: 'unknown' }), null);
  for (const phone of [false, true]) for (const id of ['mainframe', 'keep', 'boudoir', 'goonworks']) {
    const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/' + id + '.json', import.meta.url), 'utf8')));
    const atmosphere = createCourseAtmosphere(track, { phone });
    assert.ok(atmosphere.object.isGroup); scene.add(atmosphere.object);
    const resources = new Map();
    atmosphere.object.traverse(mesh => {
      for (const resource of [mesh.geometry, mesh.material].filter(Boolean)) {
        if (resources.has(resource)) continue;
        resources.set(resource, 0);
        resource.addEventListener('dispose', () => resources.set(resource, resources.get(resource) + 1));
      }
    });
    atmosphere.update(.1, { playerPos, songS: 12 });
    atmosphere.update(.1, { playerPos, paused: true });
    atmosphere.update(.1, { playerPos, reducedMotion: true });
    atmosphere.reset(); atmosphere.dispose(); atmosphere.dispose();
    atmosphere.update(.1, { playerPos }); atmosphere.reset();
    assert.equal(scene.children.length, 0, id + ' detaches when leaving');
    assert.ok(resources.size > 0);
    for (const count of resources.values()) assert.equal(count, 1, id + ' owns and frees each shared resource once');
  }
});
