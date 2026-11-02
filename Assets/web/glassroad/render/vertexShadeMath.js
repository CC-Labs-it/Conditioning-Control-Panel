/* Exact-source vertex shading. Validation is atomic: one mismatch rejects the whole sidecar. */
export const VERTEX_SHADE_ATTRIBUTE = 'ccpVertexShade';

/** decoded contains parser associations and POSITION counts, never names or traversal order. */
export function vertexShadePlan(row, hash, byteLength, decoded) {
  if (!row || row.kind !== 'vertex-data' || row.encoding !== 'uint8-unorm' || row.byteStride !== 2
    || row.channels?.ao !== 0 || row.channels?.curvature !== 1
    || !/^[a-f0-9]{64}$/i.test(hash || '') || hash.toLowerCase() !== row.sourceSha256?.toLowerCase()
    || byteLength !== row.vertexBytes || !Array.isArray(row.primitives) || !row.primitives.length
    || !Array.isArray(decoded) || !decoded.length) return null;
  const expected = new Map();
  let end = 0, vertices = 0;
  for (const p of row.primitives) {
    const key = `${p.meshIndex}:${p.primitiveIndex}`;
    if (!Number.isInteger(p.meshIndex) || p.meshIndex < 0 || !Number.isInteger(p.primitiveIndex) || p.primitiveIndex < 0
      || !Number.isInteger(p.vertexCount) || p.vertexCount <= 0 || expected.has(key)
      || p.byteOffset !== end || p.byteLength !== p.vertexCount * 2) return null;
    expected.set(key, p); end += p.byteLength; vertices += p.vertexCount;
  }
  if (end !== byteLength || vertices !== row.vertexCount) return null;
  const seen = new Set(), plan = [];
  for (let i = 0; i < decoded.length; i++) {
    const d = decoded[i], key = `${d.meshIndex}:${d.primitiveIndex}`, p = expected.get(key);
    if (!p || d.vertexCount !== p.vertexCount || d.accessorCount !== p.vertexCount || d.hasShade) return null;
    seen.add(key); plan.push({ index: i, byteOffset: p.byteOffset, byteLength: p.byteLength, neutral: !!p.neutral });
  }
  return seen.size === expected.size ? plan : null;
}
