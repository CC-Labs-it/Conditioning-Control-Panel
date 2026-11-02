import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createFieldRenderer, normalizeParams2 } from '../../arcademy/engine/loom/loomField.js';

function fixture(failure = '') {
  const live = { shader: new Set(), program: new Set(), buffer: new Set() }, deleted = [], calls = [];
  let shaderCount = 0, currentProgram = null, boundBuffer = null;
  const make = kind => { const item = { kind, index: kind === 'shader' ? ++shaderCount : 0 }; live[kind].add(item); return item; };
  const remove = item => { assert.ok(live[item.kind].delete(item), 'each owned resource is deleted once'); deleted.push(item); };
  const gl = {
    VERTEX_SHADER: 1, FRAGMENT_SHADER: 2, COMPILE_STATUS: 3, LINK_STATUS: 4, ARRAY_BUFFER: 5,
    STATIC_DRAW: 6, FLOAT: 7, TRIANGLES: 8,
    createShader: () => make('shader'), shaderSource() {}, compileShader() {},
    getShaderParameter: shader => failure !== 'shader' || shader.index !== 2,
    getShaderInfoLog: () => 'compile failed', deleteShader: remove,
    createProgram: () => make('program'), attachShader() {}, linkProgram() {},
    getProgramParameter: () => failure !== 'link', getProgramInfoLog: () => 'link failed', deleteProgram: remove,
    useProgram(program) { currentProgram = program; calls.push('program'); },
    createBuffer: () => failure === 'buffer' ? null : make('buffer'), deleteBuffer: remove,
    bindBuffer(target, buffer) { boundBuffer = buffer; calls.push('buffer'); }, bufferData() {},
    getAttribLocation: () => 0, enableVertexAttribArray() {}, disableVertexAttribArray() { calls.push('disable'); }, vertexAttribPointer() {},
    getUniformLocation: () => 0, uniform1f() {}, uniform2f() {}, uniform3f() {}, uniform3fv() {}, viewport() {},
    drawArrays() { assert.ok(live.program.has(currentProgram)); assert.ok(live.buffer.has(boundBuffer)); calls.push('draw'); },
    getExtension() { assert.fail('resource disposal must never request forced context loss'); },
  };
  const canvas = { width: 512, height: 256, getContext: () => gl };
  return { gl, canvas, live, deleted, calls, get bound() { return [currentProgram, boundBuffer]; } };
}

test('Loom disposal releases its GPU objects once and leaves another live renderer usable', () => {
  const first = fixture(), sibling = fixture(), a = createFieldRenderer(first.canvas), b = createFieldRenderer(sibling.canvas);
  const params = normalizeParams2({});
  a.render(params, 0); b.render(params, 0);
  a.dispose();
  assert.deepEqual(first.bound, [null, null]);
  assert.deepEqual([first.canvas.width, first.canvas.height], [1, 1]);
  assert.deepEqual(Object.values(first.live).map(set => set.size), [0, 0, 0]);
  assert.equal(first.deleted.length, 4);
  const callCount = first.calls.length;
  a.dispose(); a.render(params, .5);
  assert.equal(first.calls.length, callCount, 'disposal and stale rendering are harmless');
  b.render(params, .5);
  assert.equal(sibling.calls.filter(call => call === 'draw').length, 2);
  assert.deepEqual([sibling.canvas.width, sibling.canvas.height], [512, 256]);
  b.dispose();
});

for (const failure of ['shader', 'link', 'buffer']) test('Loom cleans partial initialization after ' + failure + ' failure', () => {
  const f = fixture(failure);
  assert.throws(() => createFieldRenderer(f.canvas), /loom/);
  assert.deepEqual(Object.values(f.live).map(set => set.size), [0, 0, 0]);
  assert.deepEqual(f.bound, [null, null]);
  assert.deepEqual([f.canvas.width, f.canvas.height], [1, 1]);
  assert.equal(f.deleted.filter(item => item.kind === 'shader').length, 2);
  assert.equal(f.deleted.filter(item => item.kind === 'program').length, 1);
});

test('Loom preserves the unavailable-context fallback', () => {
  assert.equal(createFieldRenderer({ getContext: () => null }), null);
});
