/* Equipped banners sit on their owner's two outer corners, beyond the squares.
 * The source art and geometry stay shared with the attachment cache. Each
 * ornament owns its material copies only. Static props need no motion clock.
 */
import * as THREE from 'three';
import { normalLook } from '../skins/look.js';

export const CORNERS = Object.freeze({ offset: 4.35, floor: -0.078, footprint: 0.50, height: 0.95 });

export function createBoardOrnaments({ scene, attachments }) {
  const group = new THREE.Group();
  group.name = 'pbp-board-ornaments';
  scene.add(group);
  const sides = { w: [], b: [] };
  const shown = { w: null, b: null };
  let revision = 0;
  let disposed = false;

  function clear(side) {
    for (const prop of sides[side]) {
      group.remove(prop);
      prop.traverse((o) => { if (o.isMesh) o.material.dispose(); });
    }
    sides[side] = [];
    shown[side] = null;
  }

  function build(parts, side, x, id) {
    const root = new THREE.Group();
    root.name = 'pbp-corner-' + side + (x < 0 ? '-left' : '-right');
    root.userData = { side, ornament: id };
    const art = new THREE.Group();
    const bounds = new THREE.Box3();
    for (const part of parts) {
      if (!part.geometry) continue;
      part.geometry.computeBoundingBox();
      bounds.union(part.geometry.boundingBox);
      const mesh = new THREE.Mesh(part.geometry, part.material
        ? part.material.clone() : new THREE.MeshStandardMaterial());
      mesh.castShadow = true;
      mesh.receiveShadow = false;
      mesh.raycast = () => {};
      art.add(mesh);
    }
    if (bounds.isEmpty()) return null;
    const size = bounds.getSize(new THREE.Vector3());
    const center = bounds.getCenter(new THREE.Vector3());
    const scale = Math.min(CORNERS.footprint / Math.max(size.x, size.z, 0.001),
      CORNERS.height / Math.max(size.y, 0.001));
    art.position.set(-center.x, -bounds.min.y, -center.z);
    root.add(art);
    root.scale.setScalar(scale);
    root.rotation.y = side === 'b' ? Math.PI : 0;
    root.position.set(x, CORNERS.floor, side === 'w' ? CORNERS.offset : -CORNERS.offset);
    return root;
  }

  async function setLooks(next = {}) {
    if (disposed) return false;
    const turn = ++revision;
    const wanted = { w: normalLook(next.w), b: normalLook(next.b) };
    // Take an old banner down immediately, even while its replacement loads.
    for (const side of ['w', 'b']) if (shown[side] !== wanted[side].banner) clear(side);
    try {
      await Promise.all(['w', 'b'].map((side) => attachments.prepare({ banner: wanted[side].banner })));
    } catch { /* a missing ornament leaves a clear corner */ }
    if (disposed || turn !== revision) return false;
    let changed = false;
    for (const side of ['w', 'b']) {
      const id = wanted[side].banner;
      if (!id || shown[side] === id) continue;
      const parts = attachments.boardPartsFor(wanted[side]);
      for (const x of [-CORNERS.offset, CORNERS.offset]) {
        const prop = build(parts, side, x, id);
        if (prop) { sides[side].push(prop); group.add(prop); }
      }
      if (sides[side].length) { shown[side] = id; changed = true; }
    }
    return changed;
  }

  return {
    group, setLooks,
    debug: () => ({ shown: { ...shown }, corners: { w: sides.w.length, b: sides.b.length } }),
    dispose() {
      disposed = true;
      ++revision;
      clear('w'); clear('b');
      scene.remove(group);
    },
  };
}
