// Attribute uploads send only the changed bytes (Firefox command-channel budget, 2026-10-27): the simulated GPU
// buffer must equal the source array after every step, with the fewest bytes crossing.
import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
registerHooks({ resolve(s, c, next) {
  const files = { 'three': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js', 'three/webgpu': 'three.webgpu.min.js' };
  return files[s] ? { url: new URL('../vendor/three/build/' + files[s], import.meta.url).href, shortCircuit: true } : next(s, c);
} });
const { BufferAttribute, InstancedBufferAttribute, DynamicDrawUsage } = await import('three/webgpu');
const { uploadChangedAttributeBytes } = await import('../render/attributeUploads.js');

const ARRAY_BUFFER = 34962;

/** A WebGL backend stand-in: one GL buffer per attribute, a byte mirror of what the GPU holds, upload accounting. */
function fakeBackend() {
  const data = new Map(), log = [];
  let bound = null;
  const gl = {
    bindBuffer(target, buf) { bound = buf; log.push(['bind', target, buf ? buf.id : null]); },
    bufferSubData(target, byteOffset, array, srcOffset = 0, length) {
      const count = length === undefined ? array.length - srcOffset : length;
      const bytes = new Uint8Array(array.buffer, array.byteOffset + srcOffset * array.BYTES_PER_ELEMENT, count * array.BYTES_PER_ELEMENT);
      assert.ok(bound, 'a buffer is bound');
      bound.bytes.set(bytes, byteOffset);
      log.push(['sub', byteOffset, bytes.length]);
    },
  };
  const backend = {
    isWebGLBackend: true, gl,
    get(o) { let d = data.get(o); if (!d) data.set(o, d = {}); return d; },
    attributeUtils: {
      // the vendored stock path: create on first sight, then full or ranged uploads
      updateAttribute(attribute) {
        const a = attribute.isInterleavedBufferAttribute ? attribute.data : attribute, d = backend.get(a);
        if (!d.bufferGPU) d.bufferGPU = { id: data.size, bytes: new Uint8Array(a.array.byteLength) }, d.bufferType = ARRAY_BUFFER;
        gl.bindBuffer(d.bufferType, d.bufferGPU);
        if (a.updateRanges.length === 0) gl.bufferSubData(d.bufferType, 0, a.array);
        else { for (const r of a.updateRanges) gl.bufferSubData(d.bufferType, r.start * a.array.BYTES_PER_ELEMENT, a.array, r.start, r.count); a.clearUpdateRanges(); }
        gl.bindBuffer(d.bufferType, null);
        d.version = a.version;
        log.push(['stock']);
      },
    },
  };
  return { backend, log, gpuOf: (a) => backend.get(a).bufferGPU.bytes };
}

const bytesSent = (log) => log.filter(e => e[0] === 'sub').reduce((s, e) => s + e[2], 0);
const same = (gpu, array) => assert.deepEqual([...gpu], [...new Uint8Array(array.buffer, array.byteOffset, array.byteLength)]);

test('first sight takes the stock upload; identical bytes then send nothing on every later draw', () => {
  const { backend, log, gpuOf } = fakeBackend(), three = { backend };
  assert.equal(uploadChangedAttributeBytes(three), true);
  assert.equal(uploadChangedAttributeBytes(three), false, 'installed once');
  const a = new BufferAttribute(new Float32Array(3000), 3).setUsage(DynamicDrawUsage);
  for (let i = 0; i < a.array.length; i++) a.array[i] = i * 0.5;
  backend.attributeUtils.updateAttribute(a);
  assert.deepEqual(log.filter(e => e[0] === 'stock').length, 1);
  same(gpuOf(a), a.array);
  log.length = 0;
  for (let pass = 0; pass < 3; pass++) backend.attributeUtils.updateAttribute(a);   // main, shadow, inset
  assert.equal(bytesSent(log), 0, 'DynamicDrawUsage re-draws send nothing when nothing moved');
});

test('a change in the middle sends exactly that span, once, and the GPU mirror matches', () => {
  const { backend, log, gpuOf } = fakeBackend();
  uploadChangedAttributeBytes({ backend });
  const a = new BufferAttribute(new Float32Array(14400 * 3), 3).setUsage(DynamicDrawUsage);
  backend.attributeUtils.updateAttribute(a);
  log.length = 0;
  for (let k = 900; k < 960; k++) a.array[k] = 7 + k;                 // 60 floats = 240 bytes
  a.needsUpdate = true;
  backend.attributeUtils.updateAttribute(a);
  assert.equal(bytesSent(log), 240);
  assert.deepEqual(log.filter(e => e[0] === 'sub')[0].slice(1), [3600, 240]);
  same(gpuOf(a), a.array);
  assert.equal(backend.get(a).version, a.version, 'three bookkeeping kept');
  log.length = 0;
  backend.attributeUtils.updateAttribute(a);
  assert.equal(bytesSent(log), 0, 'the second pass of the frame sends nothing');
  // two far-apart edits become one span (a single call) and still land exactly
  a.array[0] = -1; a.array[a.array.length - 1] = -2; a.needsUpdate = true;
  backend.attributeUtils.updateAttribute(a);
  same(gpuOf(a), a.array);
  assert.equal(log.filter(e => e[0] === 'sub').length, 1);
});

test("a writer's update ranges bound the compare: unchanged words inside them are skipped, changed ones land", () => {
  const { backend, log, gpuOf } = fakeBackend();
  uploadChangedAttributeBytes({ backend });
  const a = new InstancedBufferAttribute(new Float32Array(4000 * 4), 4).setUsage(DynamicDrawUsage);
  backend.attributeUtils.updateAttribute(a);
  log.length = 0;
  // the particle ring: only slots 0..300 are live; the writer marks them all, only 20 of them moved
  for (let s = 100; s < 120; s++) for (let k = 0; k < 4; k++) a.array[s * 4 + k] = s + k;
  a.clearUpdateRanges(); a.addUpdateRange(0, 300 * 4); a.needsUpdate = true;
  backend.attributeUtils.updateAttribute(a);
  assert.equal(bytesSent(log), 20 * 16);
  assert.equal(a.updateRanges.length, 0, 'ranges consumed like stock');
  same(gpuOf(a), a.array);
  // a change OUTSIDE the writer's range is not sent on this upload (same contract as three), lands on the next full one
  log.length = 0;
  a.array[3990 * 4] = 42; a.clearUpdateRanges(); a.addUpdateRange(0, 4); a.needsUpdate = true;
  backend.attributeUtils.updateAttribute(a);
  assert.equal(bytesSent(log), 0);
  a.needsUpdate = true;
  backend.attributeUtils.updateAttribute(a);
  assert.equal(bytesSent(log), 4);
  same(gpuOf(a), a.array);
});

test('Uint16 and Uint8 arrays (indices, packed attributes) upload whole words that cover whole elements', () => {
  const { backend, log, gpuOf } = fakeBackend();
  uploadChangedAttributeBytes({ backend });
  const idx = new BufferAttribute(new Uint16Array(6000), 1), bytes = new BufferAttribute(new Uint8Array(4096), 4);
  backend.attributeUtils.updateAttribute(idx); backend.attributeUtils.updateAttribute(bytes);
  log.length = 0;
  idx.array[1001] = 9; idx.needsUpdate = true; backend.attributeUtils.updateAttribute(idx);
  bytes.array[10] = 200; bytes.array[13] = 201; bytes.needsUpdate = true; backend.attributeUtils.updateAttribute(bytes);
  same(gpuOf(idx), idx.array); same(gpuOf(bytes), bytes.array);
  const subs = log.filter(e => e[0] === 'sub');
  assert.deepEqual(subs.map(e => e.slice(1)), [[2000, 4], [8, 8]]);
});

test('a replaced array or a new GL buffer takes the stock upload once and is then compared again', () => {
  const { backend, log, gpuOf } = fakeBackend();
  uploadChangedAttributeBytes({ backend });
  const a = new BufferAttribute(new Float32Array(300), 3).setUsage(DynamicDrawUsage);
  backend.attributeUtils.updateAttribute(a);
  a.array = new Float32Array(300).fill(3); a.needsUpdate = true;
  backend.get(a).bufferGPU = { id: 99, bytes: new Uint8Array(1200) };     // three rebuilt the GL buffer too
  log.length = 0;
  backend.attributeUtils.updateAttribute(a);
  assert.equal(log.filter(e => e[0] === 'stock').length, 1);
  same(gpuOf(a), a.array);
  log.length = 0;
  backend.attributeUtils.updateAttribute(a);
  assert.equal(bytesSent(log), 0);
});

test('WebGPU and a second install are refused', () => {
  assert.equal(uploadChangedAttributeBytes({ backend: { isWebGPUBackend: true } }), false);
  assert.equal(uploadChangedAttributeBytes(null), false);
});
