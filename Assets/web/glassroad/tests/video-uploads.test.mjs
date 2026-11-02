import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { 'three': 'three.webgpu.min.js', 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true }
    : next(specifier, context);
} });
const { Texture, VideoTexture, DataTexture } = await import('three/webgpu');
const { uploadVideoElementsDirectly, plainImageTexture } = await import('../render/videoUploads.js');
hooks.deregister();

class FakeVideo {}
function webgl() {
  const calls = [];
  const utils = { updateTexture(texture, options) { calls.push({ self: this, texture, options }); return 'done'; } };
  return { calls, utils, three: { backend: { isWebGLBackend: true, textureUtils: utils } } };
}

test('WebGL hands a <video> on a plain Texture to three as a .data holder, everything else untouched', () => {
  globalThis.HTMLVideoElement = FakeVideo;
  try {
    const g = webgl(), el = new FakeVideo();
    assert.equal(uploadVideoElementsDirectly(g.three), true);
    assert.equal(uploadVideoElementsDirectly(g.three), false, 'installs once');
    const tex = new Texture(), options = { image: el, width: 1280, height: 720, depth: 1, levels: 1 };
    assert.equal(g.utils.updateTexture(tex, options), 'done');
    const sent = g.calls[0].options;
    assert.equal(g.calls[0].self, g.utils); assert.equal(g.calls[0].texture, tex);
    assert.equal(sent.image.data, el, 'getImage falls through to .data: texSubImage2D(<video>)');
    assert.deepEqual([sent.image.width, sent.image.height, sent.width, sent.height, sent.depth, sent.levels], [1280, 720, 1280, 720, 1, 1]);
    assert.equal(options.image, el, 'the caller options are not mutated');
    const canvasOptions = { image: { width: 2, height: 2 }, width: 2, height: 2 };
    g.utils.updateTexture(tex, canvasOptions); assert.equal(g.calls[1].options, canvasOptions);
    const video = new VideoTexture(el), videoOptions = { image: el };
    video.dispose(); g.utils.updateTexture(video, videoOptions); assert.equal(g.calls[2].options, videoOptions, 'three keeps its own VideoTexture path');
    assert.equal(plainImageTexture(new DataTexture()), false);
    const mipped = new Texture(); mipped.mipmaps = [{}]; assert.equal(plainImageTexture(mipped), false);
    assert.equal(plainImageTexture(tex), true);
  } finally { delete globalThis.HTMLVideoElement; }
});

test('WebGPU and pages without HTMLVideoElement keep three untouched', () => {
  const webgpu = { backend: { isWebGPUBackend: true, textureUtils: { updateTexture() {} } } };
  const before = webgpu.backend.textureUtils.updateTexture;
  assert.equal(uploadVideoElementsDirectly(webgpu), false);
  assert.equal(webgpu.backend.textureUtils.updateTexture, before, 'copyExternalImageToTexture already takes a video');
  const g = webgl(); uploadVideoElementsDirectly(g.three);
  const options = { image: {}, width: 1, height: 1 };
  g.utils.updateTexture(new Texture(), options); assert.equal(g.calls[0].options, options);
});

test('the vendored r186 WebGL getImage still answers .data for an unknown source (the holder relies on it)', () => {
  const src = readFileSync(new URL('../vendor/three/build/three.webgpu.min.js', import.meta.url), 'latin1');
  const body = /function getImage\(e\)\{(return [^}]*)\}class WebGLExtensions/.exec(src);
  assert.ok(body, 'getImage moved: re-check render/videoUploads.js against the new vendor build');
  const getImage = new Function('e', body[1]);
  const el = {}; assert.equal(getImage({ data: el, width: 4, height: 4 }), el);
  assert.match(src, /e\.isVideoTexture\)e\.update\(\),r\.texImage2D/, 'VideoTexture keeps its own branch');
});
