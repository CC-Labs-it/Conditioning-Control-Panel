import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
const hook = registerHooks({ resolve(s, c, next) {
  const files = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[s] ? { url: new URL('../vendor/three/build/' + files[s], import.meta.url).href, shortCircuit: true } : next(s, c);
} });
const { createMachineMaterial } = await import('../render/mainframeMaterials.js');
const { createRoadMaterial } = await import('../render/materials/road.js');
const { createPalette } = await import('../render/materials/palette.js');
const { DataTexture } = await import('three/webgpu');
hook.deregister();
const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/mainframe.json', import.meta.url))));
const maps = Object.fromEntries(['detail-gold-wear', 'detail-porcelain-roughness'].map(id => [id, new DataTexture(new Uint8Array(16), 2, 2)]));
const art = { detail: id => maps[id], tex: () => null };

test('moving machine wear samples geometry coordinates and never follows the world', () => {
  const material = createMachineMaterial('#839896', art), textures = [];
  material.colorNode.traverse(node => { if (node.isTextureNode && node.value === maps['detail-gold-wear']) textures.push(node); });
  assert.ok(textures.length > 0);
  for (const node of textures) {
    const attributes = []; node.uvNode.traverse(child => { if (child.getAttributeName) attributes.push(child.getAttributeName()); });
    assert.deepEqual([...new Set(attributes)], ['position'], 'raw source position attribute owns the wear coordinates');
  }
  assert.equal(material.positionNode, null, 'surface relief does not alter track or prop geometry');
  material.dispose();
});

test('Mainframe floor keeps the two existing detail maps shared and stays opaque without displacement', () => {
  const { material } = createRoadMaterial(createPalette(), art, track), seen = new Set();
  material.colorNode.traverse(node => { if (node.isTextureNode) seen.add(node.value); });
  for (const map of Object.values(maps)) assert.ok(seen.has(map), 'existing loaded texture reused directly');
  assert.equal(material.transparent, false); assert.equal(material.positionNode, null);
  material.dispose();
});

test('machinery still builds without art, including vent and luminous variants', () => {
  for (const options of [{}, { vent: true }, { glow: true }]) {
    const material = createMachineMaterial('#839896', null, options);
    assert.ok(material.colorNode?.isNode); assert.equal(material.transparent, false); material.dispose();
  }
});
