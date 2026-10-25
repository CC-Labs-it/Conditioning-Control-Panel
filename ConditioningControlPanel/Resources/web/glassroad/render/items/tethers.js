/* render/items/tethers.js - placeholder Tether chains: one line strip per live link in the item view
 * (view.tethers, sim/items.js). Astra's chain prop replaces the line; the shape stays tetherMath.js. */
import { BufferAttribute, BufferGeometry, DynamicDrawUsage, LineBasicMaterial, LineSegments } from 'three/webgpu';
import { TETHER_LOOK, tetherPoints } from './tetherMath.js';
import { MOTE_TINT } from './look.js';

const MAX = 8;

export function createTethers(root) {
  const per = TETHER_LOOK.links * 2, pos = new BufferAttribute(new Float32Array(MAX * per * 3), 3).setUsage(DynamicDrawUsage);
  const geo = new BufferGeometry().setAttribute('position', pos);
  const [r, g, b] = MOTE_TINT.tether.split(',').map(Number);
  const mat = new LineBasicMaterial({ color: (r << 16) | (g << 8) | b, transparent: true, opacity: 0.9, depthWrite: false });
  const lines = new LineSegments(geo, mat);
  lines.frustumCulled = false; lines.renderOrder = 2; root.add(lines);
  const pts = new Float32Array((TETHER_LOOK.links + 1) * 3);
  let time = 0;
  return {
    update(view, dt) {
      time += Math.max(0, dt || 0);
      const rows = (view && view.tethers) || [];
      let n = 0;
      for (const row of rows.slice(0, MAX)) {
        tetherPoints(row.from, row.to, row.k, time, pts);
        for (let i = 0; i < TETHER_LOOK.links; i++) {
          pos.array.set(pts.subarray(i * 3, i * 3 + 6), n * 6);
          n++;
        }
      }
      geo.setDrawRange(0, n * 2);
      pos.needsUpdate = true;
      lines.visible = n > 0;
    },
    clear() { geo.setDrawRange(0, 0); lines.visible = false; },
    dispose() { root.remove(lines); geo.dispose(); mat.dispose(); },
  };
}
