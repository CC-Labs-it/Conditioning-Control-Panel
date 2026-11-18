// A continuous height field bends the tile tops. Shared border samples keep
// neighbouring colours joined, including their lighting normals.
import * as THREE from 'three';

const SUBDIVISIONS = 12, EDGE = 8 * SUBDIVISIONS, STRIDE = EDGE + 1;
const STEP = 1 / SUBDIVISIONS;
const smooth = (a, b, value) => {
  const t = Math.max(0, Math.min(1, (value - a) / (b - a)));
  return t * t * (3 - 2 * t);
};

export function createBoardWaves({ view, pieces, waves, life = 1.15 }) {
  const heights = new Float32Array(STRIDE * STRIDE);
  const tiles = [];
  const markers = new Map();
  let bent = false, disposed = false, peak = 0;

  function prepare() {
    if (tiles.length) return;
    for (const mesh of view.squares?.values() || []) {
      const geometry = new THREE.BoxGeometry(1, .16, 1, SUBDIVISIONS, 1, SUBDIVISIONS);
      const position = geometry.attributes.position, normal = geometry.attributes.normal;
      position.setUsage(THREE.DynamicDrawUsage);
      normal.setUsage(THREE.DynamicDrawUsage);
      const tops = [];
      for (let i = 0; i < position.count; i++) {
        if (position.getY(i) < 0) continue;
        const x = Math.round((position.getX(i) + mesh.position.x + 4) * SUBDIVISIONS);
        const z = Math.round((position.getZ(i) + mesh.position.z + 4) * SUBDIVISIONS);
        tops.push({ i, x, z, grid: z * STRIDE + x, normal: normal.getY(i) > .5 });
      }
      // The board is never culled or picked using stale undeformed bounds.
      geometry.computeBoundingBox();
      geometry.boundingBox.max.y += .13;
      geometry.computeBoundingSphere();
      geometry.boundingSphere.radius += .13;
      tiles.push({ mesh, original: mesh.geometry, geometry, tops, dirty: false });
      mesh.geometry = geometry;
    }
  }

  function writeTiles() {
    peak = 0;
    for (const tile of tiles) {
      const { position, normal } = tile.geometry.attributes;
      let changed = false;
      for (const v of tile.tops) {
        const h = heights[v.grid];
        peak = Math.max(peak, Math.abs(h));
        changed ||= h !== 0;
        position.setY(v.i, .08 + h);
        if (!v.normal) continue;
        const dx = (heights[v.z * STRIDE + Math.min(EDGE, v.x + 1)]
          - heights[v.z * STRIDE + Math.max(0, v.x - 1)]) / (STEP * (v.x === 0 || v.x === EDGE ? 1 : 2));
        const dz = (heights[Math.min(EDGE, v.z + 1) * STRIDE + v.x]
          - heights[Math.max(0, v.z - 1) * STRIDE + v.x]) / (STEP * (v.z === 0 || v.z === EDGE ? 1 : 2));
        const length = Math.hypot(dx, 1, dz);
        normal.setXYZ(v.i, -dx / length, 1 / length, -dz / length);
      }
      if (changed || tile.dirty) {
        position.needsUpdate = true;
        normal.needsUpdate = true;
      }
      tile.dirty = changed;
    }
  }

  function sample(x, z) {
    if (Math.abs(x) > 4 || Math.abs(z) > 4) return 0;
    const gx = (x + 4) * SUBDIVISIONS, gz = (z + 4) * SUBDIVISIONS;
    const x0 = Math.floor(gx), z0 = Math.floor(gz), x1 = Math.min(EDGE, x0 + 1), z1 = Math.min(EDGE, z0 + 1);
    const tx = gx - x0, tz = gz - z0;
    return (heights[z0 * STRIDE + x0] * (1 - tx) + heights[z0 * STRIDE + x1] * tx) * (1 - tz)
      + (heights[z1 * STRIDE + x0] * (1 - tx) + heights[z1 * STRIDE + x1] * tx) * tz;
  }

  function releaseMarkers() {
    for (const [mesh, entry] of markers) { mesh.geometry = entry.original; entry.geometry.dispose(); }
    markers.clear();
  }

  function drapeMarkers() {
    const root = view.boardGroup?.getObjectByName('pbp-markers');
    if (!root) return;
    // Legal dots and last-move ink follow the same surface, never disappear
    // under a crest. This affects only their vertices, never their hit targets.
    root.updateWorldMatrix(true, true);
    for (const entry of markers.values()) entry.seen = false;
    root.traverse(mesh => {
      if (!mesh.isMesh) return;
      let entry = markers.get(mesh);
      if (!entry) {
        const original = mesh.geometry, p = original.parameters;
        const geometry = original.type === 'PlaneGeometry'
          ? new THREE.PlaneGeometry(p.width, p.height, 12, 12).rotateX(-Math.PI / 2) : original.clone();
        geometry.attributes.position.setUsage(THREE.DynamicDrawUsage);
        entry = { original, geometry, base: geometry.attributes.position.array.slice() };
        markers.set(mesh, entry);
        mesh.geometry = geometry;
      }
      entry.seen = true;
      const position = entry.geometry.attributes.position, e = mesh.matrixWorld.elements;
      for (let i = 0; i < position.count; i++) {
        const x = entry.base[i * 3], z = entry.base[i * 3 + 2];
        position.setY(i, entry.base[i * 3 + 1] + sample(e[0] * x + e[8] * z + e[12], e[2] * x + e[10] * z + e[14]) / Math.max(.001, e[5]));
      }
      position.needsUpdate = true;
    });
    for (const [mesh, entry] of markers) if (!entry.seen) {
      mesh.geometry = entry.original;
      entry.geometry.dispose();
      markers.delete(mesh);
    }
  }

  function update(clock) {
    if (disposed) return;
    heights.fill(0);
    let active = false;
    for (const wave of waves) {
      const age = clock - wave.z;
      if (age <= 0 || age >= life || wave.w <= 0) continue;
      active = true;
      const gain = wave.w;
      const reach = .90 + .32 * gain;
      const radius = .28 + age * (.82 + .20 * gain);
      const width = .19 + .025 * gain;
      const amplitude = .077 * gain * smooth(0, .10, age) * (1 - smooth(.20, life, age));
      const cx = wave.x, cz = -wave.y;
      const x0 = Math.max(0, Math.floor((cx - reach + 4) * SUBDIVISIONS));
      const x1 = Math.min(EDGE, Math.ceil((cx + reach + 4) * SUBDIVISIONS));
      const z0 = Math.max(0, Math.floor((cz - reach + 4) * SUBDIVISIONS));
      const z1 = Math.min(EDGE, Math.ceil((cz + reach + 4) * SUBDIVISIONS));
      for (let z = z0; z <= z1; z++) for (let x = x0; x <= x1; x++) {
        const wx = x * STEP - 4, wz = z * STEP - 4;
        const distance = Math.hypot(wx - cx, wz - cz);
        if (distance >= reach) continue;
        const q = (distance - radius) / width;
        // A low crest followed by a shallow trough, with finite local reach.
        const fold = (1 - 2 * q * q) * Math.exp(-q * q);
        const edge = smooth(0, .22, 4 - Math.max(Math.abs(wx), Math.abs(wz)));
        heights[z * STRIDE + x] += amplitude * fold * edge * (1 - smooth(reach * .7, reach, distance));
      }
    }
    if (!active && !bent) return;
    if (active) prepare();
    // The surface stays flat under a planted base. Animations, contact shadows
    // and picking keep their own coordinates; the board bends around the feet.
    for (const piece of pieces?.pieces?.values() || []) {
      const p = piece.position;
      if (!piece.visible || piece.userData.held || Math.abs(p.y) > .12) continue;
      const contact = 1 - smooth(.02, .12, Math.abs(p.y));
      const x0 = Math.max(0, Math.floor((p.x - .52 + 4) * SUBDIVISIONS));
      const x1 = Math.min(EDGE, Math.ceil((p.x + .52 + 4) * SUBDIVISIONS));
      const z0 = Math.max(0, Math.floor((p.z - .52 + 4) * SUBDIVISIONS));
      const z1 = Math.min(EDGE, Math.ceil((p.z + .52 + 4) * SUBDIVISIONS));
      for (let z = z0; z <= z1; z++) for (let x = x0; x <= x1; x++) {
        const distance = Math.hypot(x * STEP - 4 - p.x, z * STEP - 4 - p.z);
        heights[z * STRIDE + x] *= 1 - contact * (1 - smooth(.30, .52, distance));
      }
    }
    // Multiple simultaneous contacts cannot fold the surface through its bed.
    for (let i = 0; i < heights.length; i++) heights[i] = Math.max(-.048, Math.min(.125, heights[i]));
    writeTiles();
    if (active) drapeMarkers();
    else releaseMarkers();
    bent = active;
  }

  function reset() {
    heights.fill(0);
    peak = 0;
    bent = false;
    releaseMarkers();
    for (const tile of tiles) {
      tile.mesh.geometry = tile.original;
      tile.geometry.dispose();
    }
    tiles.length = 0;
  }

  return {
    update, reset, sample,
    debug: () => ({ peak, active: bent, tiles: tiles.length, subdivisions: SUBDIVISIONS }),
    dispose() { if (!disposed) { reset(); disposed = true; } },
  };
}
