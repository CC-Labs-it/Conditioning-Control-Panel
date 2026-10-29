import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { markRows, clearRows, rowPlan, uploadDataTextureRows } from '../render/dataTextureRows.js';
import { createWaterField } from '../render/waterField.js';

// a WebGL backend stand-in: three's full upload records itself, gl.texSubImage2D records the row uploads
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
const image = (width, height, bpt = 4) => ({ data: new Uint8Array(width * height * bpt), width, height });

test('a data texture re-sends only its marked rows after the first full upload, from the same array', () => {
  const g = webgl();
  assert.equal(uploadDataTextureRows(g.three), true);
  assert.equal(uploadDataTextureRows(g.three), false, 'installs once');
  const img = image(64, 997), tex = { image: img, flipY: false, userData: {} };
  markRows(tex, 100, 180);
  g.utils.updateTexture(tex, { image: img });
  assert.equal(g.full.length, 1); assert.equal(g.subs.length, 0);
  assert.equal(tex.userData.__rows, undefined, 'a full upload carries the marked rows');
  markRows(tex, 120, 150); markRows(tex, 100, 130); markRows(tex, 140, 180);   // marks union
  g.utils.updateTexture(tex, { image: img });
  assert.equal(g.full.length, 1, 'no second full upload');
  assert.equal(g.subs.length, 1);
  const [target, level, x, y, w, h, fmt, type, arr, offset] = g.subs[0];
  assert.deepEqual([target, level, x, y, w, h, fmt, type], [0x0de1, 0, 0, 100, 64, 81, 1, 2]);
  assert.equal(arr, img.data); assert.equal(offset, 100 * 64 * 4, 'the span starts at its row in the same array');
  assert.deepEqual(g.params, [false], 'the same unpack state as a full upload');
  // flipY: the span lands from the bottom
  tex.flipY = true; markRows(tex, 0, 9);
  g.utils.updateTexture(tex, { image: img });
  assert.equal(g.subs[1][3], 997 - 0 - 10); assert.equal(g.subs[1][5], 10);
  // marks past the edge are clipped; an unmarked upload goes whole
  markRows(tex, 990, 2000); g.utils.updateTexture(tex, { image: img });
  assert.deepEqual(g.subs[2].slice(3, 6), [0, 64, 7]);
  g.utils.updateTexture(tex, { image: img }); assert.equal(g.full.length, 2);
  // cleared marks go whole too
  markRows(tex, 1, 2); clearRows(tex); g.utils.updateTexture(tex, { image: img }); assert.equal(g.full.length, 3);
});

test('a resized image, a rebuilt GPU texture, a non-2D target or a 16-bit odd offset goes whole', () => {
  const g = webgl();
  uploadDataTextureRows(g.three);
  const tex = { image: image(32, 50), flipY: false, userData: {} };
  markRows(tex, 0, 1); g.utils.updateTexture(tex, {});                  // first: whole
  tex.image = image(32, 60); markRows(tex, 0, 1); g.utils.updateTexture(tex, {});
  assert.equal(g.full.length, 2, 'resized');
  g.data.get(tex).textureGPU = {}; markRows(tex, 0, 1); g.utils.updateTexture(tex, {});
  assert.equal(g.full.length, 3, 'rebuilt by three');
  markRows(tex, 5, 6); g.utils.updateTexture(tex, {});
  assert.equal(g.subs.length, 1, 'then rows');
  g.data.get(tex).glTextureType = 0x806f; markRows(tex, 5, 6); g.utils.updateTexture(tex, {});
  assert.equal(g.full.length, 4, 'a 3D target');
  const plain = { image: image(8, 8), flipY: false, userData: {} };
  g.utils.updateTexture(plain, {}); g.utils.updateTexture(plain, {});
  assert.equal(g.full.length, 6);
  assert.equal(rowPlan(plain, plain.image, g.data.get(plain)), null);
  const odd = { image: { data: new Uint16Array(3 * 4), width: 3, height: 4 }, flipY: false, userData: {} };
  g.utils.updateTexture(odd, {}); markRows(odd, 1, 1);
  assert.equal(rowPlan(odd, odd.image, g.data.get(odd)), null, 'a row start that is not a whole element goes whole');
});

const fixture = () => ({ lengthM: 120, nodeS: [0, 30, 40, 70, 80, 120], sections: [], frameAt(s) { return { w: 20, n: [0, 1, 0], surface: s < 30 || s >= 40 && s < 70 ? 'stream' : 'glass', flags: [] }; } });

test('the water field reports the rows its pack touched: a disturbed run with its guard rows, nothing of a resting run', () => {
  const f = createWaterField(fixture()), [a, b] = f.runs;
  assert.equal(f.stats.rowFrom, a.rowOffset - 1, 'the first pack settles every run');
  assert.equal(f.stats.rowTo, b.rowOffset + b.ny);
  f.disturb({ s: b.fromS + b.ds * 5, x: 0, radius: 3, strength: .3 }); f.step(0.05);
  assert.equal(f.stats.rowFrom, b.rowOffset - 1); assert.equal(f.stats.rowTo, b.rowOffset + b.ny);
  assert.ok(f.stats.rowFrom > a.rowOffset + a.ny, 'the resting run is outside the span');
  assert.equal(f.stats.rowTo - f.stats.rowFrom + 1, b.ny + 2, 'the run and its two guard rows');
  assert.ok(f.stats.rowTo - f.stats.rowFrom + 1 < f.height, 'under the whole atlas');
});

test('WebGPU is left alone; the renderer installs it and the water volume marks its rows', () => {
  assert.equal(uploadDataTextureRows({ backend: { isWebGPUBackend: true, textureUtils: {} } }), false);
  assert.equal(uploadDataTextureRows(null), false);
  const here = (p) => readFileSync(new URL(p, import.meta.url), 'utf8');
  assert.match(here('../render/renderer.js'), /uploadDataTextureRows\(three\)/);
  const water = here('../render/waterVolume.js');
  assert.match(water, /markRows\(heightTexture, /);
  assert.match(water, /clearRows\(heightTexture\)/, 'a reset goes whole');
});
