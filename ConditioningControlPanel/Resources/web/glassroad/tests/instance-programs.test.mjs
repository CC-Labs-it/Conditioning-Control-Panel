// Every InstancedMesh takes the instance-attribute path, so meshes of one material share one vertex program.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { shareInstancePrograms } from '../render/instancePrograms.js';

test('the backend reports no uniform-buffer room for instance matrices, on both backends', () => {
  for (const limit of [65536, 16384]) {
    const caps = { getUniformBufferLimit: () => limit };
    shareInstancePrograms({ backend: { capabilities: caps } });
    assert.equal(caps.getUniformBufferLimit(), 0);
  }
  assert.doesNotThrow(() => shareInstancePrograms({ backend: null }));
  assert.doesNotThrow(() => shareInstancePrograms({}));
  assert.doesNotThrow(() => shareInstancePrograms(null));
});

test('the renderer shares instance programs right after it is built, before anything compiles', () => {
  const src = readFileSync(new URL('../render/renderer.js', import.meta.url), 'utf8');
  assert.match(src, /import \{ shareInstancePrograms \} from '\.\/instancePrograms\.js'/);
  const made = src.indexOf('trackSceneResources(three);'), shared = src.indexOf('shareInstancePrograms(three);');
  assert.ok(made > 0 && shared > made);
  assert.ok(shared < src.indexOf('three.info.autoReset'));
});
