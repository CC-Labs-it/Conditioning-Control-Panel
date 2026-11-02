import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { markRegion, regionPlan, uploadCanvasRegions } from '../render/canvasRegions.js';

// a WebGL backend stand-in: three's full upload records itself, gl.texSubImage2D records the region uploads
function webgl() {
  const full = [], subs = [], params = [], data = new Map();
  const gl = { TEXTURE_2D: 0x0de1, texSubImage2D(...a) { subs.push(a); } };
  const backend = { isWebGLBackend: true, gl, state: { bindTexture() {} }, get(t) { if (!data.has(t)) data.set(t, {}); return data.get(t); } };
  const utils = {
    gl, backend,
    updateTexture(texture, options) { full.push(options); const d = backend.get(texture); if (d.textureGPU === undefined) Object.assign(d, { textureGPU: {}, glTextureType: 0x0de1, glFormat: 1, glType: 2 }); },
    setTextureParameters(type, texture) { params.push(texture.flipY); },
  };
  backend.textureUtils = utils;
  return { three: { backend }, utils, full, subs, params, data };
}
const canvas = (width, height) => ({ width, height });

test('a canvas atlas re-uploads only its marked cells after the first full upload, flipY placed from the bottom', () => {
  const g = webgl();
  assert.equal(uploadCanvasRegions(g.three), true);
  assert.equal(uploadCanvasRegions(g.three), false, 'installs once');
  const atlas = canvas(1280, 1280), tex = { image: atlas, flipY: true, userData: {} }, cell = canvas(256, 256);
  // the first upload is whole even with a cell queued (no texture on the GPU yet)
  markRegion(tex, 256, 0, cell);
  g.utils.updateTexture(tex, { image: atlas, width: 1280, height: 1280 });
  assert.equal(g.full.length, 1); assert.equal(g.subs.length, 0);
  assert.equal(tex.userData.__regions.length, 0, 'a full upload carries the queued cells');
  // then cells only: canvas y 0 (the top row) lands at texture y 1280 - 0 - 256 under flipY
  markRegion(tex, 256, 0, cell); markRegion(tex, 256, 0, cell);
  const other = canvas(256, 256);
  markRegion(tex, 1024, 1024, other);
  g.utils.updateTexture(tex, { image: atlas, width: 1280, height: 1280 });
  assert.equal(g.full.length, 1, 'no second full upload');
  assert.deepEqual(g.subs.map((a) => a.slice(1, 4)), [[0, 256, 1024], [0, 1024, 0]]);
  assert.equal(g.subs[0][6], cell); assert.equal(g.subs[1][6], other);
  assert.deepEqual(g.params, [true], 'the same unpack state as a full upload');
  // without flipY the offset is the canvas y
  tex.flipY = false; markRegion(tex, 0, 512, cell);
  g.utils.updateTexture(tex, { image: atlas, width: 1280, height: 1280 });
  assert.deepEqual(g.subs[2].slice(1, 4), [0, 0, 512]);
});

test('a resized canvas, a rebuilt GPU texture, an out-of-bounds cell or a texture without cells goes whole', () => {
  const g = webgl();
  uploadCanvasRegions(g.three);
  const tex = { image: canvas(512, 512), flipY: true, userData: {} }, cell = canvas(256, 256);
  markRegion(tex, 0, 0, cell); g.utils.updateTexture(tex, {});           // first: whole
  tex.image = canvas(1024, 1024); markRegion(tex, 0, 0, cell); g.utils.updateTexture(tex, {});
  assert.equal(g.full.length, 2, 'resized');
  g.data.get(tex).textureGPU = {}; markRegion(tex, 0, 0, cell); g.utils.updateTexture(tex, {});
  assert.equal(g.full.length, 3, 'rebuilt by three');
  markRegion(tex, 900, 0, cell); g.utils.updateTexture(tex, {});
  assert.equal(g.full.length, 4, 'a cell past the edge');
  const plain = { image: canvas(64, 64), flipY: true, userData: {} };
  g.utils.updateTexture(plain, {}); g.utils.updateTexture(plain, {});
  assert.equal(g.full.length, 6); assert.equal(g.subs.length, 0);
  assert.equal(regionPlan(plain, plain.image, g.data.get(plain)), null);
});

test('WebGPU and an unknown backend are left alone; the renderer installs it and the living icons mark their cells', () => {
  assert.equal(uploadCanvasRegions({ backend: { isWebGPUBackend: true, textureUtils: {} } }), false);
  assert.equal(uploadCanvasRegions(null), false);
  const here = (p) => readFileSync(new URL(p, import.meta.url), 'utf8');
  assert.match(here('../render/renderer.js'), /uploadCanvasRegions\(three\)/);
  const living = here('../render/items/living.js');
  assert.match(living, /markRegion\(tex, x, y, cell\)/);
  assert.match(living, /atlas\.drawImage\(cell, x, y\)/, 'the atlas still carries every cell for a full upload');
});
