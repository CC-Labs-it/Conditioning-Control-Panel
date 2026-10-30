import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { registerHooks } from 'node:module';
import { S, createState } from '../sim/state.js';

const hooks = registerHooks({ resolve(s, c, next) {
  if (s === 'three' || s === 'three/webgpu') return { url: new URL('../vendor/three/build/three.webgpu.min.js', import.meta.url).href, shortCircuit: true };
  return next(s, c);
} });
const { Group } = await import('three/webgpu');
const { GLTFLoader } = await import('../vendor/three/addons/loaders/GLTFLoader.js');
const { MeshoptDecoder } = await import('../vendor/three/addons/libs/meshopt_decoder.module.js');
const { createTethers } = await import('../render/items/tethers.js');
hooks.deregister();

test('real GLB tether instances pause, split, clear and leave shared assets alive', async () => {
  const models = new Map(), loader = new GLTFLoader().setMeshoptDecoder(MeshoptDecoder);
  let sourceDisposals = 0;
  for (const id of ['toy-chain-link', 'toy-tether-head', 'toy-tether-cuff']) {
    const bytes = await readFile(new URL('../art/toys/' + id + '.glb', import.meta.url));
    const model = await loader.parseAsync(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength), '');
    model.scene.traverse(o => {
      if (!o.isMesh) return;
      o.geometry.addEventListener('dispose', () => sourceDisposals++);
      for (const m of Array.isArray(o.material) ? o.material : [o.material]) m.addEventListener('dispose', () => sourceDisposals++);
    });
    models.set(id, model);
  }
  const root = new Group(), tethers = createTethers(root, { model: id => models.get(id) });
  const row = { id: 1, by: 0, target: 1, t: 1, k: 0.25, from: [0, 0, 0], to: [3, 1, -20] };
  const karts = [{ st: createState() }, { st: createState() }]; karts[1].st[S.stalled] = 1;
  const snapshot = () => { const rows = []; root.traverseVisible(o => { if (o.isMesh) rows.push({ name: o.name, p: o.position.toArray(), q: o.quaternion.toArray(), instances: o.instanceMatrix ? [...o.instanceMatrix.array] : null }); }); return rows; };
  tethers.update({ tethers: [row] }, .1, karts);
  const live = snapshot(), chains = live.filter(o => o.instances);
  assert.ok(chains.length > 0 && chains.every(o => o.instances.length === 160), 'ten physical links per chain');
  tethers.update({ tethers: [row] }, 0, karts); assert.deepEqual(snapshot(), live, 'pause freezes sag and transforms');
  tethers.event('tether-break', { by: 0, target: 1, reason: 'pass' });
  tethers.update({ tethers: [] }, .1, karts);
  const broken = snapshot(); assert.ok(broken.length > 0); assert.notDeepEqual(broken, live);
  tethers.update({ tethers: [] }, 0, karts); assert.deepEqual(snapshot(), broken, 'pause freezes recoil');
  tethers.update(null, 0, karts); assert.deepEqual(snapshot(), [], 'reset clears live props and pending breaks');
  tethers.update({ tethers: [row] }, .1, karts); assert.ok(snapshot().length > 0);
  tethers.clear(); assert.deepEqual(snapshot(), []);
  tethers.dispose(); assert.equal(root.children.length, 0); assert.equal(sourceDisposals, 0);
});
