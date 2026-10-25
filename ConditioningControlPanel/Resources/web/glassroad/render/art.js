/* render/art.js - loads the wave 1 art set (art/manifest.json, art/ART.md) for buildWorld.
 * KTX2 textures go through the basis transcoder, GLBs through GLTFLoader with the meshopt decoder.
 * Every asset is optional: a failed or missing one comes back null and the world keeps its greybox
 * stand-in, so a broken file never blocks a race. Masks are linear data; colour textures are sRGB.
 */
import { RepeatWrapping, ClampToEdgeWrapping, SRGBColorSpace, NoColorSpace } from 'three/webgpu';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { KTX2Loader } from 'three/addons/loaders/KTX2Loader.js';
import { MeshoptDecoder } from 'three/addons/libs/meshopt_decoder.module.js';
import { Q } from './quality.js';
import { attachVertexShade } from './vertexShade.js';
import { MATERIAL_DETAIL_IDS, materialDetailId } from './materialDetailMath.js';
import { keepArtPlan } from './keepTheme.js';

const BASE = new URL('../art/', import.meta.url);
const TRANSCODER = new URL('../vendor/three/addons/libs/basis/', import.meta.url).href;
const WRAP = { repeat: RepeatWrapping, clamp: ClampToEdgeWrapping };

/** The ids buildWorld asks for; anything else in the manifest stays on disk until a later wave. */
export const WANT = [
  'toy-pendulum-lod0', 'toy-pendulum-lod1', 'toy-disc', 'toy-tether-head', 'toy-tether-cuff', 'toy-chain-link', 'toy-big-spiral',
  'road-bands-mask', 'road-glass-roughness', 'road-glass-normal', 'rail-glow-mask', 'boost-chevron-mask',
  'sky-dawn-galaxy', 'sky-deep-galaxy', 'sky-afterglow-galaxy', 'sky-dawn-stars', 'sky-deep-stars', 'sky-afterglow-stars',
  'sky-spiral-open-mask', 'emi-teacup-kart', 'countdown-ring', 'finish-portal-lod0',
  'bubble', 'card-drop', 'card-lens', 'card-orbit', 'card-petal', 'pocket-watch', 'spiral-disc', 'slot-cabinet-lod0',
  'chess-bishop-lod0', 'chess-king-lod0', 'chess-knight-lod0', 'chess-pawn-lod0', 'chess-queen-lod0', 'chess-rook-lod0',
  // wave 2 particle atlas and screen masks (FXART lane: particles, motes, wakes, screenfx)
  'particles-atlas', 'fx-frost-edge', 'fx-melt-flow', 'fx-crack', 'fx-iris', 'fx-noise',
  // wave 2 models (MODELS lane: item box card, effect-bubble shell, Melt puddle, finish LOD, rival accessories)
  'finish-portal-lod1', 'item-card', 'effect-bubble', 'fx-melt-spiral',
  'head-bow', 'head-cat-ears', 'head-halo', 'head-spiral-crown',
  // polish 2: Astra's wave 3 face atlas for emi_screen (render/faces.js draws the same 16 ids until it lands)
  'emi-faces',
  // ART5: the bot wardrobe and the two vehicle variants on the R4 kart rig (render/wardrobe.js)
  'emi-outfit-croupier', 'emi-outfit-pool', 'emi-outfit-varsity', 'emi-outfit-builder', 'emi-outfit-cheer', 'emi-outfit-swim',
  'emi-outfit-drone-protocol', 'emi-outfit-snow-day', 'emi-vehicle-petal', 'emi-vehicle-trinket',
  'emi-vehicle-pendulum','emi-vehicle-reverie','emi-vehicle-house-edge','emi-vehicle-checkmate',
  'emi-vehicle-lovebomb','emi-vehicle-feedback','emi-vehicle-velvet-lock','emi-vehicle-undertow',
  // Starting terrace: the delivered cabinet rows, selected by the current quality tier.
  'threshold-slot-row-lod0', 'threshold-slot-row-lod1',
  // wave 2 stepping stones (render/stones.js): Astra's static Threshold platform, one clone per stone
  'stone-threshold',
];
/** Wanted before they ship: missing from the manifest is quiet (not in failed); a broken file still counts. */
const LATER = new Set([]);

let current = null;
const liveLeases = new Set(), libraries = new WeakMap();
export const GARAGE_ART_IDS = Object.freeze(WANT.filter(id => id === 'emi-teacup-kart' || id === 'emi-faces' || /^emi-(outfit|vehicle)-/.test(id)));
/** Newest live course lease. Shared garage leases never replace the course's material context. */
export const currentArt = () => current;

/** Load the art set. Resolves { tex(id), model(id), row(id), failed[], loaded, wanted } and never rejects.
 * wanted counts the ids asked for minus LATER ids not shipped yet; loaded counts what arrived.
 * onProgress(done, total) runs as each id settles (arrived or failed), for the warming card. */
export async function loadArt(renderer, options = {}) {
  current?.dispose();
  return createArtLibrary(renderer).acquire(options);
}

function artPlan(want, trackId, materialTier) {
  // The terrace is built once with the starting tier, so its other LOD is never drawn.
  if (trackId === 'threshold') want = want.filter(id => id !== (materialTier === 'phone' ? 'threshold-slot-row-lod0' : 'threshold-slot-row-lod1'))
    .concat('glide-threshold-beanie');
  if (trackId === 'mainframe') want = want.filter((id) => !/^(chess-|threshold-|card-(drop|lens|orbit|petal)|slot-cabinet|pocket-watch|stone-threshold)/.test(id))
    .concat('spout-mainframe', 'stone-mainframe', 'glide-mainframe-beanie');
  if (trackId === 'boudoir') want = want.filter((id) => !/^(chess-|threshold-|card-(drop|lens|orbit|petal)|slot-cabinet|pocket-watch|stone-threshold)/.test(id))
    .concat('spout-boudoir', 'stone-boudoir', 'glide-boudoir-beanie');
  if (trackId === 'goonworks') want = want.filter((id) => !/^(chess-|threshold-|card-(drop|lens|orbit|petal)|slot-cabinet|pocket-watch|spiral-disc|stone-threshold|sky-)/.test(id))
    .concat('glide-threshold-beanie');
  const plan = trackId === 'keep' ? keepArtPlan(want, materialTier) : { ids: want, aliases: trackId === 'goonworks' ? { 'glide-keep-beanie': 'glide-threshold-beanie' } : {} };
  const waterIds = ['water-normal', 'water-foam-mask', 'waterfall-sheet', 'waterfall-phase-mask'].map(id => id + (materialTier === 'phone' ? '-phone' : ''));
  return { aliases: plan.aliases, requested: [...new Set(trackId === 'shared' ? plan.ids
    : [...plan.ids, ...waterIds, ...MATERIAL_DETAIL_IDS.map(id => materialDetailId(id, materialTier))])] };
}

/** Renderer-local public art only. Acquire the next course before releasing the old course lease.
 * Geometry/material/texture sources are borrowed and protected; cloned consumer resources are not.
 * A lease releases transient assets, while common garage art stays until library.dispose(). */
export function createArtLibrary(renderer) {
  const raw = renderer.three || renderer;
  if (libraries.has(raw)) return libraries.get(raw);
  const cache = new Map(), owned = new WeakMap(), images = new WeakMap(), leases = new Set();
  let disposed = false, manifestPending = null;
  const ktx2 = new KTX2Loader().setTranscoderPath(TRANSCODER).detectSupport(renderer.three || renderer);
  const gltf = new GLTFLoader().setMeshoptDecoder(MeshoptDecoder).setKTX2Loader(ktx2);
  function own(resource, entry) {
    if (!resource?.dispose || entry.resources.has(resource)) return;
    entry.resources.add(resource);
    let state = owned.get(resource);
    if (!state) {
      state = { refs: 0, dispose: resource.dispose, descriptor: Object.getOwnPropertyDescriptor(resource, 'dispose') };
      state.images = resource.isTexture ? [...new Set([].concat(resource.source?.data || resource.image || []))].filter(image => typeof image?.close === 'function') : [];
      for (const image of state.images) images.set(image, (images.get(image) || 0) + 1);
      owned.set(resource, state);
      // Model clones borrow geometry and texture references. Even accidental consumer disposal
      // during face replacement or world teardown must not invalidate another lease or the garage.
      Object.defineProperty(resource, 'dispose', { configurable: true, writable: true, value() {} });
    }
    state.refs++;
  }
  function ownModel(model, entry) {
    for (const scene of new Set([model.scene, ...(model.scenes || [])])) scene?.traverse(object => {
      own(object.geometry, entry);
      own(object.skeleton, entry);
      for (const material of [].concat(object.material || [])) if (material) {
        own(material, entry);
        for (const value of Object.values(material)) if (value?.isTexture) own(value, entry);
      }
    });
  }
  function evict(entry, force = false) {
    if (!entry.settled || (!force && (entry.refs || (entry.retained && !entry.error)))) return;
    if (cache.get(entry.key) === entry) cache.delete(entry.key);
    for (const resource of entry.resources) {
      const state = owned.get(resource);
      if (!state || --state.refs) continue;
      owned.delete(resource);
      if (state.descriptor) Object.defineProperty(resource, 'dispose', state.descriptor); else delete resource.dispose;
      state.dispose.call(resource);
      for (const image of state.images) {
        const refs = images.get(image) - 1;
        if (refs) images.set(image, refs); else { images.delete(image); image.close(); }
      }
    }
    entry.resources.clear(); entry.value = null;
  }
  function manifest() {
    if (!manifestPending) manifestPending = (async () => {
      try { const response = await fetch(new URL('manifest.json', BASE));
        if (!response.ok) throw Error('manifest HTTP ' + response.status);
        return await response.json();
      } catch { manifestPending = null; return []; }
    })();
    return manifestPending;
  }
  async function decode(r, rows, aniso, entry) {
    const url = new URL(r.file, BASE).href;
      if (r.kind === 'texture') {
        const t = await ktx2.loadAsync(url);
        const [ws, wt] = r.wrap || ['repeat', 'repeat'];
        t.wrapS = WRAP[ws] || RepeatWrapping; t.wrapT = WRAP[wt] || RepeatWrapping;
        t.colorSpace = r.colorSpace === 'srgb' ? SRGBColorSpace : NoColorSpace;
        if ((r.mipLevels || t.mipmaps && t.mipmaps.length || 0) > 1) t.anisotropy = aniso;
        t.needsUpdate = true;
        own(t, entry); return t;
      } else {
        const sidecar = rows.find(x => x.kind === 'vertex-data' && x.sourceFile === r.file);
        let model;
        if (!sidecar) model = await gltf.loadAsync(url);
        else {
          // Fetch optional detail alongside the model, with failure handled immediately even if
          // the model itself fails. Parsing and shade validation still run in their original order.
          const shadeBytes = fetch(new URL(sidecar.file, BASE))
            .then(shade => shade.ok ? shade.arrayBuffer() : null).catch(() => null);
          const response = await fetch(url);
          if (!response.ok) throw new Error('model HTTP ' + response.status);
          const source = await response.arrayBuffer();
          model = await gltf.parseAsync(source, new URL('.', url).href);
          // Optional shading never turns an intact model into a missing asset.
          let applied = false;
          try {
            const bytes = await shadeBytes;
            if (bytes) applied = await attachVertexShade(model, sidecar, source, bytes);
          } catch { /* missing or incompatible detail keeps the accepted model */ }
          entry.shading = applied ? 'applied' : 'skipped';
        }
        ownModel(model, entry); return model;
      }
  }
  const library = {
    isBorrowed: resource => owned.has(resource),
    get stats() { return { entries: cache.size, retained: [...cache.values()].filter(e => e.retained).length, leases: leases.size }; },
    async acquire({ trackId = 'threshold', want = trackId === 'shared' ? GARAGE_ART_IDS : WANT, onProgress = null } = {}) {
      if (disposed) throw Error('Art library disposed');
      const materialTier = Q.name, aniso = Math.max(1, Q.anisotropy || 1);
      const { requested, aliases } = artPlan(want, trackId, materialTier), resolve = id => aliases[id] || id;
      const tex = new Map(), models = new Map(), used = new Set(), failed = [], vertexShading = { applied: [], skipped: [] };
      let released = false, settled = 0;
      const lease = { theme: trackId, tier: materialTier, requested, loaded: 0, wanted: 0, failed, vertexShading,
        isBorrowed: library.isBorrowed,
        detail: id => tex.get(materialDetailId(id, materialTier)) || null,
        tex: id => tex.get(resolve(id)) || null, model: id => models.get(resolve(id)) || null,
        row: id => rows.get(resolve(id)) || null,
        dispose() {
          if (released) return; released = true; leases.delete(lease); liveLeases.delete(lease);
          for (const entry of used) { entry.refs--; evict(entry, disposed); }
          used.clear(); tex.clear(); models.clear();
          if (current === lease) current = [...liveLeases].at(-1) || null;
        },
      };
      lease.release = lease.dispose;
      const rows = new Map(); leases.add(lease);
      const manifestRows = await manifest();
      for (const row of manifestRows) rows.set(row.id, row);
      lease.wanted = requested.filter(id => rows.has(id) || !LATER.has(id)).length;
      await Promise.all(requested.map(async id => {
        try {
          if (disposed || released) return;
          const row = rows.get(id);
          if (!row) { if (!LATER.has(id)) failed.push(id); return; }
          // Texture sampling configuration is immutable per cached texture; geometry is tier-independent.
          const key = row.kind + ':' + row.file + (row.kind === 'texture' ? ':' + JSON.stringify([aniso, row.wrap, row.colorSpace]) : '');
          let entry = cache.get(key);
          if (!entry || entry.error) {
            entry = { key, refs: 0, retained: false, resources: new Set(), settled: false, value: null };
            cache.set(key, entry);
            entry.pending = decode(row, manifestRows, aniso, entry).then(value => {
              entry.value = value; entry.settled = true; evict(entry, disposed); return value;
            }, error => { entry.error = error; entry.settled = true; evict(entry, disposed); return null; });
          }
          if (!used.has(entry)) { used.add(entry); entry.refs++; }
          if (GARAGE_ART_IDS.includes(id)) entry.retained = true;
          const value = await entry.pending;
          if (released || disposed) return;
          if (!value) { failed.push(id); console.warn('[glassroad/art] ' + id + ' failed: ' + entry.error?.message); return; }
          (row.kind === 'texture' ? tex : models).set(id, value);
          if (entry.shading) vertexShading[entry.shading].push(id);
        } finally { settled++; try { onProgress?.(settled, requested.length); } catch { /* the card only */ } }
      }));
      lease.loaded = tex.size + models.size;
      if (!released && !disposed && trackId !== 'shared') { liveLeases.add(lease); current = lease; }
      return lease;
    },
    dispose() {
      if (disposed) return; disposed = true;
      for (const lease of [...leases]) lease.dispose();
      for (const entry of [...cache.values()]) evict(entry, true);
      ktx2.dispose(); libraries.delete(raw);
    },
  };
  libraries.set(raw, library); return library;
}
