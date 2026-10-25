import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';

const hooks = registerHooks({ resolve(s, c, next) {
  const files = { 'three/tsl': 'three.tsl.min.js', 'three/webgpu': 'three.webgpu.min.js' };
  return files[s] ? { url: new URL('../vendor/three/build/' + files[s], import.meta.url).href, shortCircuit: true } : next(s, c);
} });
const { createAirWords, AIR_WORD_CAPACITY, airWordBit } = await import('../render/airWords.js');
const { Scene, Box3 } = await import('three/webgpu');
hooks.deregister();

function documentFixture() {
  const canvases = [];
  return { canvases, createElement(tag) {
    assert.equal(tag, 'canvas');
    const calls = [], ctx = { measureText: t => ({ width: t.length * 90 }),
      createLinearGradient: () => ({ addColorStop() {} }) };
    for (const name of ['clearRect', 'save', 'restore', 'translate', 'scale', 'strokeText', 'fillText', 'fillRect', 'beginPath', 'arc', 'fill']) {
      ctx[name] = (...args) => calls.push([name, ...args]);
    }
    const canvas = { calls, context: ctx, getContext: () => ctx }; canvases.push(canvas); return canvas;
  } };
}
function platform(overrides = {}) {
  return { id: 'air-word-0', text: 'Breathe', s: 50, p: [4, 12, 30], t: [0, 0, 1], n: [0, 1, 0], b: [1, 0, 0],
    widthM: 12, lengthM: 18, alpha: 1, solid: true, ...overrides };
}
function fixture(track = 'threshold', options = {}) {
  const scene = new Scene(), doc = documentFixture();
  return { scene, doc, view: createAirWords(scene, { id: track }, { doc, ...options }) };
}

test('pooled thick words sit on the exact rectangular physical footprint and keep each course lettering', () => {
  for (const course of ['threshold', 'mainframe', 'keep', 'boudoir', 'goonworks']) {
    const { scene, doc, view } = fixture(course), p = platform(), run = { timeS: 3, platforms: [p] };
    assert.equal(doc.canvases.length, AIR_WORD_CAPACITY);
    view.update(run, 1 / 60);
    const root = view.object.children[0], deck = root.children[0], type = root.children[1];
    assert.equal(root.visible, true); assert.deepEqual(root.position.toArray(), p.p);
    root.updateMatrixWorld(true);
    const bounds = new Box3().setFromObject(deck);
    assert.equal(bounds.max.x - bounds.min.x, p.widthM);
    assert.equal(bounds.max.z - bounds.min.z, p.lengthM);
    assert.ok(Math.abs(bounds.max.y - p.p[1]) < 1e-6);
    assert.equal(type.children.length, 8, 'six glyph depth slices and two readable approach faces');
    assert.ok(type.children[5].position.y > type.children[0].position.y);
    assert.ok(doc.canvases[0].calls.some(c => c[0] === 'fillText' && c[1] === 'Breathe'));
    const family = { threshold: 'editorial', mainframe: 'mono', keep: 'silk', boudoir: 'silk', goonworks: 'rounded' }[course];
    assert.ok(doc.canvases[0].context.font.includes('gr-' + family));
    assert.equal(view.stats.solid, 1); assert.equal(view.stats.particles, 40);
    view.dispose(); assert.equal(scene.children.length, 0);
  }
});

test('materialization and dissolve change visible solidity without growing or moving support', () => {
  const { view } = fixture(), p = platform(), root = view.object.children[0];
  const run = { timeS: 1, platforms: [p] };
  view.update(run, .1); const scale = root.children[0].scale.toArray(), solidOpacity = root.children[0].children[0].material.opacity;
  p.alpha = .45; p.solid = false; view.update(run, .1);
  assert.equal(view.stats.solid, 0); assert.deepEqual(root.children[0].scale.toArray(), scale);
  assert.ok(root.children[0].children[0].material.opacity < solidOpacity);
  p.alpha = 0; view.update(run, .1); assert.equal(view.stats.visible, 0); assert.equal(root.visible, false);
  view.dispose();
});

test('song clock and every mote freeze on pause, while restart and missing runs clear immediately', () => {
  const { view } = fixture(), run = { timeS: 3, platforms: [platform()] };
  view.update(run, .1); const root = view.object.children[0], pool = root.children[2];
  const matrices = pool.instanceMatrix.array.slice(), before = { ...view.stats };
  run.timeS = 10; run.platforms[0].alpha = .2;
  view.update(run, 7, { paused: true });
  assert.deepEqual(pool.instanceMatrix.array, matrices); assert.deepEqual(view.stats, before);
  view.clear(); assert.equal(view.stats.visible, 0); assert.equal(pool.count, 0);
  view.update(run, .1); view.update(null, .1, { paused: true }); assert.equal(root.visible, false);
  view.update(run, .1); view.update(run, .1, { mode: 'menu' }); assert.equal(root.visible, false);
  view.dispose();
});

test('reused slots change lyrics without allocating meshes, materials or textures during playback', () => {
  const { view, doc } = fixture(), children = view.object.children.slice(), run = { timeS: 0, platforms: [] };
  const resources = children.flatMap(root => [...root.children[1].children.map(m => m.material), root.children[2].geometry]);
  for (let i = 0; i < 80; i++) {
    run.timeS = i / 10; run.platforms = [platform({ text: i % 2 ? 'Float' : 'Breathe', id: 'air-word-' + i % AIR_WORD_CAPACITY })];
    view.update(run, .1);
  }
  assert.deepEqual(view.object.children, children); assert.equal(doc.canvases.length, AIR_WORD_CAPACITY);
  assert.deepEqual(children.flatMap(root => [...root.children[1].children.map(m => m.material), root.children[2].geometry]), resources);
  const texture = children[0].children[1].children[0].material.map;
  let released = 0; texture.addEventListener('dispose', () => released++);
  view.dispose(); view.dispose(); assert.equal(released, 1);
});

test('Gentle and refused brightness budgets retain readable platforms with calmer particles', () => {
  for (const options of [{ feel: { get: () => true } }, { photosafe: { request: () => false } }]) {
    const { view } = fixture('mainframe', options);
    view.update({ timeS: 3, platforms: [platform()] }, .1);
    assert.equal(view.stats.solid, 1); assert.equal(view.stats.particles, 12);
    view.dispose();
  }
  for (let i = 0; i < 40; i++) for (const alpha of [0, .3, 1]) {
    const v = airWordBit(i, 15, 12, 18, alpha, false);
    assert.ok(Object.values(v).every(Number.isFinite));
    assert.ok(Math.abs(v.x) <= 6 && Math.abs(v.z) <= 9 && v.size >= 0 && v.size <= .075);
    if (!alpha) assert.equal(v.size, 0);
  }
});
