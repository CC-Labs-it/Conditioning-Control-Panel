import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const src = (p) => readFileSync(new URL(p, import.meta.url), 'utf8');

// The rival name tag's material is made on the first race pose (render/karts.js tagSprite); its program linked in
// the first race frame (Firefox WebGL2, GOONWORKS + Threshold, 2026-10-27). The warm now builds the same material.
test('the warm carries a name-tag sprite material that matches tagSprite', async () => {
  const { lateWarmMaterials } = await import('../render/warmExtras.js');
  assert.deepEqual(lateWarmMaterials({}), [], 'no document or no classes: nothing (node tests)');
  const made = [];
  globalThis.document = { createElement: () => ({ width: 0, height: 0 }) };
  try {
    class CanvasTexture { constructor(image) { this.image = image; } }
    class SpriteNodeMaterial { constructor(p) { Object.assign(this, p); this.isSpriteNodeMaterial = true; made.push(this); } }
    const [tag, human, ...rest] = lateWarmMaterials({ CanvasTexture, SpriteNodeMaterial, SRGBColorSpace: 'srgb' });
    assert.equal(rest.length, 0);
    assert.equal(tag.isSpriteNodeMaterial, true); assert.equal(tag.transparent, true); assert.equal(tag.depthWrite, false);
    assert.equal(tag.map.colorSpace, 'srgb'); assert.equal(tag.map.image.width, 256); assert.equal(tag.map.image.height, 64);
    assert.equal(lateWarmMaterials({ CanvasTexture, SpriteNodeMaterial })[0], tag, 'built once per page');
    assert.equal(human.depthTest, false, 'the human tag (humanTagSprite) draws over traffic'); assert.equal(human.depthWrite, false);
    assert.equal(human.map.image.width, 512); assert.equal(human.map.image.height, 128);
    assert.equal(made.length, 2);
  } finally { delete globalThis.document; }
  assert.match(src('../render/karts.js'), /new SpriteNodeMaterial\(\{ map: t, transparent: true, depthWrite: false \}\)/,
    'tagSprite changed: keep render/warmExtras.js in step');
  assert.match(src('../render/karts.js'), /new SpriteNodeMaterial\(\{ map: t, transparent: true, depthWrite: false, depthTest: false \}\)/,
    'humanTagSprite changed: keep render/warmExtras.js in step');
  const r = src('../render/renderer.js');
  assert.match(r, /\[\]\.concat\(extras \|\| \[\], lateWarmMaterials\(GPU\)\)/, 'warmAll');
  assert.match(r, /for \(const m of lateWarmMaterials\(GPU\)\) late\.add/, 'warmInset');
});
