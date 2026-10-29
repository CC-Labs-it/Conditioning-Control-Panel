import { test } from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { setTier } from '../render/quality.js';
import { setDevice } from '../render/deviceClass.js';

const moduleUrl = source => 'data:text/javascript,' + encodeURIComponent(source);
const modules = {
  'three/webgpu': 'export const RepeatWrapping=1,ClampToEdgeWrapping=2,SRGBColorSpace=3,NoColorSpace=4;',
  'three/addons/libs/meshopt_decoder.module.js': 'export const MeshoptDecoder={};',
  'three/addons/loaders/KTX2Loader.js': `export class KTX2Loader {
    setTranscoderPath(){return this} detectSupport(){return this} dispose(){}
  }`,
  'three/addons/loaders/GLTFLoader.js': `export class GLTFLoader {
    setMeshoptDecoder(){return this} setKTX2Loader(){return this}
    async loadAsync(url){return {url}}
    async parseAsync(source){globalThis.__artProbe.parsed=true;return {source}}
  }`,
};
const hooks = registerHooks({ resolve(specifier, context, next) {
  const source = modules[specifier] || (specifier === './vertexShade.js' && context.parentURL.endsWith('/render/art.js')
    ? 'export async function attachVertexShade(){if(globalThis.__artProbe.rejectShade)throw Error("incompatible optional detail");return true;}' : null);
  return source ? { url: moduleUrl(source), shortCircuit: true } : next(specifier, context);
} });
const { loadArt } = await import('../render/art.js');
hooks.deregister();
const originalFetch = globalThis.fetch;
const manifest = rows => ({ ok: true, json: async () => rows });

test('phone devices load the simpler scenery once and retain base kart plus optional distant indices', async t => {
  t.after(() => { globalThis.fetch = originalFetch; setTier('high'); setDevice('desktop'); });
  const want = ['chess-knight-lod0', 'toy-pendulum-lod0', 'toy-pendulum-lod1', 'emi-teacup-kart'];
  const ids = [...want, 'chess-knight-lod1', 'emi-teacup-kart-lod1'];
  const rows = ids.map(id => ({ id, kind: id === 'emi-teacup-kart-lod1' ? 'index-lod' : 'model', file: id + '.glb' }));
  for (const tier of ['phone', 'high']) {
    setDevice('phone'); setTier(tier);
    globalThis.fetch = async url => String(url).endsWith('manifest.json') ? manifest(rows) : manifest({ meshes: [] });
    const art = await loadArt({}, { want });
    assert.equal(art.model('chess-knight-lod0'), art.model('chess-knight-lod1'));
    assert.ok(!art.requested.includes('chess-knight-lod0'));
    assert.equal(art.requested.filter(id => id.startsWith('toy-pendulum')).length, 1);
    assert.ok(art.model('emi-teacup-kart'));
    assert.deepEqual(art.model('emi-teacup-kart-lod1'), { meshes: [] });
    art.dispose();
  }
});

test('art loads only the selected Threshold terrace LOD and preserves both runtime finish LODs', async t => {
  t.after(() => { globalThis.fetch = originalFetch; setTier('high'); });
  const want = ['threshold-slot-row-lod0', 'threshold-slot-row-lod1', 'finish-portal-lod0', 'finish-portal-lod1'];
  globalThis.fetch = async () => manifest(want.map(id => ({ id, kind: 'model', file: id + '.glb' })));
  for (const tier of ['high', 'mid', 'phone']) {
    setTier(tier);
    const art = await loadArt({}, { want });
    assert.ok(art.model('threshold-slot-row-lod' + (tier === 'phone' ? 1 : 0)));
    assert.equal(art.model('threshold-slot-row-lod' + (tier === 'phone' ? 0 : 1)), null);
    assert.ok(art.model('finish-portal-lod0') && art.model('finish-portal-lod1'));
    art.dispose();
  }
});

test('optional shading downloads during the GLB request and malformed or failed detail preserves the model', async t => {
  t.after(() => { globalThis.fetch = originalFetch; delete globalThis.__artProbe; });
  const rows = [{ id: 'fixture', kind: 'model', file: 'fixture.glb' },
    { id: 'fixture-shade', kind: 'vertex-data', sourceFile: 'fixture.glb', file: 'fixture.shade' }];
  for (const failure of ['ok', 'malformed', 'http', 'network']) {
    let releaseSource, shadeStarted = false;
    const source = new Promise(resolve => { releaseSource = resolve; });
    globalThis.__artProbe = { parsed: false, rejectShade: failure === 'malformed' };
    globalThis.fetch = async url => {
      const path = String(url);
      if (path.endsWith('manifest.json')) return manifest(rows);
      if (path.endsWith('.shade')) {
        shadeStarted = true;
        if (failure === 'network') throw Error('offline optional detail');
        return { ok: failure !== 'http', arrayBuffer: async () => new ArrayBuffer(2) };
      }
      assert.ok(shadeStarted, 'shade transport starts before waiting for the GLB');
      return { ok: true, arrayBuffer: () => source };
    };
    const pending = loadArt({}, { want: ['fixture'] });
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(shadeStarted, true);
    assert.equal(globalThis.__artProbe.parsed, false, 'model decoding still waits for complete source bytes');
    releaseSource(new ArrayBuffer(4));
    const art = await pending;
    assert.ok(art.model('fixture'), failure + ' optional detail must not hide an intact model');
    assert.ok(!art.failed.includes('fixture'));
    assert.deepEqual(art.vertexShading[failure === 'ok' ? 'applied' : 'skipped'], ['fixture']);
    art.dispose();
  }
});

test('all five course leases preload the same helicopter hat their renderer selects', async t => {
  t.after(() => { globalThis.fetch = originalFetch; });
  const ids = ['threshold','keep','mainframe','boudoir'].map(id => `glide-${id}-beanie`);
  globalThis.fetch = async () => manifest(ids.map(id => ({ id, kind: 'model', file: id + '.glb' })));
  for (const trackId of ['threshold','keep','mainframe','boudoir','goonworks']) {
    const art = await loadArt({}, {trackId, want: []});
    const expected = `glide-${trackId === 'goonworks' ? 'threshold' : trackId}-beanie`;
    assert.ok(art.model(expected), trackId + ' preloads ' + expected);
    art.dispose();
  }
});
