/* ============================================================================
 * board/attachments.js - the clothes, banners and insignias a man wears.
 *
 * Art is Astra's (assets/skins/, registry.json beside it): one glb per item
 * per piece type, rooted at `attachment_<item>_<piece>` with a
 * `socket_<piece>` marker, authored in the approved piece's own space
 * ("identity under approved piece root"). So an attachment goes under the
 * piece root with no offset at all, and the sculpted piece itself is never
 * touched or rebuilt from anything here.
 *
 * Every mesh is baked into the attachment root's space (only geometry travels
 * out of the file, as pieces.js does for the jewels) and meshes that share a
 * material are merged, so a cape is a handful of draws and not sixteen. The
 * flex shader reads position.y as height up the piece, which baked geometry
 * satisfies: the cape bends with the body.
 *
 *   createAttachments({ base }) -> { prepare(look) -> Promise, partsFor(type, look) -> [{geometry, material}] }
 *
 * prepare() loads what a look needs (cached, once per item and piece);
 * partsFor() is synchronous and answers only what is already loaded, so a
 * build never waits on the network. A missing or broken file is a quiet no.
 * ==========================================================================*/

import * as THREE from 'three';
import { lookIds, slotOf } from '../skins/look.js';

export const PIECE_WORD = { p: 'pawn', n: 'knight', b: 'bishop', r: 'rook', q: 'queen', k: 'king' };
const TYPES = Object.keys(PIECE_WORD);
const VERSION = 'skins-1030';

export function createAttachments({ base = './assets/skins/' } = {}) {
  let registry = null;          // Promise<Map "id|piece" -> file>
  let loader = null;
  let merge = null;
  const loaded = new Map();     // "id|piece" -> parts[] (resolved)
  const pending = new Map();    // "id|piece" -> Promise

  function readRegistry() {
    if (!registry) {
      registry = fetch(base + 'registry.json?v=' + VERSION)
        .then((r) => (r.ok ? r.json() : { items: [] }))
        .catch(() => ({ items: [] }))
        .then((j) => {
          const map = new Map();
          for (const it of (j && Array.isArray(j.items) ? j.items : [])) {
            if (it && typeof it.id === 'string' && typeof it.piece === 'string' && typeof it.file === 'string'
              && /^[\w.-]+\.glb$/.test(it.file)) map.set(it.id + '|' + it.piece, it.file);
          }
          return map;
        });
    }
    return registry;
  }

  async function bake(url) {
    if (!loader) {
      const mod = await import('three/addons/loaders/GLTFLoader.js');
      loader = new mod.GLTFLoader();
    }
    if (!merge) {
      try { merge = (await import('three/addons/utils/BufferGeometryUtils.js')).mergeGeometries; }
      catch { merge = () => null; }
    }
    const gltf = await loader.loadAsync(url);
    gltf.scene.updateMatrixWorld(true);
    const byMat = new Map();
    gltf.scene.traverse((o) => {
      if (!o.isMesh || !o.geometry) return;
      const mat = Array.isArray(o.material) ? o.material[0] : o.material;
      const geo = o.geometry.clone().applyMatrix4(o.matrixWorld);
      if (!byMat.has(mat)) byMat.set(mat, []);
      byMat.get(mat).push(geo);
    });
    const parts = [];
    for (const [material, geos] of byMat) {
      let joined = null;
      if (geos.length > 1) { try { joined = merge(geos, false); } catch { joined = null; } }
      if (joined) { joined.computeBoundingBox(); parts.push({ geometry: joined, material }); continue; }
      for (const g of geos) { g.computeBoundingBox(); parts.push({ geometry: g, material }); }
    }
    return parts;
  }

  function loadOne(id, piece) {
    const key = id + '|' + piece;
    if (loaded.has(key)) return Promise.resolve(loaded.get(key));
    if (pending.has(key)) return pending.get(key);
    const p = readRegistry().then((map) => {
      const file = map.get(key);
      return file ? bake(base + file + '?v=' + VERSION) : [];
    }).then((parts) => turnBadge(id, parts)).catch(() => []).then((parts) => {
      loaded.set(key, parts);
      pending.delete(key);
      return parts;
    });
    pending.set(key, p);
    return p;
  }

  /** An insignia is authored on the man's front (-Z); it belongs on his back, the side his owner's camera
   *  sees (skins/mount.js). Half a turn about the up axis; pieces.js seats it against the body. */
  function turnBadge(id, parts) {
    const slot = slotOf(id);
    for (const p of parts) {
      p.slot = slot;
      if (slot !== 'insignia') continue;
      p.geometry.rotateY(Math.PI);
      p.geometry.computeBoundingBox();
    }
    return parts;
  }

  /** Load everything a look needs, every piece type. Resolves true when anything new landed. */
  async function prepare(look) {
    const jobs = [];
    for (const id of lookIds(look)) {
      for (const t of TYPES) {
        const key = id + '|' + PIECE_WORD[t];
        if (!loaded.has(key)) jobs.push(loadOne(id, PIECE_WORD[t]));
      }
    }
    if (!jobs.length) return false;
    await Promise.all(jobs);
    return true;
  }

  /** What one man of this type wears for this look, from what is loaded now. */
  function partsFor(type, look) {
    const piece = PIECE_WORD[type];
    if (!piece || !look) return [];
    const out = [];
    for (const id of lookIds(look)) {
      const parts = loaded.get(id + '|' + piece);
      if (parts) out.push(...parts);
    }
    return out;
  }

  return { prepare, partsFor, debug: () => ({ loaded: [...loaded.keys()], pending: [...pending.keys()] }) };
}

/** One worn mesh, ready for a piece root: its own material copy, so the fades and the flex are his. */
export function wornMesh(part, receiveShadow) {
  const m = new THREE.Mesh(part.geometry, part.material ? part.material.clone() : undefined);
  m.castShadow = true;
  m.receiveShadow = receiveShadow;
  m.userData.worn = true;
  return m;
}
