/* render/wardrobe.js - dresses a kart clone in Astra's outfits and vehicles (ART5; render/wardrobeMath.js
 * holds the pure law). Contract (art/models/emi-bot-wardrobe.json, emi-vehicle-variants.json):
 * - reuse the kart clone's 15 bones by exact name with the outfit's own inverse binds, identity local mesh
 *   transform and bind matrix, the new mesh a sibling of the kart mesh; the loaded temporary rig is dropped;
 * - reject an outfit or vehicle when its base hash or exact bone-name set differs from the accepted R4 kart;
 * - a vehicle replaces the cup and saucer: a derived index buffer drops exactly the triangles fully weighted
 *   to cup_body / saucer_body (shared by every kart on a vehicle); the source geometry stays untouched.
 * No clips here: the shared kart mixer moves every piece.
 */
import { SkinnedMesh, Skeleton, Matrix4, BufferGeometry, BufferAttribute, SRGBColorSpace, Color } from 'three/webgpu';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';
import { createVehicleLife } from './vehicleLife.js';
import { racerFinish } from './racerFinish.js';
import { catalogFrom, lookForSeat as pick, sameJoints, cutIndex, SHOP_PIECES } from './wardrobeMath.js';

const json = (f) => fetch(new URL('../art/models/' + f, import.meta.url)).then((r) => (r.ok ? r.json() : null)).catch(() => null);
const [WARDROBE, VARIANTS] = await Promise.all([json('emi-bot-wardrobe.json'), json('emi-vehicle-variants.json')]);

export const VEHICLE_MECHANISMS = await json('vehicle-mechanisms.json');
export const CATALOG = catalogFrom(WARDROBE, VARIANTS);
const SHOP_IDS = new Set([...SHOP_PIECES.outfits, ...Object.values(SHOP_PIECES.umbrellas), ...Object.values(SHOP_PIECES.trails)]);

/** The look for a seat (render/wardrobeMath.js lookForSeat over the loaded catalog); want = a remote player's ids. */
export const lookForSeat = (seat, seed, want) => pick(seat, seed, CATALOG, want);

const I4 = new Matrix4(), WHITE = new Color(1, 1, 1), TC = new Color();
const cutCache = new WeakMap(), mergeCache = new WeakMap();

function skinnedIn(root) { const out = []; root.traverse((o) => { if (o.isSkinnedMesh) out.push(o); }); return out; }

/* Astra's Prize Counter pieces arrive as many small skinned meshes on one skin (a tutu is 31 ribbons). They share
 * the inverse binds, so one merged geometry per material keeps a racer at two or three draws; cached per source
 * scene, shared by every kart. Anything that will not merge keeps its own meshes. */
function mergedSkinned(scene) {
  if (mergeCache.has(scene)) return mergeCache.get(scene);
  const list = skinnedIn(scene), byMat = new Map();
  for (const s of list) { const k = s.material; if (!byMat.has(k)) byMat.set(k, []); byMat.get(k).push(s); }
  let out = list;
  if (list.length > byMat.size) {
    out = [];
    for (const [material, group] of byMat) {
      const g = group.length > 1 ? mergeGeometries(group.map((m) => m.geometry)) : group[0].geometry;
      if (!g) { out = list; break; }
      out.push({ geometry: g, material, skeleton: group[0].skeleton, isSkinnedMesh: true });
    }
  }
  mergeCache.set(scene, out);
  return out;
}

/** The art row agrees with the accepted base kart (hash, bones). */
function rowOk(art, id) {
  const row = art.row ? art.row(id) : null, base = art.row ? art.row('emi-teacup-kart') : null;
  if (!row || !CATALOG.base) return !!row;
  if (row.baseSha256 && row.baseSha256 !== CATALOG.base) return false;
  return !(base && base.sha256 && base.sha256 !== CATALOG.base);
}

/** Bind every skinned mesh of a loaded outfit / vehicle to the kart's bones; null when rejected. */
function rebind(art, id, kartMesh, material) {
  const g = art.model ? art.model(id) : null;
  if (!g || !kartMesh || !rowOk(art, id)) return null;
  const byName = new Map(kartMesh.skeleton.bones.map((b) => [b.name, b]));
  const meshes = [];
  for (const s of (SHOP_IDS.has(id) ? mergedSkinned(g.scene) : skinnedIn(g.scene))) {
    const names = s.skeleton.bones.map((b) => b.name);
    if (!sameJoints(names, CATALOG.joints) || names.some((n) => !byName.has(n))) {
      console.warn('[glassroad/wardrobe] ' + id + ' rejected: its bones are not the accepted R4 set');
      return null;
    }
    const m = new SkinnedMesh(s.geometry, material ? material(racerFinish(s.material)) : racerFinish(s.material));
    // Same space as the kart mesh (identity local transform and bind), so it shares the kart's culling: a racer's
    // fixed kart sphere (render/dressing.js kartCullSphere), unculled where the kart is (garage, menu rider).
    m.name = id; m.frustumCulled = !!(kartMesh.frustumCulled && kartMesh.boundingSphere);
    if (m.frustumCulled) m.boundingSphere = kartMesh.boundingSphere.clone();
    m.bind(new Skeleton(names.map((n) => byName.get(n)), s.skeleton.boneInverses), I4);
    meshes.push(m);
  }
  return meshes.length ? meshes : null;
}

/** The kart's kart_surface skinned mesh (the one a vehicle cuts) and its parent. */
function kartSurface(model) {
  let m = null;
  model.traverse((o) => { if (o.isSkinnedMesh && o.material && o.material.name === 'kart_surface' && !m) m = o; });
  if (!m) model.traverse((o) => { if (o.isSkinnedMesh && !m) m = o; });
  return m;
}

/** The kart geometry without the cup and saucer (cached per source geometry); null when the cut breaks the contract. */
function cutGeometry(mesh) {
  const src = mesh.geometry;
  if (cutCache.has(src)) return cutCache.get(src);
  let out = null;
  const idx = src.index, j = src.getAttribute('skinIndex'), w = src.getAttribute('skinWeight');
  const remove = CATALOG.removeJoints.map((n) => mesh.skeleton.bones.findIndex((b) => b.name === n)).filter((k) => k >= 0);
  if (idx && j && w && remove.length === CATALOG.removeJoints.length) {
    const c = cutIndex(idx.array, flat(j), flat(w), remove);
    if (c.mixed === 0 && (!CATALOG.removedTriangles || c.removed === CATALOG.removedTriangles)) {
      out = new BufferGeometry();
      for (const [k, a] of Object.entries(src.attributes)) out.setAttribute(k, a);   // shared, never copied
      out.setIndex(new BufferAttribute(c.index, 1));
      out.boundingSphere = src.boundingSphere; out.boundingBox = src.boundingBox;
    } else console.warn('[glassroad/wardrobe] vehicle cut refused: removed ' + c.removed + ', mixed ' + c.mixed);
  }
  cutCache.set(src, out);
  return out;
}

/** A menu rider shares the accepted rig but leaves the original vessel on the carousel. */
export function riderOnly(model) {
  const mesh=kartSurface(model),geometry=mesh&&cutGeometry(mesh);
  if(!geometry)return false;
  mesh.geometry=geometry;return true;
}

/** Four components per vertex as plain floats (getX..getW undo normalisation and interleaving, as meshopt packs them). */
function flat(a) {
  const f = new Float32Array(a.count * 4);
  for (let i = 0; i < a.count; i++) { f[i * 4] = a.getX(i); f[i * 4 + 1] = a.getY(i); f[i * 4 + 2] = a.getZ(i); f[i * 4 + 3] = a.getW(i); }
  return f;
}

/**
 * Dress a kart clone: look = { outfit, vehicle } ids (null = EMI's own), plus the Prize Counter `umbrella` and
 * `trail` ids (render/wardrobeMath.js SHOP_PIECES). tint = sRGB rgb of the rival's cup (vehicle porcelain mixed
 * toward it by tintMix) or null. Returns { outfit, vehicle, umbrella, trail, tris, draws, dispose,
 * showUmbrella(on) }: the lace umbrella starts hidden (the race opens it for a glide, the garage shows it), the
 * trail ribbons sit behind the cup in kart space. dispose takes the pieces off again and gives the kart its cup
 * back (a re-dress).
 */
export function dressKart(art, model, look, { tint = null, tintMix = 0.6 } = {}) {
  const kart = kartSurface(model), res = { outfit: null, vehicle: null, umbrella: null, trail: null, tris: 0, draws: 0, dispose() {}, showUmbrella() {} };
  if (!kart || !art || !look) return res;
  const parent = kart.parent, owned = [], added = [], srcGeo = kart.geometry;
  let vehicleLife = null;
  if (look.vehicle) {
    const cut = cutGeometry(kart);
    const meshes = cut ? rebind(art, look.vehicle, kart, (m) => {
      if (!tint || !/porcelain/.test(m.name || '')) return m;
      const c = m.clone(); owned.push(c);
      TC.setRGB(tint[0], tint[1], tint[2], SRGBColorSpace);
      c.color.copy(WHITE).lerp(TC, tintMix);
      return c;
    }) : null;
    if (meshes) {
      kart.geometry = cut;
      for (const m of meshes) { m.castShadow = kart.castShadow; parent.add(m); added.push(m); res.tris += triCount(m.geometry); }
      res.vehicle = look.vehicle; res.draws += meshes.length;
      const vehicleId=look.vehicle.replace('emi-vehicle-', '');
      vehicleLife = createVehicleLife(meshes, {...VEHICLE_MECHANISMS?.vehicles?.[vehicleId],vehicleId},{art});
      res.life = vehicleLife; res.draws += vehicleLife.metrics.draws || 0; res.tris += vehicleLife.metrics.accentTriangles || 0;
    }
  }
  if (look.outfit) {
    const meshes = rebind(art, look.outfit, kart, null);   // the loaded materials, shared by every kart
    if (meshes) {
      // the cup casts the kart's shadow; the outfit's own would cost a draw per kart for a sliver of cloth
      for (const m of meshes) { m.userData.noCast = true; parent.add(m); added.push(m); res.tris += triCount(m.geometry); }
      res.outfit = look.outfit; res.draws += meshes.length;
    }
  }
  const umbrellaId = look.umbrella && SHOP_PIECES.umbrellas[look.umbrella];
  if (umbrellaId && Object.hasOwn(SHOP_PIECES.umbrellas, look.umbrella)) {
    const meshes = rebind(art, umbrellaId, kart, null);
    if (meshes) {
      for (const m of meshes) { m.userData.noCast = true; m.visible = false; parent.add(m); added.push(m); res.tris += triCount(m.geometry); }
      res.umbrella = look.umbrella; res.draws += meshes.length;
      res.showUmbrella = (on) => { for (const m of meshes) m.visible = !!on; };
    }
  }
  const trailId = look.trail && Object.hasOwn(SHOP_PIECES.trails, look.trail) ? SHOP_PIECES.trails[look.trail] : null;
  const trailSource = trailId && art.model ? art.model(trailId) : null;
  let trail = null;
  if (trailSource && rowOk(art, trailId)) {
    trail = trailSource.scene.clone(true);   // static ribbons in kart model space; geometry and materials stay shared
    trail.traverse((o) => { if (o.isMesh) { o.castShadow = false; o.userData.kartTrail = true; o.frustumCulled = !!kart.frustumCulled; res.tris += triCount(o.geometry); res.draws++; } });
    parent.add(trail); res.trail = look.trail;
  }
  res.dispose = () => {
    vehicleLife?.dispose();
    if (trail) parent.remove(trail);
    for (const m of added) { parent.remove(m); m.skeleton.dispose(); }
    for (const m of owned) m.dispose();
    kart.geometry = srcGeo;
  };
  return res;
}

function triCount(g) { return g.index ? g.index.count / 3 : (g.getAttribute('position').count / 3); }
