import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';

const json = JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8'));

// 200 ms per 5 km of course (Threshold is 8.4 km since the 2026-10-28 reshape), best of two loads: the first one on a
// busy CI runner carries JIT warm-up and ran 163 to 318 ms on the same commit (2026-10-28)
test('mesh: builds in under 200 ms per 5 km', () => {
  let best = Infinity, lengthM = 0;
  for (let i = 0; i < 2; i++) {
    const t0 = performance.now();
    const tr = loadTrack(json);
    void tr.meshes;
    best = Math.min(best, performance.now() - t0); lengthM = tr.lengthM;
  }
  const budget = 200 * Math.max(1, lengthM / 5000);
  assert.ok(best < budget, 'load + mesh ' + best.toFixed(1) + ' ms (budget ' + budget.toFixed(0) + ' ms for ' + Math.round(lengthM) + ' m)');
});

const tr = loadTrack(json);
const M = tr.meshes;

test('mesh: every MeshData is valid and winds toward its normals', () => {
  for (const [name, m] of Object.entries(M)) {
    const V = m.positions.length / 3;
    assert.ok(m.positions instanceof Float32Array && m.normals instanceof Float32Array && m.uvs instanceof Float32Array && m.indices instanceof Uint32Array, name);
    assert.equal(m.normals.length, V * 3);
    assert.equal(m.uvs.length, V * 2);
    assert.ok(V > 0 && m.indices.length > 0 && m.indices.length % 3 === 0, name);
    for (const x of m.positions) assert.ok(Number.isFinite(x), name);
    for (const x of m.uvs) assert.ok(Number.isFinite(x), name);
    for (let v = 0; v < V; v++) assert.ok(Math.abs(Math.hypot(m.normals[v * 3], m.normals[v * 3 + 1], m.normals[v * 3 + 2]) - 1) < 1e-4, name + ' normal');
    const p = (i, k) => m.positions[i * 3 + k];
    for (let i = 0; i < m.indices.length; i += 3) {
      const a = m.indices[i], b = m.indices[i + 1], c = m.indices[i + 2];
      assert.ok(a < V && b < V && c < V, name + ' index range');
      const e1 = [0, 1, 2].map((k) => p(b, k) - p(a, k)), e2 = [0, 1, 2].map((k) => p(c, k) - p(a, k));
      const cr = [e1[1] * e2[2] - e1[2] * e2[1], e1[2] * e2[0] - e1[0] * e2[2], e1[0] * e2[1] - e1[1] * e2[0]];
      const area = Math.hypot(...cr);
      assert.ok(area > 1e-6, name + ' degenerate triangle ' + i / 3);
      const nd = cr[0] * m.normals[a * 3] + cr[1] * m.normals[a * 3 + 1] + cr[2] * m.normals[a * 3 + 2];
      assert.ok(nd > 0, name + ' winding ' + i / 3);
    }
  }
});

test('mesh: road top uv is lateral and s, with a gap at the jump', () => {
  const m = M.road, j = tr.jumps[0];
  let tops = 0;
  for (let v = 0; v < m.positions.length / 3; v++) {
    const P = [m.positions[v * 3], m.positions[v * 3 + 1], m.positions[v * 3 + 2]];
    const q = tr.query(P[0], P[1], P[2]);
    const up = m.normals[v * 3] * q.normal[0] + m.normals[v * 3 + 1] * q.normal[1] + m.normals[v * 3 + 2] * q.normal[2];
    if (Math.abs(q.height) < 1e-3 && up > 0.999) {
      tops++;
      assert.ok(Math.abs(m.uvs[v * 2] - q.lateral) < 0.01, 'u = lateral');
      const sv = m.uvs[v * 2 + 1] % tr.lengthM;
      assert.ok(Math.abs(sv - q.s) < 0.05 || Math.abs(Math.abs(sv - q.s) - tr.lengthM) < 0.05, 'v = s');
    }
    assert.ok(!(q.s > j.s + 0.5 && q.s < j.landS - 0.5 && q.seg === j.node), 'road vertex inside the gap');
  }
  assert.ok(tops > 1000);
});

test('mesh: rails only on railed sides, decals on the road', () => {
  const r = M.rails;
  for (let v = 0; v < r.positions.length / 3; v++) {
    const q = tr.query(r.positions[v * 3], r.positions[v * 3 + 1], r.positions[v * 3 + 2]);
    // stones wave: the court's rail under the bridge resolves to the (open) field deck 10 m above it; the court probe
    // keeps the ribbon for a point outside the court's edge (the outer rail face), so judge only a rail on its own road
    if (Math.abs(q.height) > 2.5) continue;
    const fl = tr.nodeFlags(q.seg);
    assert.ok(!fl.includes('open') || q.segT < 1e-3 || q.segT > 1 - 1e-3, 'rail vertex on an open segment ' + q.seg);
    assert.ok(r.uvs[v * 2] >= 0 && r.uvs[v * 2] <= 1.2001);   // u = metres up, the rail is 1.2 m (EDGES lane)
  }
  assert.ok(M.pads.positions.length / 3 >= tr.boosts.length * 4);
  for (let v = 0; v < M.pads.positions.length / 3; v++) {
    const q = tr.query(M.pads.positions[v * 3], M.pads.positions[v * 3 + 1], M.pads.positions[v * 3 + 2]);
    assert.ok(Math.abs(q.height - 0.02) < 1e-3 && q.onRoad);
    const padLen = M.pads.padDesign[v * 4 + 1];
    assert.ok(M.pads.uvs[v * 2 + 1] >= 0 && M.pads.uvs[v * 2 + 1] <= padLen + 1e-6);
  }
  assert.equal(M.jumps.positions.length / 3 >= 4, true);
});

test('track: driveline quads cover the loop', () => {
  const d = tr.driveline;
  assert.equal(d.count, d.quads + 1);
  assert.equal(d.s[0], 0);
  assert.ok(Math.abs(d.s[d.count - 1] - tr.lengthM) < 1e-9);
  for (let r = 1; r < d.count; r++) assert.ok(d.s[r] > d.s[r - 1]);
  const aerialQuads = Array.from({ length: d.quads }, (_, i) => i)
    .filter(i => tr.aerials.some(a => d.s[i] >= a.gapS0 && d.s[i + 1] <= a.gapS1)).length;
  assert.equal(d.gap.reduce((a, b) => a + b, 0), d.rows + aerialQuads);
});
