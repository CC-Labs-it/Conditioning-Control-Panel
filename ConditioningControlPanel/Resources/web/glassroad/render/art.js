/* render/art.js - loads the wave 1 art set (art/manifest.json, art/ART.md) for buildWorld.
 * KTX2 textures go through the basis transcoder, GLBs through GLTFLoader with the meshopt decoder.
 * Every asset is optional: a failed or missing one comes back null and the world keeps its greybox
 * stand-in, so a broken file never blocks a race. Masks are linear data; colour textures are sRGB.
 */
import { RepeatWrapping, ClampToEdgeWrapping, SRGBColorSpace, NoColorSpace } from 'three/webgpu';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { KTX2Loader } from 'three/addons/loaders/KTX2Loader.js';
import { MeshoptDecoder } from 'three/addons/libs/meshopt_decoder.module.js';

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
];
/** Wanted before they ship: missing from the manifest is quiet (not in failed); a broken file still counts. */
const LATER = new Set(['emi-faces']);

let current = null;
/** The last art set loadArt resolved (null before, or with ?art=0): modules built after boot's
 * loadArt read it when their caller passes no art. */
export const currentArt = () => current;

/** Load the art set. Resolves { tex(id), model(id), row(id), failed[], loaded, wanted } and never rejects.
 * wanted counts the ids asked for minus LATER ids not shipped yet; loaded counts what arrived. */
export async function loadArt(renderer, { want = WANT } = {}) {
  const rows = new Map(), tex = new Map(), models = new Map(), failed = [];
  let manifest = [];
  try {
    const res = await fetch(new URL('manifest.json', BASE));
    if (res.ok) manifest = await res.json();
  } catch { /* no art: greybox */ }
  for (const r of manifest) rows.set(r.id, r);

  // renderer: our createRenderer wrapper or a bare WebGPURenderer (already init-ed)
  const ktx2 = new KTX2Loader().setTranscoderPath(TRANSCODER).detectSupport(renderer.three || renderer);
  const gltf = new GLTFLoader().setMeshoptDecoder(MeshoptDecoder).setKTX2Loader(ktx2);

  await Promise.all(want.map(async (id) => {
    const r = rows.get(id);
    if (!r) { if (!LATER.has(id)) failed.push(id); return; }
    const url = new URL(r.file, BASE).href;
    try {
      if (r.kind === 'texture') {
        const t = await ktx2.loadAsync(url);
        const [ws, wt] = r.wrap || ['repeat', 'repeat'];
        t.wrapS = WRAP[ws] || RepeatWrapping; t.wrapT = WRAP[wt] || RepeatWrapping;
        t.colorSpace = r.colorSpace === 'srgb' ? SRGBColorSpace : NoColorSpace;
        t.needsUpdate = true;
        tex.set(id, t);
      } else {
        models.set(id, await gltf.loadAsync(url));
      }
    } catch (e) {
      failed.push(id);
      console.warn('[glassroad/art] ' + id + ' failed: ' + (e && e.message));
    }
  }));
  ktx2.dispose();
  const wanted = want.filter((id) => rows.has(id) || !LATER.has(id)).length;
  return current = {
    loaded: tex.size + models.size,
    wanted,
    tex: (id) => tex.get(id) || null,
    model: (id) => models.get(id) || null,
    row: (id) => rows.get(id) || null,
    failed,
    dispose() { for (const t of tex.values()) t.dispose(); },
  };
}
