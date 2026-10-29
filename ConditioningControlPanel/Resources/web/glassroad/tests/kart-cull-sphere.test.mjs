// Racer skinned meshes cull per camera on one fixed kart sphere (render/dressing.js KART_CULL). The real R4 kart,
// every one of its clips, every outfit and every vehicle stay well inside it, and a look swap keeps it.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';

const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { three: 'build/three.webgpu.min.js', 'three/webgpu': 'build/three.webgpu.min.js', 'three/tsl': 'build/three.tsl.min.js' };
  const file = files[specifier] || (specifier.startsWith('three/addons/') ? 'addons/' + specifier.slice(13) : null);
  return file ? { url: new URL('../vendor/three/' + file, import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
// wardrobe.js reads its catalogs with fetch at import; serve the local files.
const realFetch = globalThis.fetch;
globalThis.fetch = async (url) => {
  const u = String(url);
  if (u.startsWith('file:')) { try { const text = readFileSync(new URL(u), 'utf8'); return { ok: true, json: async () => JSON.parse(text) }; } catch (e) { return { ok: false }; } }
  return realFetch(url);
};
// buildKart paints EMI's face on a canvas; a no-op 2D context is enough here.
const hadDocument = 'document' in globalThis;
if (!hadDocument) globalThis.document = { createElement: () => ({ width: 0, height: 0, getContext: () => new Proxy({}, { get: () => () => {} }) }) };
const THREE = await import('three/webgpu');
const { GLTFLoader } = await import('three/addons/loaders/GLTFLoader.js');
const { MeshoptDecoder } = await import('three/addons/libs/meshopt_decoder.module.js');
const { buildKart, KART_CULL, kartCullSphere } = await import('../render/dressing.js');
const { dressKart, CATALOG } = await import('../render/wardrobe.js');
hooks.deregister(); globalThis.fetch = realFetch;

await MeshoptDecoder.ready;
const loader = new GLTFLoader().setMeshoptDecoder(MeshoptDecoder);
loader.register(parser => ({ name: 'geometryOnlyTest', beforeRoot() {
  const strip = v => { if (!v || typeof v !== 'object') return; for (const k of Object.keys(v)) { if (k.endsWith('Texture')) delete v[k]; else strip(v[k]); } };
  for (const m of parser.json.materials || []) strip(m);
} }));
const load = async name => {
  const bytes = readFileSync(new URL('../art/models/' + name + '.glb', import.meta.url));
  return loader.parseAsync(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength), '');
};
const ids = [...CATALOG.outfits.map(o => o.id || o), ...CATALOG.vehicles.map(v => v.id || v)].filter(id => typeof id === 'string');
const gltfs = new Map([['emi-teacup-kart', await load('emi-teacup-kart')]]);
for (const id of ids) gltfs.set(id, await load(id));
const art = { model: id => gltfs.get(id) || null, row: () => ({}), tex: () => null };
test.after(() => { if (!hadDocument) delete globalThis.document; });

const skinned = root => { const out = []; root.traverse(o => { if (o.isSkinnedMesh) out.push(o); }); return out; };

test('every clip, outfit and vehicle stays inside the fixed kart culling sphere with margin', (t) => {
  assert.ok(ids.length >= 15, 'catalog lists outfits and vehicles: ' + ids.length);
  const model = buildKart(art), clips = gltfs.get('emi-teacup-kart').animations, centre = new THREE.Vector3(...KART_CULL.center);
  assert.ok(clips.length >= 20, 'every R4 clip is sampled');
  const mixer = new THREE.AnimationMixer(model), v = new THREE.Vector3();
  let worst = 0, sampled = 0;
  for (const id of [null, ...ids]) {
    const look = id ? (id.includes('vehicle') ? { vehicle: id } : { outfit: id }) : null;
    const dress = look ? dressKart(art, model, look) : null;
    if (look) assert.ok(dress.outfit || dress.vehicle, id + ' dressed');
    const meshes = skinned(model);
    for (const m of meshes) {
      assert.equal(m.frustumCulled, true, (id || 'base') + ' ' + m.name + ' culls');
      assert.ok(m.boundingSphere && m.boundingSphere.center.equals(centre) && m.boundingSphere.radius === KART_CULL.radius, (id || 'base') + ' ' + m.name + ' sphere');
      assert.ok(m.position.lengthSq() === 0 && m.quaternion.w === 1 && m.scale.x === 1, 'identity local transform shares the sphere space');
    }
    for (const clip of clips) {
      mixer.stopAllAction(); mixer.clipAction(clip).reset().play();
      for (let k = 0; k <= 8; k++) {
        mixer.setTime(clip.duration * k / 8); model.updateMatrixWorld(true);
        for (const m of meshes) {
          const p = m.geometry.getAttribute('position'), index = m.geometry.index, step = Math.max(1, Math.floor((index ? index.count : p.count) / 600));
          for (let i = 0; i < (index ? index.count : p.count); i += step) {
            m.getVertexPosition(index ? index.getX(i) : i, v);
            worst = Math.max(worst, v.distanceTo(centre)); sampled++;
          }
        }
      }
    }
    dress?.dispose();
  }
  assert.ok(sampled > 100000, 'sampled ' + sampled);
  assert.ok(worst < KART_CULL.radius * .7, `posed extent ${worst.toFixed(3)} keeps 30% margin under ${KART_CULL.radius}`);
  t.diagnostic(`${ids.length} looks, ${clips.length} clips, ${sampled} posed vertices: kart posed extent ${worst.toFixed(3)} of ${KART_CULL.radius}`);
});

test('an unculled kart (garage, menu rider) dresses unculled; a racer swap keeps the sphere', () => {
  const garage = buildKart(art);
  garage.traverse(o => { if (o.isMesh) o.frustumCulled = false; });
  const outfit = ids.find(id => id.includes('outfit')), vehicle = ids.find(id => id.includes('vehicle'));
  const a = dressKart(art, garage, { outfit, vehicle });
  for (const m of skinned(garage)) assert.equal(m.frustumCulled, false, 'garage ' + m.name);
  a.dispose();
  const racer = buildKart(art);
  let d = dressKart(art, racer, { outfit });
  d.dispose(); d = dressKart(art, racer, { vehicle });
  for (const m of skinned(racer)) assert.equal(m.frustumCulled && m.boundingSphere?.radius, KART_CULL.radius, 'racer ' + m.name);
  d.dispose();
  // The helper itself never shares one Sphere object between meshes.
  const x = kartCullSphere(new THREE.SkinnedMesh()), y = kartCullSphere(new THREE.SkinnedMesh());
  assert.notEqual(x.boundingSphere, y.boundingSphere);
});
