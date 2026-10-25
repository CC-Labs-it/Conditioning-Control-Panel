import { test } from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { setTier } from '../render/quality.js';

const data = source => 'data:text/javascript,' + encodeURIComponent(source);
const mocks = {
  'three/addons/libs/meshopt_decoder.module.js': 'export const MeshoptDecoder={};',
  'three/addons/loaders/KTX2Loader.js': `export class KTX2Loader {
    setTranscoderPath(){return this} detectSupport(){return this}
    async loadAsync(url){return globalThis.__artLibraryFixture.texture(url)}
    dispose(){globalThis.__artLibraryFixture.workersDisposed++}
  }`,
  'three/addons/loaders/GLTFLoader.js': `export class GLTFLoader {
    setMeshoptDecoder(){return this} setKTX2Loader(){return this}
    async loadAsync(url){return globalThis.__artLibraryFixture.model(url)}
  }`,
};
const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  if (files[specifier]) return { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true };
  if (mocks[specifier]) return { url: data(mocks[specifier]), shortCircuit: true };
  return next(specifier, context);
} });
const { createArtLibrary, currentArt, GARAGE_ART_IDS } = await import('../render/art.js');
const { Group, Mesh, BoxGeometry, MeshBasicNodeMaterial, Texture } = await import('three/webgpu');
hooks.deregister();

function fixture(t) {
  const originalFetch = globalThis.fetch, resources = [], loads = new Map();
  const rows = ['emi-teacup-kart', 'emi-outfit-pool', 'emi-vehicle-petal', 'course-a', 'course-b']
    .map(id => ({ id, kind: 'model', file: id + '.glb' }));
  rows.push({ id: 'emi-faces', kind: 'texture', file: 'faces.ktx2', mipLevels: 3 });
  let manifests = 0;
  const watch = resource => { const record = { resource, disposals: 0 }; resources.push(record);
    resource.addEventListener('dispose', () => record.disposals++); return resource; };
  const count = url => loads.set(String(url), (loads.get(String(url)) || 0) + 1);
  const api = { workersDisposed: 0, resources, loads, get manifests() { return manifests; },
    texture(url) { count(url); return watch(new Texture()); },
    model(url) { count(url); const scene = new Group(), texture = watch(new Texture());
      const geometry = watch(new BoxGeometry()), material = watch(new MeshBasicNodeMaterial({ map: texture }));
      scene.add(new Mesh(geometry, material), new Mesh(geometry, material)); return { scene, scenes: [scene] }; },
  };
  globalThis.__artLibraryFixture = api;
  globalThis.fetch = async () => { manifests++; return { ok: true, json: async () => rows }; };
  t.after(() => { globalThis.fetch = originalFetch; delete globalThis.__artLibraryFixture; setTier('high'); });
  return api;
}

test('renderer library reuses common source objects across leases and protects them through consumer disposal', async t => {
  const f = fixture(t), raw = {}, library = createArtLibrary({ three: raw });
  assert.equal(createArtLibrary(raw), library);
  assert.ok(GARAGE_ART_IDS.includes('emi-teacup-kart') && GARAGE_ART_IDS.includes('emi-faces'));
  const want = ['emi-teacup-kart', 'emi-faces', 'emi-outfit-pool', 'emi-vehicle-petal'];
  const garage = await library.acquire({ trackId: 'shared', want });
  const first = await library.acquire({ trackId: 'fixture-a', want: [...want, 'course-a'] });
  const second = await library.acquire({ trackId: 'fixture-b', want: [...want, 'course-b'] });
  assert.equal(currentArt(), second);
  const anotherGarage = await library.acquire({ trackId: 'shared', want });
  assert.equal(currentArt(), second, 'garage leases never replace current course art');
  assert.equal(first.model('emi-teacup-kart'), second.model('emi-teacup-kart'));
  assert.equal(garage.tex('emi-faces'), second.tex('emi-faces'));
  for (const calls of f.loads.values()) assert.equal(calls, 1);
  assert.equal(f.manifests, 1);
  const source = garage.model('emi-teacup-kart').scene.children[0];
  for (const resource of [source.geometry, source.material, source.material.map, garage.tex('emi-faces')]) {
    assert.equal(library.isBorrowed(resource), true); resource.dispose(); resource.dispose();
  }
  // Geometry is shared by clones, while per-world cloned materials keep ordinary disposal.
  const clone = source.material.clone(); let cloneDisposed = 0;
  clone.addEventListener('dispose', () => cloneDisposed++); clone.dispose(); assert.equal(cloneDisposed, 1);
  assert.equal(Object.keys(source.material).includes('dispose'), false, 'copying material fields must not copy the ownership guard');
  const courseResources = first.model('course-a').scene.children[0];
  first.dispose(); first.dispose();
  for (const resource of [courseResources.geometry, courseResources.material, courseResources.material.map]) {
    assert.equal(library.isBorrowed(resource), false);
    assert.equal(f.resources.find(row => row.resource === resource).disposals, 1);
  }
  assert.equal(currentArt(), second);
  second.dispose(); anotherGarage.release(); garage.dispose();
  assert.equal(currentArt(), null);
  const retained = f.resources.filter(row => library.isBorrowed(row.resource));
  assert.ok(retained.length > 0 && retained.every(row => row.disposals === 0));
  const next = await library.acquire({ trackId: 'shared', want });
  assert.equal(next.model('emi-teacup-kart').scene.children[0].geometry, source.geometry);
  library.dispose(); library.dispose();
  assert.equal(f.workersDisposed, 1); assert.equal(library.stats.entries, 0);
  assert.ok(f.resources.every(row => row.disposals === 1), 'all source geometry, materials and embedded/standalone textures freed once');
  await assert.rejects(library.acquire(), /disposed/);
});

test('overlapping course leases keep shared transient assets alive until their last release', async t => {
  const f = fixture(t), library = createArtLibrary({});
  const [a, b] = await Promise.all([library.acquire({ trackId: 'fixture-a', want: ['course-a'] }),
    library.acquire({ trackId: 'fixture-b', want: ['course-a'] })]);
  const source = a.model('course-a'), geometry = source.scene.children[0].geometry;
  assert.equal(source, b.model('course-a')); assert.equal(f.loads.size, 1);
  a.dispose(); geometry.dispose(); assert.ok(f.resources.every(row => row.disposals === 0));
  b.dispose(); assert.ok(f.resources.every(row => row.disposals === 1));
  await library.acquire({ trackId: 'fixture-a', want: ['course-a'] });
  assert.equal([...f.loads.values()][0], 2, 'evicted course geometry is decoded fresh when revisited');
  library.dispose(); assert.ok(f.resources.every(row => row.disposals === 1));
});

test('texture tier changes keep source model decoding shared but never mutate a leased texture sampler', async t => {
  const f = fixture(t), library = createArtLibrary({}), want = ['emi-teacup-kart', 'emi-faces'];
  setTier('high'); const high = await library.acquire({ trackId: 'shared', want });
  setTier('phone'); const phone = await library.acquire({ trackId: 'shared', want });
  assert.equal(high.model('emi-teacup-kart'), phone.model('emi-teacup-kart'));
  assert.notEqual(high.tex('emi-faces'), phone.tex('emi-faces'));
  assert.equal(high.tex('emi-faces').anisotropy, 8); assert.equal(phone.tex('emi-faces').anisotropy, 2);
  library.dispose(); assert.ok(f.resources.every(row => row.disposals === 1));
});

test('library shutdown during a pending model decode releases late resources once and cannot resurrect a lease', async t => {
  const f = fixture(t), library = createArtLibrary({}), makeModel = f.model;
  let finish, started;
  const began = new Promise(resolve => { started = resolve; });
  f.model = url => new Promise(resolve => { finish = () => resolve(makeModel(url)); started(); });
  const pending = library.acquire({ trackId: 'fixture', want: ['course-a'] });
  await began; library.dispose(); finish();
  const lease = await pending;
  assert.equal(lease.model('course-a'), null); assert.equal(library.stats.entries, 0); assert.equal(library.stats.leases, 0);
  assert.ok(f.resources.every(row => row.disposals === 1));
  lease.dispose(); library.dispose(); assert.ok(f.resources.every(row => row.disposals === 1));
});

test('shared embedded image bitmaps close only when their last source texture is evicted', async t => {
  const f = fixture(t), library = createArtLibrary({}), makeModel = f.model;
  let closed = 0;
  const image = { close() { closed++; } };
  f.model = url => { const model = makeModel(url); model.scene.children[0].material.map.image = image; return model; };
  const a = await library.acquire({ trackId: 'fixture', want: ['course-a'] });
  const b = await library.acquire({ trackId: 'fixture', want: ['course-b'] });
  a.dispose(); assert.equal(closed, 0);
  b.dispose(); assert.equal(closed, 1);
  library.dispose(); assert.equal(closed, 1);
});

test('failed source loads can retry without an older failed lease deleting the recovered cache entry', async t => {
  const f = fixture(t), library = createArtLibrary({}), makeModel = f.model, warn = console.warn;
  t.after(() => { console.warn = warn; }); console.warn = () => {};
  let fail = true;
  f.model = url => { if (fail) throw Error('temporary transport failure'); return makeModel(url); };
  const old = await library.acquire({ trackId: 'fixture', want: ['course-a'] });
  assert.ok(old.failed.includes('course-a'));
  fail = false;
  const recovered = await library.acquire({ trackId: 'fixture', want: ['course-a'] });
  old.dispose(); assert.ok(recovered.model('course-a')); assert.equal(library.stats.entries, 1);
  const overlap = await library.acquire({ trackId: 'fixture', want: ['course-a'] });
  assert.equal(overlap.model('course-a'), recovered.model('course-a'));
  library.dispose(); assert.ok(f.resources.every(row => row.disposals === 1));
});
