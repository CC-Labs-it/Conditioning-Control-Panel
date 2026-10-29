// A WebGL uniform buffer whose bytes changed re-sends only the changed span (Firefox command-channel budget).
import test from 'node:test';
import assert from 'node:assert/strict';
import { skipUnchangedInstanceUploads } from '../render/uniformUploads.js';

function webgl() {
  const log = [], gpu = new Map();
  let bound = null;
  const gl = {
    UNIFORM_BUFFER: 0x8a11,
    bindBuffer(target, buf) { bound = buf; },
    bufferSubData(target, byteOffset, array, srcOffset = 0, length) {
      assert.equal(target, 0x8a11); assert.ok(bound, 'bound');
      const count = length === undefined ? array.length - srcOffset : length;
      bound.bytes.set(new Uint8Array(array.buffer, array.byteOffset + srcOffset * array.BYTES_PER_ELEMENT, count * array.BYTES_PER_ELEMENT), byteOffset);
      log.push(['span', byteOffset, count * array.BYTES_PER_ELEMENT]);
    },
  };
  const backend = {
    isWebGLBackend: true, gl,
    get(b) { if (!gpu.has(b)) gpu.set(b, { bufferGPU: { bytes: new Uint8Array(b.buffer.byteLength) } }); return gpu.get(b); },
    // the stock path: the whole array with bufferData
    updateBinding(b) { const d = backend.get(b); d.bufferGPU.bytes.set(new Uint8Array(b.buffer.buffer, b.buffer.byteOffset, b.buffer.byteLength)); log.push(['whole', b.buffer.byteLength]); },
  };
  const bindings = { updateForRender(ro) { for (const b of ro.list) backend.updateBinding(b); } };
  return { three: { backend, _bindings: bindings }, backend, bindings, log, gpu };
}
const mirror = (g, b) => assert.deepEqual([...g.gpu.get(b).bufferGPU.bytes], [...new Uint8Array(b.buffer.buffer, b.buffer.byteOffset, b.buffer.byteLength)]);

test('changed instance matrices send one span covering the moved instances, the GPU copy stays exact', () => {
  const g = webgl();
  assert.equal(skipUnchangedInstanceUploads(g.three), true);
  const matrices = { array: new Float32Array(239 * 16), version: 0 };
  const mesh = { isInstancedMesh: true, instanceMatrix: matrices };
  const ubo = { isNodeUniformBuffer: true, buffer: matrices.array, updateRanges: [] };
  const draw = () => g.bindings.updateForRender({ object: mesh, list: [ubo] });
  draw(); assert.deepEqual(g.log, [['whole', 239 * 64]]);
  g.log.length = 0; draw(); assert.deepEqual(g.log, [], 'unchanged: nothing');
  // two halo slots (instances 40 and 41) move
  for (let k = 40 * 16; k < 42 * 16; k++) matrices.array[k] = k * 0.25;
  matrices.version++; g.log.length = 0; draw();
  assert.deepEqual(g.log, [['span', 40 * 64, 2 * 64]]);
  mirror(g, ubo);
  g.log.length = 0; draw(); assert.deepEqual(g.log, [], 'the second pass of the frame sends nothing');
  // far-apart edits become one span, still exact
  matrices.array[0] = 9; matrices.array[239 * 16 - 1] = 7; matrices.version++; g.log.length = 0; draw();
  assert.equal(g.log.length, 1); assert.equal(g.log[0][0], 'span'); mirror(g, ubo);
});

test('bones outside a render object, -0 and NaN take spans too; a new array or GL buffer goes whole', () => {
  const g = webgl();
  skipUnchangedInstanceUploads(g.three);
  const bones = { isNodeUniformBuffer: true, buffer: new Float32Array(16 * 30), updateRanges: [] };
  g.backend.updateBinding(bones); g.log.length = 0;
  bones.buffer[100] = -0; g.backend.updateBinding(bones);
  assert.deepEqual(g.log, [['span', 400, 4]]); mirror(g, bones);
  bones.buffer[100] = NaN; g.log.length = 0; g.backend.updateBinding(bones);
  assert.deepEqual(g.log, [['span', 400, 4]]);
  g.log.length = 0; g.backend.updateBinding(bones); assert.deepEqual(g.log, [], 'the same NaN bytes skip');
  g.gpu.get(bones).bufferGPU = { bytes: new Uint8Array(bones.buffer.byteLength) }; g.log.length = 0; g.backend.updateBinding(bones);
  assert.deepEqual(g.log, [['whole', 1920]], 'a new GL buffer re-sends whole');
  bones.buffer = new Float32Array(16 * 30).fill(1); g.log.length = 0; g.backend.updateBinding(bones);
  assert.deepEqual(g.log, [['whole', 1920]], 'a new array re-sends whole'); mirror(g, bones);
  bones.buffer[7] = 2; g.log.length = 0; g.backend.updateBinding(bones);
  assert.deepEqual(g.log, [['span', 28, 4]]); mirror(g, bones);
});

test('without a GL handle (a backend stand-in) a changed buffer still takes the stock whole upload', () => {
  const g = webgl(); delete g.backend.gl;
  skipUnchangedInstanceUploads(g.three);
  const bones = { isNodeUniformBuffer: true, buffer: new Float32Array(8), updateRanges: [] };
  g.backend.updateBinding(bones); bones.buffer[1] = 1; g.log.length = 0; g.backend.updateBinding(bones);
  assert.deepEqual(g.log, [['whole', 32]]);
});
