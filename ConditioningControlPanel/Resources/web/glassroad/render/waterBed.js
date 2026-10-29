// Only the render copy becomes a submerged bed. Physical support stays intact.
export function waterBedDepth(s, field, length, depth = 1.05) {
  const runs = field.runs;
  if (!runs?.length) return depth;
  const run = runs.find(r => s >= r.fromS && s <= r.toS);
  if (!run) return 0;
  const joinedAtStart = run.fromS === 0 && runs.some(r => r.toS >= length);
  const joinedAtEnd = run.toS >= length && runs.some(r => r.fromS === 0);
  const edge = Math.min(joinedAtStart ? Infinity : s - run.fromS, joinedAtEnd ? Infinity : run.toS - s);
  const t = Math.max(0, Math.min(1, edge / Math.min(6, (run.toS - run.fromS) / 2)));
  return depth * t * t * (3 - 2 * t);
}

export function waterBedPositions(positions, uvs, field, track, depth = 1.05) {
  const out = new Float32Array(positions);
  for (let i = 0; i < out.length / 3; i++) {
    // Include the exact road-edge vertices beyond the atlas's small rail inset.
    // A road run crossing the lap seam keeps continuous UVs above lengthM.
    // Field runs use wrapped lap distance, so normalize before testing membership.
    const rawS = uvs[i * 2 + 1], length = track.lengthM;
    const s = length > 0 ? ((rawS % length) + length) % length : rawS;
    if (!field.atlasUV(s, 0).wet) continue;
    const sink = waterBedDepth(s, field, length, depth);
    const f = track.frameAt(s);
    // Bottom and edge shading normals point away from the physical surface.
    // Move the entire slab down along its support normal, never those face normals.
    const x = f.b.reduce((sum, b, k) => sum + (positions[i * 3 + k] - f.p[k]) * b, 0);
    const n = track.surfaceAt ? track.surfaceAt(s, x).n : f.n;
    for (let k = 0; k < 3; k++) out[i * 3 + k] -= n[k] * sink;
  }
  return out;
}
