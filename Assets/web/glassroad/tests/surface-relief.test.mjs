import { test } from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
const hook = registerHooks({ resolve(s, c, next) {
  const files = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[s] ? { url: new URL('../vendor/three/build/' + files[s], import.meta.url).href, shortCircuit: true } : next(s, c);
} });
const { reliefNormal } = await import('../render/surfaceRelief.js');
const { createMachineMaterial } = await import('../render/mainframeMaterials.js');
const { createGoonworksMaterial } = await import('../render/goonworksMaterials.js');
const { keepStoneMaterial } = await import('../render/keepFortress.js');
const { vec3, float, sin, positionGeometry } = await import('three/tsl');
hook.deregister();

const nodesOf = root => { const seen = new Set(), todo = [root];
  while (todo.length) { const node = todo.pop(); if (seen.has(node)) continue; seen.add(node); todo.push(...node.getChildren()); }
  return [...seen];
};

test('surface relief has bounded spatial derivatives without textures, clocks or geometry edits', () => {
  const N = reliefNormal(vec3(0, 1, 0), sin(positionGeometry.x).mul(.04), float(.01));
  const nodes = nodesOf(N), methods = new Set(nodes.map(n => n.method));
  assert.ok(methods.has('dFdx') && methods.has('dFdy') && methods.has('normalize'));
  assert.ok(methods.has('max') && methods.has('smoothstep'), 'slope/degenerate guards and distance filtering remain');
  assert.equal(nodes.some(n => n.isTextureNode || n.isUniformNode), false);
  assert.ok(nodes.length < 140, `shared relief graph stays small (${nodes.length})`);
});

test('all dry material families keep silhouette geometry, opaque depth and source attributes', () => {
  const materials = [createMachineMaterial('#839896', null), createMachineMaterial('#839896', null, { vent: true }),
    createGoonworksMaterial('#e78eb8'), createGoonworksMaterial('#ede7e6', { paper: true }),
    keepStoneMaterial('#521744', null, true, false), keepStoneMaterial('#521744', null, false, false, 'iron')];
  for (const material of materials) {
    assert.equal(material.positionNode, null);
    assert.equal(material.transparent, false); assert.equal(material.depthWrite, true);
    const nodes = nodesOf(material.colorNode);
    assert.ok(nodes.some(n => n.method === 'dFdx'), material.name + ' has real directional relief');
    assert.ok(!nodes.some(n => n.isTextureNode), 'relief adds no downloads when art is absent');
    material.dispose();
  }
  const keep = keepStoneMaterial('#521744', null, true, false);
  assert.ok(!nodesOf(keep.colorNode).some(n => n.getAttributeName?.() === 'uv'), 'accepted UV-less Keep arches remain supported');
  keep.dispose();
});

test('machine and toy colourways are uniform data rather than separate compiled shader literals', () => {
  for (const factory of [hex => createMachineMaterial(hex, null), hex => createGoonworksMaterial(hex)]) {
    for (const hex of ['#123456', '#abcdef']) {
      const material = factory(hex), colours = nodesOf(material.colorNode).filter(n => n.isUniformNode && n.value?.isColor);
      assert.ok(colours.some(n => '#' + n.value.getHexString() === hex), hex);
      material.dispose();
    }
  }
});
