import { DEVICE } from './deviceClass.js';
import { Q } from './quality.js';

export const phoneModels = () => DEVICE.name === 'phone' || Q.name === 'phone';
const LIGHT_MODELS = new Set(['toy-pendulum', 'finish-portal', 'slot-cabinet',
  'chess-bishop', 'chess-king', 'chess-knight', 'chess-pawn', 'chess-queen', 'chess-rook',
  'threshold-slot-row', 'keep-colossus-padlock', 'keep-colossus-chainpile', 'keep-portcullis-arch']);
export function phoneModelId(id) {
  return id.endsWith('-lod0') && LIGHT_MODELS.has(id.slice(0, -5)) ? id.slice(0, -1) + '1' : id;
}

// Only swap base geometry. The same skeleton, face, paint, outfits and effects keep their owners.
// Shared reduced geometries live for one racer fleet and are released after every kart restores its source.
export function createKartLods(data) {
  const rows = new Map((Array.isArray(data?.meshes) ? data.meshes : []).filter(row => row && Array.isArray(row.indices)).map(row => [row.name, row]));
  const cache = new Map(), bindings = [];
  function bind(model, seat) {
    if (seat === 0) return null;
    const pairs = [];
    model.traverse(mesh => {
      const source = mesh.geometry, row = rows.get(mesh.name);
      if (!mesh.isMesh || !row || source.attributes.position.count !== row.vertices || source.index?.count !== row.sourceIndices) return;
      if (!row.indices.length || row.indices.length % 3 || row.indices.some(i => !Number.isInteger(i) || i < 0 || i >= row.vertices)) return;
      let low = cache.get(source);
      if (!low) { low = source.clone(); low.setIndex(row.indices); cache.set(source, low); }
      pairs.push({ mesh, source, low });
    });
    let reduced = false;
    const binding = {
      update(distance, enabled = phoneModels()) {
        // A focused hit-camera racer is passed a distance <= kartLodM. Always restore it immediately.
        const next = enabled && distance > Q.kartLodM + (reduced ? 0 : 8);
        if (next === reduced) return;
        reduced = next;
        for (const p of pairs) p.mesh.geometry = next ? p.low : p.source;
      },
      reset() { reduced = false; for (const p of pairs) p.mesh.geometry = p.source; },
      get reduced() { return reduced; },
    };
    bindings.push(binding);
    return binding;
  }
  return { bind, dispose() { for (const b of bindings) b.reset(); for (const g of cache.values()) g.dispose(); cache.clear(); bindings.length = 0; } };
}
