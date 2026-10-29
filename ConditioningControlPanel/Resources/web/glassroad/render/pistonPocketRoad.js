import { Mesh, BufferGeometry, BufferAttribute, DynamicDrawUsage } from 'three/webgpu';
import { surfaceId } from '../track/schema.js';
import { movingPoseAt } from '../track/movingHazards.js';

// All six covers share one road draw. Their closed vertices follow the real road, including banks.
export function createPistonPocketRoad(track, material) {
  const positions = [], normals = [], uvs = [], surfaces = [], flows = [], indices = [], panels = [];
  for (const row of track.moving?.traps || []) {
    const p = row.pocket;
    if (!p) continue;
    const start = positions.length / 3, cols = Math.max(2, Math.ceil(p.w / 2));
    const from = p.s - p.len / 2, to = p.s + p.len / 2;
    const stations = [from, ...Array.from(track.driveline.s).filter(s => s > from && s < to), to];
    for (const lower of [false, true]) {
      const layer = positions.length / 3;
      for (const s of stations) for (let col = 0; col <= cols; col++) {
        const center = track.frameAt(s), lo = Math.max(-center.w / 2, p.minX), hi = Math.min(center.w / 2, p.maxX);
        const x = lo + (hi - lo) * col / cols, f = track.surfaceAt(s, x);
        positions.push(...f.p.map((v, k) => v - f.n[k] * (lower ? .24 : 0)));
        normals.push(...f.n.map(v => lower ? -v : v)); uvs.push(x, s);
        surfaces.push(surfaceId(center.surface)); flows.push(...track.flowAt(center.seg, center.segT, [0, 0]));
      }
      for (let r = 0; r < stations.length - 1; r++) for (let c = 0; c < cols; c++) {
        const a = layer + r * (cols + 1) + c, b = a + 1, d = b + cols + 1, cc = d - 1;
        indices.push(...(lower ? [a, b, d, a, d, cc] : [a, d, b, a, cc, d]));
      }
    }
    panels.push({ row, start, count: positions.length / 3 - start, shift: NaN, y: NaN });
  }
  const geometry = new BufferGeometry(), rest = new Float32Array(positions);
  const position = new BufferAttribute(rest.slice(), 3).setUsage(DynamicDrawUsage);
  geometry.setAttribute('position', position);
  geometry.setAttribute('normal', new BufferAttribute(new Float32Array(normals), 3));
  geometry.setAttribute('uv', new BufferAttribute(new Float32Array(uvs), 2));
  geometry.setAttribute('surface', new BufferAttribute(new Float32Array(surfaces), 1));
  geometry.setAttribute('flow', new BufferAttribute(new Float32Array(flows), 2));
  geometry.setAttribute('forkWidth', new BufferAttribute(new Float32Array(surfaces.length), 1));
  geometry.setAttribute('roadEdge', new BufferAttribute(new Float32Array(surfaces.length).fill(1), 1));
  geometry.setIndex(indices);
  const mesh = new Mesh(geometry, material); mesh.name = 'piston-sliding-road-covers'; mesh.frustumCulled = false;
  return { mesh, panels, update(timeS) {
    let dirty = false;
    for (const panel of panels) {
      const pose = movingPoseAt(track, panel.row, timeS), shift = pose.panelShift || 0, y = pose.panelY || 0;
      if (shift === panel.shift && y === panel.y) continue;
      panel.shift = shift; panel.y = y; dirty = true;
      for (let i = panel.start; i < panel.start + panel.count; i++)
        for (let k = 0; k < 3; k++) position.array[i * 3 + k] = rest[i * 3 + k] + panel.row.b[k] * shift + panel.row.n[k] * y;
      position.addUpdateRange(panel.start * 3, panel.count * 3);
    }
    if (dirty) position.needsUpdate = true;
  }, dispose() { mesh.removeFromParent(); geometry.dispose(); } };
}
