// Per-frame upload savings (Firefox frame cost, 2026-10-26): the same values reach the GPU, with less work.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
registerHooks({ resolve(s, c, next) {
  const files = { 'three': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js', 'three/webgpu': 'three.webgpu.min.js' };
  if (s.startsWith('three/addons/')) return { url: new URL('../vendor/' + s, import.meta.url).href, shortCircuit: true };
  return files[s] ? { url: new URL('../vendor/three/build/' + files[s], import.meta.url).href, shortCircuit: true } : next(s, c);
} });
const { Matrix4, Vector3, Quaternion, Euler } = await import('three/webgpu');
const { composeInto, matrixInto } = await import('../render/instanceWrite.js');
const { skipUnchangedInstanceUploads } = await import('../render/uniformUploads.js');
const { trackSceneResources } = await import('../render/sceneResources.js');

const here = (p) => new URL(p, import.meta.url);

test('composeInto writes exactly what Matrix4.compose + setMatrixAt wrote', () => {
  const m = new Matrix4(), a = new Float32Array(32), b = new Float32Array(32);
  const p = new Vector3(), q = new Quaternion(), s = new Vector3();
  for (let i = 0; i < 400; i++) {
    p.set(Math.sin(i) * 900, Math.cos(i * 1.3) * 40, i * 7.1 - 1300);
    q.setFromEuler(new Euler(i * .37, i * .11 - 2, Math.cos(i) * 3)); s.set(.2 + i % 5, i % 3 === 0 ? 0 : 1.7, .05 * i);
    m.compose(p, q, s).toArray(a, 16);
    composeInto(b, 1, p, q, s);
    assert.deepEqual(b, a);
    m.makeRotationY(i).setPosition(i, -i, 2 * i); m.toArray(a, 0); matrixInto(b, 0, m);
    assert.deepEqual(b, a);
  }
});

test('cue particle pools idle without matrix uploads, and still show every live burst', async () => {
  const { Scene } = await import('three/webgpu'), { createCueParticles } = await import('../render/cueParticles.js');
  const fx = createCueParticles(new Scene()), meshes = fx.object.children;
  fx.update(1 / 60); fx.update(1 / 60);                 // the first pass hides every pooled slot once
  const versions = meshes.map(m => m.instanceMatrix.version);
  for (let i = 0; i < 30; i++) fx.update(1 / 60);
  assert.deepEqual(meshes.map(m => m.instanceMatrix.version), versions, 'nothing alive: no upload');
  fx.burst('hearts', [0, 0, 0], { count: 12 });
  fx.update(1 / 60);
  const hearts = meshes[0];
  assert.ok(hearts.count > 0 && hearts.instanceMatrix.version > versions[0], 'a live burst uploads');
  assert.deepEqual(meshes.slice(1).map(m => m.instanceMatrix.version), versions.slice(1), 'idle pools stay quiet');
  for (let i = 0; i < 400; i++) fx.update(1 / 30);
  const done = hearts.instanceMatrix.version;
  assert.equal(hearts.count, 0);
  for (let k = 0; k < 12; k++) assert.ok(hearts.instanceMatrix.array[k * 16] === 0, 'a finished heart is zero scale');
  fx.update(1 / 60); assert.equal(hearts.instanceMatrix.version, done, 'and stops uploading');
  fx.dispose();
});

test('moving machinery instances equal their source world matrices while unmoved parts skip work', async () => {
  const { Scene } = await import('three/webgpu'), { createMovingHazardView } = await import('../render/movingHazards.js');
  const { loadTrack } = await import('../track/load.js');
  const json = JSON.parse(readFileSync(here('../tracks/goonworks.json'), 'utf8'));
  const track = loadTrack(json, { moving: true }), scene = new Scene();
  const view = createMovingHazardView(scene, track, { primaryHex: () => '#8f969e', accentHex: () => '#e6b464' });
  const batches = view.group.children.filter(o => o.isInstancedMesh);
  assert.ok(batches.length > 0);
  const check = () => {
    scene.updateMatrixWorld(true);
    for (const batch of batches) {
      const sources = []; view.group.traverse(o => { if (o.isMesh && !o.isInstancedMesh && o.material === batch.material) sources.push(o); });
      sources.forEach((src, i) => assert.deepEqual([...batch.instanceMatrix.array.subarray(i * 16, i * 16 + 16)], [...new Float32Array(src.matrixWorld.elements)]));
    }
  };
  for (const t of [0, .5, 3, 3, 9, 13.8, 13.8, 40]) { view.update(t); check(); }
  const versions = batches.map(b => b.instanceMatrix.version);
  view.update(40); view.update(40);
  assert.deepEqual(batches.map(b => b.instanceMatrix.version), versions, 'a held pose uploads nothing');
});

test('WebGL instance matrices in a uniform buffer are re-sent only when they change', () => {
  const sent = [], gpu = new Map();
  const backend = { isWebGLBackend: true, get(b) { if (!gpu.has(b)) gpu.set(b, { bufferGPU: {} }); return gpu.get(b); }, updateBinding(b) { sent.push(b); } };
  const bindings = { updateForRender(ro) { for (const b of ro.list) backend.updateBinding(b); } };
  const three = { backend, _bindings: bindings };
  assert.equal(skipUnchangedInstanceUploads(three), true);
  assert.equal(skipUnchangedInstanceUploads(three), false, 'patched once');
  const matrices = { array: new Float32Array(32), version: 0 };
  const mesh = { isInstancedMesh: true, instanceMatrix: matrices };
  const ubo = { isNodeUniformBuffer: true, buffer: matrices.array, updateRanges: [] };
  const bones = { isNodeUniformBuffer: true, buffer: new Float32Array(16), updateRanges: [] };
  const group = { isUniformsGroup: true, buffer: new Float32Array(4), updateRanges: [] };
  const draw = () => bindings.updateForRender({ object: mesh, list: [ubo, bones, group] });
  draw(); assert.deepEqual(sent, [ubo, bones, group], 'first draw sends all');
  sent.length = 0; draw(); assert.deepEqual(sent, [group], 'unchanged instance matrices and bones skip; groups stay with three');
  matrices.version++; sent.length = 0; draw(); assert.deepEqual(sent, [group], 'needsUpdate with the same bytes skips (bones compare bitwise too)');
  matrices.array[5] = 2; matrices.version++; sent.length = 0; draw(); assert.deepEqual(sent, [ubo, group], 'changed matrices re-send');
  gpu.get(ubo).bufferGPU = {}; sent.length = 0; draw(); assert.deepEqual(sent, [ubo, group], 'a new GL buffer re-sends');
  sent.length = 0; backend.updateBinding(ubo); assert.deepEqual(sent, [], 'outside a render object the bytes decide too');
  assert.equal(skipUnchangedInstanceUploads({ backend: { isWebGLBackend: false }, _bindings: bindings }), false, 'WebGPU untouched');
});

test('scene resource tracking folds each render variant once until its attribute list changes', () => {
  const geometries = { calls: 0, updateAttributes() { this.calls++; } };
  const three = { _geometries: geometries };
  trackSceneResources(three);
  const own = {}, nodeOnly = {}, scene = {}, object = {};
  let reads = 0;
  const geometry = { get attributes() { reads++; return { own }; } };
  const row = { object, scene, geometry, attributes: [own, nodeOnly], getAttributes() { return this.attributes; } };
  for (let i = 0; i < 5; i++) geometries.updateAttributes(row);
  assert.equal(geometries.calls, 5, 'three still updates every draw');
  assert.equal(reads, 1, 'the ownership Set is built once');
  row.attributes = [own, nodeOnly, {}];
  geometries.updateAttributes(row);
  assert.equal(reads, 2, 'a rebuilt list is folded again');
});

test('the boot debug record never serialises the garage scene', () => {
  const src = readFileSync(here('../boot.js'), 'utf8');
  assert.match(src, /Object\.defineProperty\(debug, 'garage', \{ value: garage, enumerable: false/);
  assert.doesNotMatch(src, /debug\.garage = garage/);
});
