import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { vertexShadePlan } from '../render/vertexShadeMath.js';
import { MATERIAL_DETAIL_IDS, materialDetailId, surfaceFinish } from '../render/materialDetailMath.js';

const hash = 'a'.repeat(64);
const fixture = () => ({ kind: 'vertex-data', encoding: 'uint8-unorm', byteStride: 2, channels: { ao: 0, curvature: 1 },
  sourceSha256: hash, vertexBytes: 14, vertexCount: 7, primitives: [
    { meshIndex: 2, primitiveIndex: 0, vertexCount: 4, byteOffset: 0, byteLength: 8 },
    { meshIndex: 2, primitiveIndex: 1, vertexCount: 3, byteOffset: 8, byteLength: 6, neutral: true },
  ] });
const decoded = () => [
  { meshIndex: 2, primitiveIndex: 1, vertexCount: 3, accessorCount: 3 },
  { meshIndex: 2, primitiveIndex: 0, vertexCount: 4, accessorCount: 4 },
];

test('vertex shading follows parser primitive identities even when scene traversal is reversed', () => {
  assert.deepEqual(vertexShadePlan(fixture(), hash, 14, decoded()), [
    { index: 0, byteOffset: 8, byteLength: 6, neutral: true },
    { index: 1, byteOffset: 0, byteLength: 8, neutral: false },
  ]);
});
test('vertex shading rejects changed source bytes and truncated or extra binary data', () => {
  assert.equal(vertexShadePlan(fixture(), 'b'.repeat(64), 14, decoded()), null);
  assert.equal(vertexShadePlan(fixture(), '', 14, decoded()), null);
  for (const len of [0, 13, 15]) assert.equal(vertexShadePlan(fixture(), hash, len, decoded()), null);
});
test('vertex shading atomically rejects missing association, count changes, wrong accessor and duplicate attachment', () => {
  for (const patch of [{ meshIndex: undefined }, { primitiveIndex: 8 }, { vertexCount: 2 }, { accessorCount: 2 }, { hasShade: true }]) {
    const d = decoded(); Object.assign(d[1], patch);
    assert.equal(vertexShadePlan(fixture(), hash, 14, d), null, JSON.stringify(patch));
  }
  assert.equal(vertexShadePlan(fixture(), hash, 14, decoded().slice(0, 1)), null);
});
test('vertex shading refuses overlapping, gapped, duplicated or reordered manifest primitive ranges', () => {
  for (const patch of [{ byteOffset: 6 }, { byteOffset: 10 }, { primitiveIndex: 0 }, { byteLength: 5 }]) {
    const r = fixture(); Object.assign(r.primitives[1], patch);
    assert.equal(vertexShadePlan(r, hash, 14, decoded()), null);
  }
  const r = fixture(); r.primitives.reverse();
  assert.equal(vertexShadePlan(r, hash, 14, decoded()), null);
});
test('vertex shading supports repeated nodes referencing the same decoded primitive', () => {
  const d = decoded(); d.push({ ...d[0] });
  assert.equal(vertexShadePlan(fixture(), hash, 14, d).length, 3);
});
test('all shipped sidecars match their immutable GLB, exact length and original primitive counts', () => {
  const base = new URL('../art/', import.meta.url);
  const rows = JSON.parse(readFileSync(new URL('manifest.json', base), 'utf8'));
  const sides = rows.filter((r) => r.kind === 'vertex-data');
  assert.equal(sides.length, 16);
  for (const row of sides) {
    const source = readFileSync(new URL(row.sourceFile, base));
    const sourceHash = createHash('sha256').update(source).digest('hex');
    const data = readFileSync(new URL(row.file, base));
    const jsonLength = source.readUInt32LE(12);
    const gltf = JSON.parse(source.subarray(20, 20 + jsonLength).toString('utf8'));
    const d = row.primitives.map((p) => ({ meshIndex: p.meshIndex, primitiveIndex: p.primitiveIndex,
      vertexCount: p.vertexCount,
      accessorCount: gltf.accessors[gltf.meshes[p.meshIndex].primitives[p.primitiveIndex].attributes.POSITION].count }));
    assert.ok(vertexShadePlan(row, sourceHash, data.byteLength, d), row.id);
    for (const p of row.primitives.filter((p) => p.neutral)) {
      for (let at = p.byteOffset; at < p.byteOffset + p.byteLength; at += 2) {
        assert.equal(data[at], 255, row.id + ' neutral AO');
        assert.equal(data[at + 1], 128, row.id + ' neutral curvature');
      }
    }
  }
});
test('only the selected material texture tier is loaded, and matte surfaces have broader highlights than polished metal', () => {
  assert.equal(MATERIAL_DETAIL_IDS.length, 2);
  for (const id of MATERIAL_DETAIL_IDS) {
    assert.equal(materialDetailId(id, 'phone'), id + '-phone');
    assert.equal(materialDetailId(id, 'mid'), id);
  }
  const matte = surfaceFinish({ roughness: 0.8 }), metal = surfaceFinish({ roughness: 0.25, metalness: 1 });
  assert.ok(metal.exponent > matte.exponent * 2);
  assert.ok(metal.gain > matte.gain);
});
