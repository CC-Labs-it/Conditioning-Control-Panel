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
/** The last art set loadArt resolved (null before, or with ?art=0): modules built after boot's
 * loadArt read it when their caller passes no art. */
export const currentArt = () => current;

/** Load the art set. Resolves { tex(id), model(id), row(id), failed[], loaded, wanted } and never rejects.
 * wanted counts the ids asked for minus LATER ids not shipped yet; loaded counts what arrived.
 * onProgress(done, total) runs as each id settles (arrived or failed), for the warming card. */
export async function loadArt(renderer, { want = WANT, trackId = 'threshold', onProgress = null } = {}) {
  // Evict the previous theme before loading its replacements, including a quality-tier change.
  if (current) { current.dispose(); current = null; }
  const rows = new Map(), tex = new Map(), models = new Map(), failed = [];
  const materialTier = Q.name;
  const plan = trackId === 'keep' ? keepArtPlan(want, materialTier) : { ids: want, aliases: {} };
  const resolve = (id) => plan.aliases[id] || id;
  const requested = [...new Set([...plan.ids, ...MATERIAL_DETAIL_IDS.map((id) => materialDetailId(id, materialTier))])];
  const vertexShading = { applied: [], skipped: [] };
  let manifest = [];
  try {
    const res = await fetch(new URL('manifest.json', BASE));
    if (res.ok) manifest = await res.json();
  } catch { /* no art: greybox */ }
  for (const r of manifest) rows.set(r.id, r);

  // renderer: our createRenderer wrapper or a bare WebGPURenderer (already init-ed)
  // anisotropic filtering from the START tier, set once here (changing it mid-race re-uploads every texture);
  // the renderer caps it at what the GPU offers
  const aniso = Math.max(1, Q.anisotropy || 1);
  const ktx2 = new KTX2Loader().setTranscoderPath(TRANSCODER).detectSupport(renderer.three || renderer);
  const gltf = new GLTFLoader().setMeshoptDecoder(MeshoptDecoder).setKTX2Loader(ktx2);

  let settled = 0;
  const tick = () => { settled++; if (onProgress) { try { onProgress(settled, requested.length); } catch { /* the card only */ } } };
  await Promise.all(requested.map(async (id) => {
    try { await loadOne(id); } finally { tick(); }
  }));
  async function loadOne(id) {
    const r = rows.get(id);
    if (!r) { if (!LATER.has(id)) failed.push(id); return; }
    const url = new URL(r.file, BASE).href;
    try {
      if (r.kind === 'texture') {
        const t = await ktx2.loadAsync(url);
        const [ws, wt] = r.wrap || ['repeat', 'repeat'];
        t.wrapS = WRAP[ws] || RepeatWrapping; t.wrapT = WRAP[wt] || RepeatWrapping;
        t.colorSpace = r.colorSpace === 'srgb' ? SRGBColorSpace : NoColorSpace;
        if ((r.mipLevels || t.mipmaps && t.mipmaps.length || 0) > 1) t.anisotropy = aniso;
        t.needsUpdate = true;
        tex.set(id, t);
      } else {
        const sidecar = manifest.find((x) => x.kind === 'vertex-data' && x.sourceFile === r.file);
        if (!sidecar) models.set(id, await gltf.loadAsync(url));
        else {
          const response = await fetch(url);
          if (!response.ok) throw new Error('model HTTP ' + response.status);
          const source = await response.arrayBuffer();
          const model = await gltf.parseAsync(source, new URL('.', url).href);
          // Optional shading never turns an intact model into a missing asset.
          let applied = false;
          try {
            const shade = await fetch(new URL(sidecar.file, BASE));
            if (shade.ok) applied = await attachVertexShade(model, sidecar, source, await shade.arrayBuffer());
          } catch { /* missing or incompatible detail keeps the accepted model */ }
          vertexShading[applied ? 'applied' : 'skipped'].push(id);
          models.set(id, model);
        }
      }
    } catch (e) {
      failed.push(id);
      console.warn('[glassroad/art] ' + id + ' failed: ' + (e && e.message));
    }
  }
  ktx2.dispose();
  const wanted = requested.filter((id) => rows.has(id) || !LATER.has(id)).length;
  return current = {
    theme: trackId, tier: materialTier, requested,
    loaded: tex.size + models.size,
    wanted,
    vertexShading,
    detail: (id) => tex.get(materialDetailId(id, materialTier)) || null,
    tex: (id) => tex.get(resolve(id)) || null,
    model: (id) => models.get(id) || null,
    row: (id) => rows.get(resolve(id)) || null,
    failed,
    dispose() { for (const t of tex.values()) t.dispose(); },
  };
}
