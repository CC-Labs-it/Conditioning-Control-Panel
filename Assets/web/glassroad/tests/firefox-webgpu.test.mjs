import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { 'three': 'three.webgpu.min.js', 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true }
    : next(specifier, context);
} });
const { WGSLNodeBuilder } = await import('three/webgpu');
const { isFirefox, concreteFloat, concreteFloatConstants, videoBlitWgsl, createVideoBlit, uploadVideoThroughExternalTexture } = await import('../render/firefoxWebgpu.js');
hooks.deregister();
const vendor = readFileSync(new URL('../vendor/three/build/three.webgpu.min.js', import.meta.url), 'latin1');

test('isFirefox reads the user agent', () => {
  assert.equal(isFirefox({ userAgent: 'Mozilla/5.0 (Windows NT 10.0; rv:158.0) Gecko/20100101 Firefox/158.0' }), true);
  assert.equal(isFirefox({ userAgent: 'Mozilla/5.0 AppleWebKit/537.36 Chrome/141.0 Safari/537.36 Edg/141.0' }), false);
  assert.equal(isFirefox(null), false);
});

test('a float literal becomes a concrete f32 constant; everything else passes through', () => {
  for (const [a, b] of [['0.55', 'f32( 0.55 )'], ['1.0', 'f32( 1.0 )'], ['-2.5', 'f32( -2.5 )'], ['1e-7', 'f32( 1e-7 )'], ['3', 'f32( 3 )']]) assert.equal(concreteFloat(a), b);
  for (const s of ['nodeVar0', 'vec3<f32>( 1.0, 1.0, 1.0 )', 'f32( 0.5 )', '1u', 'true']) assert.equal(concreteFloat(s), s);
});

test('Firefox WGSL: float constants are concrete f32 (naga has no constant mix on abstract floats)', () => {
  const proto = WGSLNodeBuilder.prototype, stock = proto.generateConst;
  const before = stock.call(Object.create(proto), 'float', 0.55);
  assert.equal(before, '0.55', 'stock r186 writes a bare literal: re-check render/firefoxWebgpu.js after an upgrade');
  try {
    assert.equal(concreteFloatConstants(WGSLNodeBuilder), true);
    assert.equal(concreteFloatConstants(WGSLNodeBuilder), false, 'installs once');
    const b = Object.create(proto);
    assert.equal(b.generateConst('float', 0.55), 'f32( 0.55 )');
    assert.equal(b.generateConst('float', 1), 'f32( 1.0 )');
    assert.equal(b.generateConst('int', 3), '3');
    assert.equal(b.generateConst('uint', 3), '3u');
    assert.equal(b.generateConst('bool', true), 'true');
  } finally { proto.generateConst = stock; }
  assert.match(vendor, /"float"===e\)return _toFloat\(t\)/, 'NodeBuilder.generateConst moved: re-check the f32 patch');
});

test('the vendored r186 WebGPU upload still swallows a refused copyExternalImageToTexture (the blit replaces it)', () => {
  assert.match(vendor, /_copyImageToTexture\(e,t,r,i,n,o,a=0\)\{const s=this\.backend\.device/, '_copyImageToTexture moved: re-check the video blit');
  assert.match(vendor, /s\.queue\.copyExternalImageToTexture\(_copyExternalImageSourceInfo,_copyExternalImageDestInfo,_extent3D\$1\)\}catch\(e\)\{\}/);
  assert.match(vendor, /this\._copyImageToTexture\(t\.image,r\.texture,n,0,e\.flipY,e\.premultiplyAlpha\)/);
});

test('the blit shader flips like flipY and decodes to linear for an -srgb target', () => {
  const plain = videoBlitWgsl(), flipped = videoBlitWgsl({ flipY: true }), srgb = videoBlitWgsl({ decode: true });
  assert.match(plain, /texture_external/); assert.match(plain, /textureSampleBaseClampToEdge/);
  assert.match(plain, /0\.5 - p\.y \* 0\.5/); assert.match(flipped, /0\.5 \+ p\.y \* 0\.5/);
  assert.doesNotMatch(plain, /pow\(/); assert.match(srgb, /pow\(\(c\.rgb \+ vec3f\(0\.055\)\) \/ 1\.055, vec3f\(2\.4\)\)/);
});

class FakeVideo { constructor(ready = 4) { this.readyState = ready; this.videoWidth = ready >= 2 ? 854 : 0; } }
function fakeDevice() {
  const log = [];
  const device = {
    log,
    createSampler: (d) => ({ sampler: d }),
    createShaderModule: (d) => { log.push(['module', d.code]); return { code: d.code }; },
    createRenderPipeline: (d) => { log.push(['pipeline', d.fragment.targets[0].format]); return { d, getBindGroupLayout: () => ({}) }; },
    importExternalTexture: ({ source }) => { log.push(['import', source]); return { external: source }; },
    createBindGroup: (d) => ({ bg: d }),
    createCommandEncoder: () => ({
      beginRenderPass: (d) => { log.push(['pass', d.colorAttachments[0].view]); return { setPipeline() {}, setBindGroup() {}, draw: (n) => log.push(['draw', n]), end() {} }; },
      finish: () => 'cmd',
    }),
    queue: { submit: (c) => log.push(['submit', c.length]) },
  };
  return device;
}

test('Firefox WebGPU: a <video> uploads through the external-texture blit, everything else through three', () => {
  globalThis.HTMLVideoElement = FakeVideo;
  try {
    const device = fakeDevice(), copies = [];
    const utils = { _copyImageToTexture(...a) { copies.push(a); return 'three'; } };
    const three = { backend: { isWebGPUBackend: true, device, textureUtils: utils } };
    assert.equal(uploadVideoThroughExternalTexture(three), true);
    assert.equal(uploadVideoThroughExternalTexture(three), false, 'installs once');
    const views = [];
    const tex = { createView: (d) => { views.push(d); return 'view'; } };
    const v = new FakeVideo();
    assert.equal(utils._copyImageToTexture(v, tex, { format: 'rgba8unorm-srgb' }, 0, true, false), undefined);
    assert.equal(copies.length, 0, 'three never sees the video');
    assert.deepEqual(device.log.filter((x) => x[0] !== 'module').map((x) => x[0]), ['pipeline', 'import', 'pass', 'draw', 'submit']);
    assert.equal(device.log.find((x) => x[0] === 'draw')[1], 3);
    assert.match(device.log.find((x) => x[0] === 'module')[1], /0\.5 \+ p\.y \* 0\.5[\s\S]*pow\(/, 'flipped and decoded for -srgb');
    assert.deepEqual(views[0], { dimension: '2d', baseMipLevel: 0, mipLevelCount: 1, baseArrayLayer: 0, arrayLayerCount: 1 });
    utils._copyImageToTexture(v, tex, { format: 'rgba8unorm-srgb' }, 0, true, false);
    assert.equal(device.log.filter((x) => x[0] === 'pipeline').length, 1, 'one pipeline per format and flip');
    assert.equal(device.log.filter((x) => x[0] === 'import').length, 2, 'a fresh external texture per frame');
    // not a video, a mip level, or a video without a frame: three's path (or nothing)
    const img = {};
    assert.equal(utils._copyImageToTexture(img, tex, { format: 'rgba8unorm' }, 0, true, false), 'three');
    assert.equal(utils._copyImageToTexture(v, tex, { format: 'rgba8unorm' }, 0, true, false, 2), 'three');
    const before = device.log.length;
    assert.equal(utils._copyImageToTexture(new FakeVideo(1), tex, { format: 'rgba8unorm' }, 0, true, false), undefined);
    assert.equal(device.log.length, before, 'no frame yet: no blit, no throw');
    assert.equal(copies.length, 2);
    // a device that refuses the import falls back to three
    device.importExternalTexture = () => { throw new Error('nope'); };
    assert.equal(utils._copyImageToTexture(v, tex, { format: 'rgba8unorm' }, 0, false, false), 'three');
  } finally { delete globalThis.HTMLVideoElement; }
});

test('WebGL, and WebGPU without importExternalTexture, keep three untouched', () => {
  const gl = { backend: { isWebGLBackend: true, textureUtils: { _copyImageToTexture() {} } } };
  assert.equal(uploadVideoThroughExternalTexture(gl), false);
  const d = fakeDevice(); delete d.importExternalTexture;
  const utils = { _copyImageToTexture() {} }, before = utils._copyImageToTexture;
  assert.equal(uploadVideoThroughExternalTexture({ backend: { isWebGPUBackend: true, device: d, textureUtils: utils } }), false);
  assert.equal(utils._copyImageToTexture, before);
  assert.equal(typeof createVideoBlit(fakeDevice()).draw, 'function');
});

test('renderer.js applies both Firefox patches only on a Firefox WebGPU backend', () => {
  const src = readFileSync(new URL('../render/renderer.js', import.meta.url), 'utf8');
  assert.match(src, /if \(backend === 'webgpu' && isFirefox\(\)\) \{ concreteFloatConstants\(threeWebgpu\.WGSLNodeBuilder\); uploadVideoThroughExternalTexture\(three\); \}/);
});
