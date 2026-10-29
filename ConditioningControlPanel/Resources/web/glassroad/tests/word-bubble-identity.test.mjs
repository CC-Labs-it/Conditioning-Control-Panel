import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';

const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { Scene } = await import('three/webgpu');
const { createWordBubbles } = await import('../render/wordBubbles.js');
hooks.deregister();

function fixture(options = {}) {
  const canvases = [], previous = globalThis.document;
  globalThis.document = { createElement(tag) {
    assert.equal(tag, 'canvas');
    const context = { measureText: text => ({ width: text.length * 55 }), createLinearGradient: () => ({ addColorStop() {} }) };
    for (const name of ['beginPath', 'moveTo', 'lineTo', 'quadraticCurveTo', 'fill', 'stroke', 'save', 'restore',
      'clearRect', 'strokeText', 'fillText', 'fillRect', 'arc', 'drawImage']) context[name] = () => {};
    const canvas = { width: 0, height: 0, getContext: () => context }; canvases.push(canvas); return canvas;
  } };
  try { const scene = new Scene(); return { scene, canvases, view: createWordBubbles(scene, { track: 'mainframe', ...options }) }; }
  finally { if (previous === undefined) delete globalThis.document; else globalThis.document = previous; }
}
const bubble = (word, overrides = {}) => ({ id: 1, w: word, row: 5, n: 5, p: Array.from({ length: 5 }, (_, i) => [i * 2, 1.5, 30]),
  state: 'live', inT: 2, outT: -1, remainS: -.25, lenS: .5, hit: 2, ...overrides });
const near = (actual, expected) => assert.ok(Math.abs(actual - expected) < .0001, `${actual} != ${expected}`);

test('RESET and EMPTY keep every trigger bubble and carry their exact shared material identity', () => {
  const { view } = fixture();
  for (const [word, kind] of [['RESET', 7], ['EMPTY', 8], ['"pink,"', 3], ['reset now', 0], ['unlocked', 0]]) {
    const v = bubble(word); view.update([v]);
    assert.equal(view.object.count, 5, 'the existing trigger row remains five bubbles');
    const ids = view._dev.attributes.identity.array;
    for (let i = 0; i < 5; i++) {
      assert.equal(ids[i * 4], kind); near(ids[i * 4 + 1], .25);
      assert.equal(ids[i * 4 + 2], -1); assert.equal(ids[i * 4 + 3], word.length);
    }
    assert.equal(v.w, word); assert.equal(v.state, 'live'); assert.equal(v.n, 5);
  }
  view.dispose();
});

test('semantic source text remains visible through its real pop and rings never inherit its deformation', () => {
  const { view } = fixture({ photosafe: { request: () => false } });
  view.update([bubble('RESET', { state: 'popped', outT: .16 })]);
  const { identity, values } = view._dev.attributes;
  for (let i = 0; i < view.object.count; i++) {
    const at = i * 4, mode = values.array[at + 2];
    if (mode < .5) {
      assert.equal(identity.array[at], 7); near(identity.array[at + 2], .5);
      near((values.array[at] % 1) / .45, .5);
    } else { assert.equal(identity.array[at], 0); assert.equal(identity.array[at + 2], -1); }
  }
  view.update([bubble('RESET', { state: 'popped', outT: .33 })]);
  for (let i = 0; i < view.object.count; i++) assert.equal(identity.array[i * 4], 0, 'the collected word has disappeared');
  view.dispose();
});

test('Gentle and motion off keep the semantic source still with a longer real collection fade', () => {
  const { view } = fixture();
  for (const options of [{ gentle: true }, { motion: 'off' }]) {
    view.update([bubble('EMPTY', { state: 'popped', outT: .3 })], null, .1, options);
    assert.equal(view._dev.uniforms.identityMotion.value, 0);
    near(view._dev.attributes.identity.array[2], .5);
    near((view._dev.attributes.values.array[0] % 1) / .45, .5);
    assert.equal(view._dev.uniforms.twinkle.value, 0);
  }
  view.update([bubble('EMPTY')], null, .1, { motion: 'less' });
  assert.equal(view._dev.uniforms.identityMotion.value, .5);
  view.update([bubble('EMPTY')], null, .1, { motion: 'full' });
  assert.equal(view._dev.uniforms.identityMotion.value, 1);
  view.dispose();
});

test('pause freezes source attributes, glass, text clock and atlas without new materials or canvases', () => {
  const { view, canvases, scene } = fixture(), material = view.object.material;
  view.update([bubble('RESET')]);
  const attrs = view._dev.attributes, buffers = Object.fromEntries(Object.entries(attrs).map(([key, attr]) => [key, attr.array]));
  const snapshots = Object.fromEntries(Object.entries(attrs).map(([key, attr]) => [key, attr.array.slice()]));
  const time = view._dev.uniforms.time.value, count = view.object.count;
  view.update([bubble('EMPTY', { state: 'popped', outT: .2 })], null, 3, { paused: true });
  for (const [key, attr] of Object.entries(attrs)) assert.deepEqual(attr.array, snapshots[key]);
  assert.equal(view._dev.uniforms.time.value, time); assert.equal(view.object.count, count);
  for (let i = 0; i < 20; i++) view.update([bubble(i % 2 ? 'EMPTY' : 'RESET')]);
  for (const [key, attr] of Object.entries(attrs)) assert.equal(attr.array, buffers[key]);
  assert.equal(view.object.material, material); assert.equal(canvases.length, 2);
  view.clear(); assert.equal(view.object.count, 0);
  view.dispose(); assert.equal(scene.children.length, 0);
});
