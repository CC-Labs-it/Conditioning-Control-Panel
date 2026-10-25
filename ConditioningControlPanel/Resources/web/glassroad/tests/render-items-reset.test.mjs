import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { registerHooks } from 'node:module';
import { createFeel } from '../core/feel.js';
import { createState, S } from '../sim/state.js';

const hooks = registerHooks({ resolve(s, c, next) {
  const files = { three: 'three.webgpu.min.js', 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  if (files[s]) return { url: new URL('../vendor/three/build/' + files[s], import.meta.url).href, shortCircuit: true };
  if (s.startsWith('three/addons/')) return { url: new URL('../vendor/' + s, import.meta.url).href, shortCircuit: true };
  return next(s, c);
} });
const { Group } = await import('three/webgpu');
const { GLTFLoader } = await import('../vendor/three/addons/loaders/GLTFLoader.js');
const { MeshoptDecoder } = await import('../vendor/three/addons/libs/meshopt_decoder.module.js');
const { createItemVisuals } = await import('../render/items/visuals.js');
hooks.deregister();

// Only the bitmap drawing surface is inert. Actual Three materials, meshes and item pools run.
function canvas() {
  const ctx = { measureText: text => ({ width: text.length * 8 }),
    createLinearGradient: () => ({ addColorStop() {} }), createRadialGradient: () => ({ addColorStop() {} }),
    createConicGradient: () => ({ addColorStop() {} }) };
  for (const method of ['save','restore','clearRect','fillRect','strokeRect','beginPath','closePath','moveTo','lineTo',
    'arc','ellipse','fill','stroke','translate','rotate','scale','setTransform','drawImage','rect','clip','fillText','strokeText','quadraticCurveTo','bezierCurveTo']) ctx[method] = () => {};
  const result = { width: 1, height: 1, getContext: kind => kind === '2d' ? ctx : null }; ctx.canvas = result;
  return result;
}
const visibleMeshes = root => { const rows = []; root.traverseVisible(o => { if (o.isMesh && (!o.isInstancedMesh || o.count)) rows.push(o); }); return rows; };

test('item restart clears real toy effects, broken tethers, pickup turns and confetti', async () => {
  const doc = globalThis.document, image = globalThis.Image;
  globalThis.document = { createElement: tag => { assert.equal(tag, 'canvas'); return canvas(); } };
  globalThis.Image = class { constructor() { this.complete = true; this.naturalWidth = this.naturalHeight = 256; } set src(value) { this.url = value; this.onload?.(); } };
  let visuals;
  try {
    const models = new Map(), loader = new GLTFLoader().setMeshoptDecoder(MeshoptDecoder);
    for (const id of ['toy-chain-link','toy-tether-head','toy-tether-cuff']) {
      const bytes = await readFile(new URL('../art/toys/' + id + '.glb', import.meta.url));
      models.set(id, await loader.parseAsync(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength), ''));
    }
    const feel = createFeel(JSON.parse(await readFile(new URL('../feel.json', import.meta.url), 'utf8')));
    const scene = new Group(), karts = [{ st: createState() }, { st: createState() }];
    karts[1].st[S.stalled] = 1;
    visuals = createItemVisuals(scene, { model: id => models.get(id) }, feel, { photosafe: { request: () => true } });
    const root = scene.getObjectByName('item-visuals'), toys = scene.getObjectByName('physical-toys'), tethers = scene.getObjectByName('toy-tethers');
    const card = root.children.find(o => o.userData.pickupFace === 0);
    const confetti = root.children.find(o => o.isInstancedMesh && o.geometry.parameters?.height === .64);
    const box = { alive: true, k: 1, p: [0,0,0], n: [0,1,0] };
    visuals.update({ boxes: [box] }, 0, karts);
    const fresh = { p: card.position.toArray(), q: card.quaternion.toArray(), scale: card.scale.toArray() };
    for (let i = 0; i < 6; i++) visuals.update({ boxes: [box] }, .1, karts);
    visuals.event('item-box', { box: 0, racer: 0 });
    visuals.event('snap-burst', { p: [0,0,0] });
    const tow = { by: 0, target: 1, k: .5, t: 2, from: [0,0,0], to: [0,0,-20] };
    visuals.update({ boxes: [{ ...box, alive: false }], held: [{ racer: 0, id: 'melt' }], tethers: [tow] }, .1, karts);
    visuals.event('tether-break', { by: 0, target: 1, reason: 'pass' });
    visuals.update({ boxes: [{ ...box, alive: false }] }, .1, karts);
    assert.ok(visibleMeshes(toys).length > 0, 'Snap ring is live before restart');
    assert.ok(visibleMeshes(tethers).length > 0, 'broken chain is live before restart');
    assert.ok(confetti.count > 0, 'pickup confetti is live before restart');
    visuals.clear();
    assert.equal(visibleMeshes(toys).length, 0); assert.equal(visibleMeshes(tethers).length, 0);
    assert.equal(confetti.count, 0); assert.equal(card.visible, false);
    visuals.update({ boxes: [box] }, 0, karts);
    assert.deepEqual({ p: card.position.toArray(), q: card.quaternion.toArray(), scale: card.scale.toArray() }, fresh,
      'new pickup restores full scale and fresh clock, without the old turn');
    assert.equal(confetti.count, 0, 'old pickup confetti must not resume');
    visuals.clear();
    visuals.update({ boxes: [{ ...box, alive: false }] }, .1, karts);
    assert.equal(card.visible, false, 'previous alive state must not create a phantom pickup');
    assert.equal(confetti.count, 0);
  } finally {
    visuals?.dispose();
    if (doc === undefined) delete globalThis.document; else globalThis.document = doc;
    if (image === undefined) delete globalThis.Image; else globalThis.Image = image;
  }
});

test('world restart clears items alongside the song cue renderers', async () => {
  const source = await readFile(new URL('../render/scene.js', import.meta.url), 'utf8');
  const reset = source.slice(source.indexOf('resetReactions:'));
  for (const owner of ['itemVis', 'marquees', 'veils']) assert.match(reset, new RegExp(owner + '(?:\\?\\.)?\\.?clear\\(\\)'));
});
