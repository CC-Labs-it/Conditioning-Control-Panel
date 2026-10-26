// Every InstancedMesh takes the instance-attribute path, so meshes of one material share one vertex program.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { 'three': 'three.webgpu.min.js', 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { shareInstancePrograms } = await import('../render/renderer.js');
hooks.deregister();

test('the backend reports no uniform-buffer room for instance matrices, on both backends', () => {
  for (const limit of [65536, 16384]) {
    const caps = { getUniformBufferLimit: () => limit };
    shareInstancePrograms({ backend: { capabilities: caps } });
    assert.equal(caps.getUniformBufferLimit(), 0);
  }
  assert.doesNotThrow(() => shareInstancePrograms({ backend: null }));
  assert.doesNotThrow(() => shareInstancePrograms({}));
});

test('the renderer shares instance programs right after it is built', () => {
  const src = readFileSync(new URL('../render/renderer.js', import.meta.url), 'utf8');
  const made = src.indexOf('trackSceneResources(three);'), shared = src.indexOf('shareInstancePrograms(three);');
  assert.ok(made > 0 && shared > made, 'after the renderer exists, before anything compiles');
  assert.ok(shared < src.indexOf('three.info.autoReset'));
});
