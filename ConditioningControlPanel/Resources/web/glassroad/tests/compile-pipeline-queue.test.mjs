import { test } from 'node:test';
import assert from 'node:assert/strict';
import { Renderer, WebGPUBackend, WebGLBackend, Scene, PerspectiveCamera, MeshBasicNodeMaterial } from '../vendor/three/build/three.webgpu.min.js';

globalThis.ProgressEvent ??= class { constructor(type, values) { this.type = type; Object.assign(this, values); } };
globalThis.requestAnimationFrame ??= callback => setImmediate(() => callback(performance.now()));
const deferred = () => { let resolve, reject; const promise = new Promise((a, b) => { resolve = a; reject = b; }); return { promise, resolve, reject }; };
async function until(predicate) {
  for (let tries = 0; tries < 300 && !predicate(); tries++) await new Promise(resolve => setTimeout(resolve, 5));
  assert.ok(predicate(), 'compiler reaches the expected asynchronous boundary');
}
function fixture({ count = 10, webgpu = true, parallel = false, hookAt = -1, failBefore = -1 } = {}) {
  const entries = Array.from({ length: count }, (_, id) => ({ object: { id, geometry: { drawRange: {} } } }));
  const events = [], jobs = new Map(), progress = [], context = { clippingContext: null };
  let active = 0, maximum = 0;
  const list = { begin() {}, finish() {}, opaque: [], transparent: [], transparentDoublePass: [], lightsNode: {} };
  const renderer = {
    backend: { isWebGPUBackend: webgpu, isWebGLBackend: !webgpu, parallel: parallel ? {} : null }, _initialized: true, _callDepth: 0, shadowMap: {}, xr: { isPresenting: false },
    _nodes: {
      nodeFrame: { renderId: 7, update() {} },
      async getForRenderAsync(object) { return { updateBeforeNodes: object.id === hookAt ? [{}] : [], updateAfterNodes: object.id === hookAt ? [{}] : [] }; },
      updateBefore(object) { events.push('before:' + object.id); if (object.id === failBefore) throw Error('before failed'); },
      updateForRender() {}, updateAfter(object) { events.push('after:' + object.id); },
    },
    _mrt: null, _renderTarget: null, _outputRenderTarget: null, _activeMipmapLevel: 0,
    _currentRenderContext: null, _currentRenderObjectFunction: null, _handleObjectFunction: null, _compilationPromises: null,
    _isPreCompiling: false,
    _renderContexts: { get() { return context; } }, _renderLists: { get() { return list; } }, _background: { update() {} },
    _projectObject() { this._compilationPromises.push(...entries); }, _updateCamera(camera) { return camera; },
    _objects: { get(object) { return object; } }, _geometries: { updateForRender() {} }, _bindings: { updateForRender() {} },
    _pipelines: { getForRender(object, promises) {
      const job = deferred(); jobs.set(object.id, job); events.push('start:' + object.id); maximum = Math.max(maximum, ++active);
      promises.push(job.promise.then(value => { active--; events.push('ready:' + object.id); return value; }, cause => { active--; throw cause; }));
    } },
  };
  const compile = () => Renderer.prototype.compileAsync.call(renderer, new Scene(), new PerspectiveCamera(), null, e => progress.push(e.loaded));
  return { renderer, events, jobs, progress, compile, get maximum() { return maximum; } };
}

for (const webgpu of [true, false]) test((webgpu ? 'WebGPU' : 'parallel WebGL') + ' overlaps at most eight pure pipelines and reports completion in source order', async () => {
  const f = fixture({ webgpu, parallel: true }), task = f.compile();
  await until(() => f.jobs.size === 8);
  assert.equal(f.maximum, 8); assert.deepEqual(f.progress, []);
  for (let id = 7; id >= 0; id--) f.jobs.get(id).resolve();
  await until(() => f.jobs.size === 10);
  f.jobs.get(9).resolve(); f.jobs.get(8).resolve(); await task;
  assert.deepEqual(f.progress, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
  assert.equal(f.renderer._isPreCompiling, false);
});

for (const webgpu of [true, false]) test((webgpu ? 'WebGPU' : 'parallel WebGL') + ' drains pending jobs before hooks and keeps after-hooks serial', async () => {
  const f = fixture({ count: 4, hookAt: 2, webgpu, parallel: true }), task = f.compile();
  await until(() => f.jobs.size === 2);
  assert.ok(!f.events.includes('before:2'));
  f.jobs.get(1).resolve(); f.jobs.get(0).resolve();
  await until(() => f.jobs.has(2));
  assert.ok(f.events.indexOf('after:1') < f.events.indexOf('before:2'));
  assert.ok(!f.events.includes('before:3'));
  f.jobs.get(2).resolve(); await until(() => f.jobs.has(3));
  assert.ok(f.events.indexOf('after:2') < f.events.indexOf('before:3'));
  f.jobs.get(3).resolve(); await task;
});

test('WebGL2 without KHR_parallel_shader_compile remains strictly serial', async () => {
  const f = fixture({ count: 3, webgpu: false }), task = f.compile();
  for (let id = 0; id < 3; id++) { await until(() => f.jobs.has(id)); assert.equal(f.jobs.size, id + 1); f.jobs.get(id).resolve(); }
  await task; assert.equal(f.maximum, 1);
});

test('real parallel WebGL completion binds each program correctly when links finish in reverse order', async () => {
  const backend = new WebGLBackend(), programs = [], ready = new Set(), bindings = [];
  let current = null;
  backend.parallel = { COMPLETION_STATUS_KHR: 'complete' };
  backend.gl = {
    LINK_STATUS: 'linked',
    createProgram() { const program = { id: programs.length }; programs.push(program); return program; },
    attachShader() {}, linkProgram() {},
    getProgramParameter(program, query) { return query === 'complete' ? ready.has(program) : true; },
    getUniformBlockIndex(program, name) { return { program, name }; },
    uniformBlockBinding(program, block, unit) { assert.equal(current, program); assert.equal(block.program, program); bindings.push(['block', program.id, unit]); },
    getUniformLocation(program, name) { return { program, name }; },
    uniform1i(location, unit) { assert.equal(current, location.program); bindings.push(['sampler', location.program.id, unit]); },
  };
  backend.state = { useProgram(program) { current = program; } };
  function object() {
    const pipeline = { vertexProgram: {}, fragmentProgram: {} };
    for (const stage of [pipeline.vertexProgram, pipeline.fragmentProgram]) backend.get(stage).shaderGPU = {};
    return { pipeline, getBindings: () => [{ bindings: [{ isUniformBuffer: true, name: 'block' }, { isSampledTexture: true, name: 'sampler' }] }] };
  }
  const a = object(), b = object(), promises = [];
  backend.createRenderPipeline(a, promises); backend.createRenderPipeline(b, promises);
  assert.equal(promises.length, 2); assert.equal(current, null);
  ready.add(programs[1]); await until(() => backend.get(b.pipeline).pipeline !== undefined);
  assert.equal(backend.get(a.pipeline).pipeline, undefined);
  assert.equal(current, programs[1]);
  ready.add(programs[0]); await Promise.all(promises);
  assert.equal(current, programs[0]);
  assert.deepEqual(bindings, [['block', 1, 0], ['sampler', 1, 0], ['block', 0, 0], ['sampler', 0, 0]]);
});

test('early pipeline rejection is handled while the queue drains and flags restore', async () => {
  const f = fixture({ count: 8 }), task = f.compile(), rejected = assert.rejects(task, /pipeline failed/);
  await until(() => f.jobs.size >= 1); f.jobs.get(0).reject(Error('pipeline failed'));
  await until(() => f.jobs.size === 8);
  for (let id = 1; id < 8; id++) f.jobs.get(id).resolve();
  await rejected; assert.deepEqual(f.progress, []); assert.equal(f.renderer._isPreCompiling, false);
  assert.equal(f.renderer._currentRenderContext, null); assert.equal(f.renderer._nodes.nodeFrame.renderId, 7);
});

test('preparation failure still waits for started GPU work and restores precompile state', async () => {
  const f = fixture({ count: 4, failBefore: 2 }), task = f.compile();
  let done = false; const rejected = assert.rejects(task, /before failed/).then(() => { done = true; });
  await until(() => f.events.includes('before:2')); assert.equal(done, false);
  for (const job of f.jobs.values()) job.resolve(); await rejected;
  assert.equal(f.renderer._isPreCompiling, false);
});

for (const failure of ['validation', 'pipeline']) test('real WebGPU backend pairs error scopes under overlapping ' + failure + ' failure', async () => {
  const backend = new WebGPUBackend(), calls = [], pipelineJobs = [], scopes = [], stack = [], errors = [], diagnostics = [];
  backend.device = {
    createPipelineLayout() { return {}; },
    pushErrorScope() { const id = scopes.length; scopes.push(deferred()); stack.push(id); calls.push('push:' + id); },
    createRenderPipelineAsync(descriptor) { const job = deferred(); pipelineJobs.push(job); calls.push('create:' + descriptor.label); return job.promise; },
    popErrorScope() { const id = stack.pop(); calls.push('pop:' + id); return scopes[id].promise; },
  };
  backend.utils = { getCurrentColorFormat: () => 'rgba8unorm', getCurrentDepthStencilFormat: () => 'depth24plus', getSampleCountRenderContext: () => 1, getPrimitiveTopology: () => 'triangle-list' };
  backend.attributeUtils = { createShaderVertexBuffers: () => [] };
  backend.pipelineUtils._reportShaderDiagnostics = async (stages, label) => { diagnostics.push(label); };
  function object(name) {
    const material = new MeshBasicNodeMaterial({ name }), pipeline = { vertexProgram: {}, fragmentProgram: {} };
    for (const stage of [pipeline.vertexProgram, pipeline.fragmentProgram]) backend.get(stage).module = { module: {}, entryPoint: 'main' };
    return { object: {}, material, geometry: { index: null }, pipeline, context: { textures: null, depth: false, stencil: false }, getBindings: () => [] };
  }
  const a = object('A'), b = object('B'), promises = [], originalError = console.error;
  console.error = (...args) => errors.push(args.join(' '));
  try {
    backend.createRenderPipeline(a, promises); backend.createRenderPipeline(b, promises);
    assert.deepEqual(calls.filter(value => /^(push|pop):/.test(value)), ['push:0', 'pop:0', 'push:1', 'pop:1']);
    assert.equal(stack.length, 0);
    if (failure === 'pipeline') pipelineJobs[1].reject(Error('B-only pipeline rejection'));
    else pipelineJobs[1].resolve({ name: 'pipeline-B' });
    scopes[0].resolve(null);
    pipelineJobs[0].resolve({ name: 'pipeline-A' }); scopes[1].resolve(failure === 'validation' ? { message: 'B-only validation error' } : null);
    await Promise.all(promises);
    assert.equal(backend.get(a.pipeline).pipeline.name, 'pipeline-A'); assert.equal(backend.get(a.pipeline).error, undefined);
    assert.equal(backend.get(b.pipeline).error, true); assert.equal(diagnostics.length, 1);
    assert.ok(diagnostics[0].includes('_B_')); assert.ok(errors[0].includes('B-only'));
  } finally { console.error = originalError; a.material.dispose(); b.material.dispose(); }
});
