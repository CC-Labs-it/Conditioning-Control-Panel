import { test } from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';

const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { 'three': 'build/three.webgpu.min.js', 'three/webgpu': 'build/three.webgpu.min.js', 'three/tsl': 'build/three.tsl.min.js' };
  const path = files[specifier] || (specifier.startsWith('three/addons/') ? specifier.slice(6) : null);
  return path ? { url: new URL('../vendor/three/' + path, import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { Renderer, Scene, PerspectiveCamera } = await import('three/webgpu');
const { createPost } = await import('../render/post.js');
hooks.deregister();

test('real vendored compiler selects next direct-render depth or an explicit nested-pass depth', async () => {
  const depths = [], context = { clippingContext: null };
  const list = { begin() {}, finish() {}, opaque: [], transparent: [], transparentDoublePass: [], lightsNode: {} };
  const renderer = {
    _initialized: true, _isDeviceLost: false, _callDepth: 0, shadowMap: {}, xr: { isPresenting: false },
    _nodes: { nodeFrame: { renderId: 7, update() {} } }, _mrt: null,
    _renderTarget: null, _outputRenderTarget: null, _activeMipmapLevel: 0,
    _currentRenderContext: null, _currentRenderObjectFunction: null, _handleObjectFunction: null, _compilationPromises: null,
    _renderContexts: { get(target, mrt, depth) { depths.push(depth); return context; } },
    _renderLists: { get() { return list; } }, _background: { update() {} },
    _projectObject() {}, _updateCamera(camera) { return camera; },
  };
  const scene = new Scene(), camera = new PerspectiveCamera();
  await Renderer.prototype.compileAsync.call(renderer, scene, camera);
  renderer._callDepth = 3;
  await Renderer.prototype.compileAsync.call(renderer, scene, camera);
  await Renderer.prototype.compileAsync.call(renderer, scene, camera, null, null, 9);
  assert.deepEqual(depths, [1, 4, 9]);
  assert.equal(renderer._callDepth, 3, 'compilation never changes the live recursion counter');
  assert.equal(renderer._nodes.nodeFrame.renderId, 7);
  assert.equal(renderer._currentRenderContext, null);
});

for (const failure of [false, true]) test('real post compiler restores target/MRT after ' + (failure ? 'rejection' : 'success'), async () => {
  const originalTarget = { name: 'owner-target' }, originalMrt = { name: 'owner-mrt' };
  let target = originalTarget, mrt = originalMrt, observed;
  const scene = new Scene(), camera = new PerspectiveCamera();
  const renderer = {
    _callDepth: 3,
    getRenderTarget() { return target; }, setRenderTarget(value) { target = value; },
    getMRT() { return mrt; }, setMRT(value) { mrt = value; },
    async compileAsync(...args) {
      observed = { args, target, mrt, depth: this._callDepth };
      await Promise.resolve();
      assert.equal(this._callDepth, 3, 'no shared counter mutation across the await');
      if (failure) throw new Error('compile failed');
    },
  };
  const originalWarn = console.warn;
  let post;
  try {
    // Screen decoration requires a DOM; the actual post compiler and scene pass do not.
    console.warn = () => {};
    post = createPost(renderer, scene, camera);
  } finally { console.warn = originalWarn; }
  try {
    if (failure) await assert.rejects(post.compileAsync(), /compile failed/);
    else await post.compileAsync();
    assert.equal(observed.args[0], scene); assert.equal(observed.args[1], camera);
    assert.deepEqual(observed.args.slice(2), [null, null, 5]);
    assert.ok(observed.target.isRenderTarget); assert.notEqual(observed.target, originalTarget);
    assert.equal(observed.mrt, null);
    assert.equal(target, originalTarget); assert.equal(mrt, originalMrt);
    assert.equal(renderer._callDepth, 3);
  } finally { post.dispose(); }
});

test('speed wave edge blur: post.update drives the speedBlur uniform from fx.speedBlur (#3063 left it at 0)', () => {
  const renderer = { _callDepth: 0, getRenderTarget() { return null; }, setRenderTarget() {}, getMRT() { return null; }, setMRT() {} };
  const originalWarn = console.warn; let post;
  try { console.warn = () => {}; post = createPost(renderer, new Scene(), new PerspectiveCamera()); } finally { console.warn = originalWarn; }
  try {
    assert.equal(post.uniforms.speedBlur.value, 0);
    post.update({ speedBlur: 0.6 }); assert.equal(post.uniforms.speedBlur.value, 0.6);
    post.update({ speedBlur: 1 }); assert.equal(post.uniforms.speedBlur.value, 1);
    post.update({}); assert.equal(post.uniforms.speedBlur.value, 0, 'no speed: sharp');
  } finally { post.dispose(); }
});
