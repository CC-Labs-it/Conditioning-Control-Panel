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
const { Group, Vector3 } = await import('three/webgpu');
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

test('physical projectiles retain readable wakes through a pass, fade after a hit and clear on restart', async () => {
  const doc = globalThis.document, image = globalThis.Image;
  globalThis.document = { createElement: () => canvas() };
  globalThis.Image = class { constructor() { this.complete = true; this.naturalWidth = this.naturalHeight = 256; } set src(value) { this.onload?.(); } };
  let visuals;
  try {
    const bytes = await readFile(new URL('../art/toys/toy-disc.glb', import.meta.url));
    const model = await new GLTFLoader().setMeshoptDecoder(MeshoptDecoder).parseAsync(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength), '');
    const feel = createFeel(JSON.parse(await readFile(new URL('../feel.json', import.meta.url), 'utf8')));
    const scene = new Group();
    visuals = createItemVisuals(scene, { model: id => id === 'toy-disc' ? model : null }, feel, { photosafe: { request: () => true } });
    const root = scene.getObjectByName('item-visuals'), motes = root.children[2], toys = scene.getObjectByName('physical-toys');
    const shot = { id: 12, kind: 'disc', mode: 'throw', t: .12, p: [0, 2, 0], v: [40, 10, 0], radiusM: .55 };
    visuals.event('item-fire', { id: 'disc', p: [0, 0, 0] });
    const snapshot = structuredClone(shot);
    visuals.update({ projectiles: [shot] }, .1);
    const release = toys.children.find(o => o.type === 'Group' && o.visible);
    assert.ok(release.scale.z / release.scale.x > 1.65, 'actual GLB stretches along the throw during release');
    assert.ok(new Vector3(0, 0, -1).applyQuaternion(release.quaternion).dot(new Vector3(...shot.v).normalize()) > .9999,
      'release direction follows uphill flight, not just horizontal yaw');
    assert.deepEqual(shot, snapshot, 'presentation never alters aim, position or contact radius');
    assert.ok(motes.count >= 12, 'release cone is visible before a ribbon has built up');
    shot.v = [40, 0, 0];
    for (let i = 0; i < 7; i++) {
      shot.t = i * .1; shot.p[0] = i * 4;
      visuals.update({ projectiles: [shot] }, .1);
    }
    assert.ok(motes.count >= 40, 'actual GLB flight carries a continuous core and halo wake');
    const carrier = toys.children.find(o => o.type === 'Group' && o.visible);
    assert.ok(carrier.scale.x >= 2.8, 'the actual exported disc is materially enlarged');
    assert.equal(carrier.scale.x, carrier.scale.z, 'release deformation settles back to the authored silhouette');
    const paused = motes.count;
    visuals.update({ projectiles: [shot] }, .1, [], null, { paused: true });
    assert.equal(motes.count, paused, 'pause does not change a live wake');
    visuals.update({ projectiles: [] }, .1);
    assert.ok(motes.count > 0, 'impact retains the tail for its OUT instead of cutting it off');
    for (let i = 0; i < 5; i++) visuals.update({ projectiles: [] }, .1);
    assert.equal(motes.count, 0, 'retired tail has a bounded lifetime');
    visuals.clear();
    feel.set('gentle.on', true);
    for (let i = 0; i < 5; i++) { shot.t = i * .1; shot.p[0] = i * 4; visuals.update({ projectiles: [shot] }, .1); }
    assert.equal(motes.count, 0, 'Gentle keeps the larger physical prop without moving wake particles');
    assert.ok(visibleMeshes(toys).length > 0);
    visuals.event('item-flyby', { kind: 'disc', p: [20, 2, 0], v: [40, 0, 0] });
    visuals.update({ projectiles: [] }, .1);
    visuals.clear();
    assert.equal(motes.count, 0); assert.equal(visibleMeshes(toys).length, 0);
  } finally {
    visuals?.dispose();
    if (doc === undefined) delete globalThis.document; else globalThis.document = doc;
    if (image === undefined) delete globalThis.Image; else globalThis.Image = image;
  }
});
