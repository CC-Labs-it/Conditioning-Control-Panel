import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { registerHooks } from 'node:module';
import { createKartLods, phoneModels, phoneModelId } from '../render/phoneModels.js';
import { setTier } from '../render/quality.js';
import { setDevice } from '../render/deviceClass.js';
const hooks = registerHooks({ resolve(specifier, context, next) {
  const file = specifier === 'three/tsl' ? 'three.tsl.min.js' : ['three', 'three/webgpu'].includes(specifier) ? 'three.webgpu.min.js' : null;
  return file ? { url: new URL('../vendor/three/build/' + file, import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { GLTFLoader } = await import('../vendor/three/addons/loaders/GLTFLoader.js');
const { MeshoptDecoder } = await import('../vendor/three/addons/libs/meshopt_decoder.module.js');
const { clone } = await import('../vendor/three/addons/utils/SkeletonUtils.js');
const { AnimationMixer, Vector3 } = await import('../vendor/three/build/three.webgpu.min.js');
const { createEmiSkin } = await import('../render/emiSkin.js');
hooks.deregister();
await MeshoptDecoder.ready;
const bytes = readFileSync(new URL('../art/models/emi-teacup-kart.glb', import.meta.url));
const data = JSON.parse(readFileSync(new URL('../art/models/emi-teacup-kart-lod1.json', import.meta.url)));
const loader = new GLTFLoader().setMeshoptDecoder(MeshoptDecoder);
loader.register(parser => ({ name: 'geometryOnly', beforeRoot() {
  const strip = v => { if (!v || typeof v !== 'object') return; for (const k of Object.keys(v)) { if (k.endsWith('Texture')) delete v[k]; else strip(v[k]); } };
  for (const m of parser.json.materials || []) strip(m);
} }));
const gltf = await loader.parseAsync(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength), '');
const meshes = root => { const out = []; root.traverse(o => { if (o.isMesh) out.push(o); }); return out; };
test.afterEach(() => { setTier('high'); setDevice('desktop'); });

test('phone scenery aliases all resolve to existing simpler painted assets', () => {
  const manifest = JSON.parse(readFileSync(new URL('../art/manifest.json', import.meta.url)));
  const rows = new Map(manifest.map(r => [r.id, r]));
  for (const row of manifest) {
    const id = phoneModelId(row.id);
    if (id === row.id) continue;
    assert.ok(rows.get(id).tris < row.tris, row.id);
    if (id.startsWith('chess-')) assert.ok(rows.has(id + '-vtx'), 'paint sidecar retained');
  }
  setDevice('phone'); assert.ok(phoneModels()); // High pin cannot undo the geometry saving.
});

test('reduced kart indices match the accepted source and cut triangles by at least half', () => {
  assert.equal(data.sourceSha256, createHash('sha256').update(bytes).digest('hex'), 'regenerate after a base asset change');
  const low = data.meshes.reduce((sum, m) => sum + m.indices.length / 3, 0);
  assert.equal(low, 1956);
  assert.ok(low < 4892 / 2);
  assert.equal(data.meshes.length, meshes(gltf.scene).length);
});

test('player stays full detail; rival transitions preserve rig, materials and effects with hysteresis', () => {
  setTier('phone');
  const fleet = createKartLods(data), player = clone(gltf.scene), rival = clone(gltf.scene);
  const skin = createEmiSkin(rival, { cloneMaterials: false });
  assert.ok(meshes(rival).some(m => m.geometry.hasAttribute('garageShell')), 'test the customized runtime geometry');
  assert.equal(fleet.bind(player, 0), null);
  const original = meshes(rival).map(m => ({ mesh: m, geometry: m.geometry, material: m.material, skeleton: m.skeleton }));
  const lod = fleet.bind(rival, 1);
  lod.update(49); assert.ok(lod.reduced);
  for (const { mesh, geometry, material, skeleton } of original) {
    assert.notEqual(mesh.geometry, geometry);
    assert.equal(mesh.material, material); assert.equal(mesh.skeleton, skeleton);
    for (const key of Object.keys(geometry.attributes)) {
      const a = mesh.geometry.attributes[key], b = geometry.attributes[key];
      assert.equal(a.count, b.count);
      for (let i = 0; i < a.count; i++) for (let j = 0; j < a.itemSize; j++) assert.equal(a.getComponent(i, j), b.getComponent(i, j), key);
    }
  }
  lod.update(44); assert.ok(lod.reduced, 'no chatter around the enter distance');
  lod.update(40); assert.equal(lod.reduced, false, 'hit camera and nearby racers are detailed');
  for (const p of original) assert.equal(p.mesh.geometry, p.geometry);
  lod.update(60); setTier('high'); lod.update(60); assert.equal(lod.reduced, false);
  setDevice('phone'); lod.update(100); assert.ok(lod.reduced);
  fleet.dispose(); for (const p of original) assert.equal(p.mesh.geometry, p.geometry);
  skin.dispose();
});

test('all animation clips keep low-detail vertices at their original skinned positions', () => {
  setTier('phone');
  const model = clone(gltf.scene), fleet = createKartLods(data), lod = fleet.bind(model, 1);
  const mixer = new AnimationMixer(model), vertex = new Vector3();
  for (const clip of gltf.animations) {
    mixer.stopAllAction(); mixer.clipAction(clip).reset().play();
    for (const fraction of [0, .25, .5, .75, 1]) {
      mixer.setTime(clip.duration * fraction); model.updateMatrixWorld(true);
      lod.update(100);
      for (const mesh of meshes(model)) {
        mesh.skeleton?.update();
        for (let i = 0; i < mesh.geometry.index.count; i += 19) {
          mesh.getVertexPosition(mesh.geometry.index.getX(i), vertex);
          assert.ok(Number.isFinite(vertex.length()) && vertex.distanceTo(new Vector3(0, .8, 0)) < 2, clip.name);
        }
      }
    }
  }
  fleet.dispose();
});

test('missing or incompatible LOD data keeps original geometry, and valid rivals share one reduced allocation', () => {
  for (const data0 of [null, { meshes: {} }, { meshes: [{ ...data.meshes[0], indices: [-1, 0, 1] }] }]) {
    const model = clone(gltf.scene), before = meshes(model).map(m => m.geometry), fleet = createKartLods(data0);
    fleet.bind(model, 1).update(100, true);
    assert.deepEqual(meshes(model).map(m => m.geometry), before);
    fleet.dispose();
  }
  const fleet = createKartLods(data), a = clone(gltf.scene), b = clone(gltf.scene);
  fleet.bind(a, 1).update(100, true); fleet.bind(b, 2).update(100, true);
  let disposed = 0;
  for (const [i, mesh] of meshes(a).entries()) {
    assert.equal(mesh.geometry, meshes(b)[i].geometry);
    mesh.geometry.addEventListener('dispose', () => disposed++);
  }
  fleet.dispose(); fleet.dispose(); assert.equal(disposed, data.meshes.length);
});
