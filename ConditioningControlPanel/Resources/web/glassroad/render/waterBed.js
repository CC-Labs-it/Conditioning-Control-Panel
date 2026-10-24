// Only the render copy becomes a submerged bed. Physical support stays intact.
export function waterBedPositions(positions, uvs, field, track, depth = 1.05) {
  const out = new Float32Array(positions);
  for (let i = 0; i < out.length / 3; i++) {
    // Include the exact road-edge vertices beyond the atlas's small rail inset.
    const s = uvs[i * 2 + 1];
    if (!field.atlasUV(s, 0).wet) continue;
    const f = track.frameAt(s);
    // Bottom and edge shading normals point away from the physical surface.
    // Move the entire slab down along its support normal, never those face normals.
    const x = f.b.reduce((sum, b, k) => sum + (positions[i * 3 + k] - f.p[k]) * b, 0);
    const n = track.surfaceAt ? track.surfaceAt(s, x).n : f.n;
    for (let k = 0; k < 3; k++) out[i * 3 + k] -= n[k] * depth;
  }
  return out;
}
